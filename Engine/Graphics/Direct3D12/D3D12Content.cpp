// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "D3D12Content.h"
#include "D3D12Core.h"
#include "Utilities/IOStream.h"
#include "Content/ContentToEngine.h"
#include "D3D12GPass.h"
#include "D3D12Upload.h"

namespace primal::graphics::d3d12::content {
namespace {

struct pso_id
{
    id::id_type gpass_pso_id{ id::invalid_id };
    id::id_type depth_pso_id{ id::invalid_id };
};

struct submesh_view
{
    D3D12_VERTEX_BUFFER_VIEW    position_buffer_view{};
    D3D12_VERTEX_BUFFER_VIEW    element_buffer_view{};
    D3D12_INDEX_BUFFER_VIEW     index_buffer_view{};
    D3D_PRIMITIVE_TOPOLOGY      primitive_topology;
    u32                         elements_type{};
    u32                         resource_index{ u32_invalid_id }; // index of the mesh buffer resource.
};

struct d3d12_render_item
{
    id::id_type entity_id;
    id::id_type submesh_gpu_id;
    id::id_type material_id;
    id::id_type pso_id;
    id::id_type depth_pso_id;
};

utl::free_list<ID3D12Resource*>                 mesh_buffers{};
utl::free_list<submesh_view>                    submesh_views{};
std::mutex                                      mesh_mutex{};

utl::free_list<d3d12_texture>                   textures;
utl::free_list<u32>                             descriptor_indices;
std::mutex                                      texture_mutex{};

utl::vector<ID3D12RootSignature*>               root_signatures;
utl::vector<ID3D12CommandSignature*>            cmd_signatures;
std::unordered_map<u64, id::id_type>            mtl_rs_map; // maps a material's type and shader flags to an index in the array of root signatures.
utl::free_list<std::unique_ptr<u8[]>>           materials;
std::mutex                                      material_mutex{};

utl::free_list<d3d12_render_item>               render_items;
utl::free_list<std::unique_ptr<id::id_type[]>>  render_item_ids;
std::mutex                                      render_item_mutex{};

utl::vector<ID3D12PipelineState*>               pipeline_states;
std::unordered_map<u64, id::id_type>            pso_map;
std::mutex                                      pso_mutex{};

struct {
    utl::vector<primal::content::lod_offset>    lod_offsets;
    utl::vector<id::id_type>                    geometry_ids;
} frame_cache;

id::id_type create_root_signature(material_type::type type, shader_flags::flags flags);

class d3d12_material_stream
{
public:
    DISABLE_COPY_AND_MOVE(d3d12_material_stream);
    explicit d3d12_material_stream(u8 *const material_buffer)
        :_buffer{ material_buffer }
    {
        initialize();
    }

    explicit d3d12_material_stream(std::unique_ptr<u8[]>& material_buffer, material_init_info info)
    {
        assert(!material_buffer);

        u32 shader_count{ 0 };
        u32 flags{ 0 };
        for (u32 i{ 0 }; i < shader_type::count; ++i)
        {
            if (id::is_valid(info.shader_ids[i]))
            {
                ++shader_count;
                flags |= (1 << i);
            }
        }

        assert(shader_count && flags);

        const u32 buffer_size{
            sizeof(material_type::type) +               // material type
            sizeof(shader_flags::flags) +               // shader flags
            sizeof(id::id_type) +                       // root signature id
            sizeof(u32) +                               // texture count
            sizeof(material_surface) +                  // PBR material properties
            sizeof(id::id_type) * shader_count +        // shader ids
            (sizeof(id::id_type) + sizeof(u32)) * info.texture_count // texture ids and descriptor indices (maybe 0 if no textures used).
        };

        material_buffer = std::make_unique<u8[]>(buffer_size);
        _buffer = material_buffer.get();
        u8 *const buffer{ _buffer };

        *(material_type::type*)buffer = info.type;
        *(shader_flags::flags*)&buffer[shader_flags_index] = (shader_flags::flags)flags;
        *(id::id_type*)&buffer[root_signature_index] = create_root_signature(info.type, (shader_flags::flags)flags);
        *(u32*)&buffer[texture_count_index] = info.texture_count;
        *(material_surface*)&buffer[material_surface_index] = info.surface;

        initialize();

        if (info.texture_count)
        {
            memcpy(_texture_ids, info.texture_ids, info.texture_count * sizeof(id::id_type));
            texture::get_descriptor_indices(_texture_ids, info.texture_count, _descriptor_indices);
        }

        u32 shader_index{ 0 };
        for (u32 i{ 0 }; i < shader_type::count; ++i)
        {
            if (id::is_valid(info.shader_ids[i]))
            {
                _shader_ids[shader_index] = info.shader_ids[i];
                ++shader_index;
            }
        }

        assert(shader_index == (u32)_mm_popcnt_u32(_shader_flags));
    }

    [[nodiscard]] constexpr u32 texture_count() const { return _texture_count; }
    [[nodiscard]] constexpr material_type::type material_type() const { return _type; }
    [[nodiscard]] constexpr shader_flags::flags shader_flags() const { return _shader_flags; }
    [[nodiscard]] constexpr id::id_type root_signature_id() const { return _root_signature_id; }
    [[nodiscard]] constexpr id::id_type* texture_ids() const { return _texture_ids; }
    [[nodiscard]] constexpr u32* descriptor_indices() const { return _descriptor_indices; }
    [[nodiscard]] constexpr id::id_type* shader_ids() const { return _shader_ids; }
    [[nodiscard]] constexpr material_surface* surface() const { return _material_surface; }

private:
    void initialize()
    {
        assert(_buffer);
        u8 *const buffer{ _buffer };

        _type = *(material_type::type*)buffer;
        _shader_flags = *(shader_flags::flags*)&buffer[shader_flags_index];
        _root_signature_id = *(id::id_type*)&buffer[root_signature_index];
        _texture_count = *(u32*)&buffer[texture_count_index];
        _material_surface = (material_surface*)&buffer[material_surface_index];

        _shader_ids = (id::id_type*)&buffer[material_surface_index + sizeof(material_surface)];
        _texture_ids = _texture_count ? &_shader_ids[_mm_popcnt_u32(_shader_flags)] : nullptr;
        _descriptor_indices = _texture_count ? (u32*)&_texture_ids[_texture_count] : nullptr;
    }

    constexpr static u32    shader_flags_index{ sizeof(material_type::type) };
    constexpr static u32    root_signature_index{ shader_flags_index + sizeof(shader_flags::flags) };
    constexpr static u32    texture_count_index{ root_signature_index + sizeof(id::id_type) };
    constexpr static u32    material_surface_index{ texture_count_index + sizeof(u32) };

    u8*                     _buffer;
    material_surface*       _material_surface;
    id::id_type*            _texture_ids;
    u32*                    _descriptor_indices;
    id::id_type*            _shader_ids;
    id::id_type             _root_signature_id;
    u32                     _texture_count;
    material_type::type     _type;
    shader_flags::flags     _shader_flags;
};

constexpr D3D_PRIMITIVE_TOPOLOGY
get_d3d_primitive_topology(primitive_topology::type type)
{
    assert(type < primitive_topology::count);

    switch (type)
    {
    case primitive_topology::point_list:     return D3D_PRIMITIVE_TOPOLOGY_POINTLIST;
    case primitive_topology::line_list:      return D3D_PRIMITIVE_TOPOLOGY_LINELIST;
    case primitive_topology::line_strip:     return D3D_PRIMITIVE_TOPOLOGY_LINESTRIP;
    case primitive_topology::triangle_list:  return D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST;
    case primitive_topology::triangle_strip: return D3D_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP;
    }

    return D3D_PRIMITIVE_TOPOLOGY_UNDEFINED;
}

constexpr D3D12_PRIMITIVE_TOPOLOGY_TYPE
get_d3d_primitive_topology_type(D3D_PRIMITIVE_TOPOLOGY topology)
{
    switch (topology)
    {
    case D3D_PRIMITIVE_TOPOLOGY_POINTLIST: return D3D12_PRIMITIVE_TOPOLOGY_TYPE_POINT;
    case D3D_PRIMITIVE_TOPOLOGY_LINELIST:
    case D3D_PRIMITIVE_TOPOLOGY_LINESTRIP: return D3D12_PRIMITIVE_TOPOLOGY_TYPE_LINE;
    case D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST:
    case D3D_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP: return D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
    }

    return D3D12_PRIMITIVE_TOPOLOGY_TYPE_UNDEFINED;
}


constexpr D3D12_ROOT_SIGNATURE_FLAGS
get_root_signature_flags(shader_flags::flags flags)
{
    D3D12_ROOT_SIGNATURE_FLAGS default_flags{ d3dx::d3d12_root_signature_desc::default_flags };
    if (flags & shader_flags::vertex)           default_flags &= ~D3D12_ROOT_SIGNATURE_FLAG_DENY_VERTEX_SHADER_ROOT_ACCESS;
    if (flags & shader_flags::hull)             default_flags &= ~D3D12_ROOT_SIGNATURE_FLAG_DENY_HULL_SHADER_ROOT_ACCESS;
    if (flags & shader_flags::domain)           default_flags &= ~D3D12_ROOT_SIGNATURE_FLAG_DENY_DOMAIN_SHADER_ROOT_ACCESS;
    if (flags & shader_flags::geometry)         default_flags &= ~D3D12_ROOT_SIGNATURE_FLAG_DENY_GEOMETRY_SHADER_ROOT_ACCESS;
    if (flags & shader_flags::pixel)            default_flags &= ~D3D12_ROOT_SIGNATURE_FLAG_DENY_PIXEL_SHADER_ROOT_ACCESS;
    if (flags & shader_flags::amplification)    default_flags &= ~D3D12_ROOT_SIGNATURE_FLAG_DENY_AMPLIFICATION_SHADER_ROOT_ACCESS;
    if (flags & shader_flags::mesh)             default_flags &= ~D3D12_ROOT_SIGNATURE_FLAG_DENY_MESH_SHADER_ROOT_ACCESS;
    return default_flags;
}

void
create_command_signature(material_type::type type, u32 root_sig_id)
{
    assert(root_sig_id < root_signatures.size());
    assert(root_sig_id == cmd_signatures.size());

    ID3D12CommandSignature* cmd_signature{ nullptr };

    switch (type)
    {
    case material_type::opaque:
    {
        using params = gpass::opaque_root_parameter;
        d3dx::d3d12_indirect_argument_desc args[params::count + 2]{};

        args[params::global_shader_data].as_cbv(params::global_shader_data);
        args[params::per_object_data].as_cbv(params::per_object_data);
        args[params::position_buffer].as_srv(params::position_buffer);
        args[params::element_buffer].as_srv(params::element_buffer);
        args[params::material_data].as_srv(params::material_data);
        args[params::directional_lights].as_srv(params::directional_lights);
        args[params::cullable_lights].as_srv(params::cullable_lights);
        args[params::light_grid].as_srv(params::light_grid);
        args[params::light_index_list].as_srv(params::light_index_list);

        args[params::count].as_index_buffer_view();

        args[params::count + 1].as_draw_indexed();

        cmd_signature = d3dx::d3d12_command_signature_desc
        {
            sizeof(gpass::draw_indexed_indirect_command), _countof(args), &args[0]
        }.create(root_signatures[root_sig_id]);
    }
    break;
    }

    assert(cmd_signature);
    cmd_signatures.emplace_back(cmd_signature);
    assert(root_sig_id == cmd_signatures.size() - 1);
}

id::id_type
create_root_signature(material_type::type type, shader_flags::flags flags)
{
    assert(type < material_type::count);
    static_assert(sizeof(type) == sizeof(u32) && sizeof(flags) == sizeof(u32));
    const u64 key{ ((u64)type << 32) | flags };
    auto pair = mtl_rs_map.find(key);
    if (pair != mtl_rs_map.end())
    {
        assert(pair->first == key);
        return pair->second;
    }

    ID3D12RootSignature* root_signature{ nullptr };

    switch (type)
    {
    case material_type::opaque:
    {
        using params = gpass::opaque_root_parameter;
        d3dx::d3d12_root_parameter parameters[params::count]{};

        D3D12_SHADER_VISIBILITY buffer_visibility{};
        D3D12_SHADER_VISIBILITY data_visibility{};

        if (flags & shader_flags::vertex)
        {
            buffer_visibility = D3D12_SHADER_VISIBILITY_VERTEX;
            data_visibility = D3D12_SHADER_VISIBILITY_VERTEX;
        }
        else if (flags & shader_flags::mesh)
        {
            buffer_visibility = D3D12_SHADER_VISIBILITY_MESH;
            data_visibility = D3D12_SHADER_VISIBILITY_MESH;
        }

        if ((flags & shader_flags::hull) || (flags & shader_flags::geometry) ||
            (flags & shader_flags::amplification))
        {
            buffer_visibility = D3D12_SHADER_VISIBILITY_ALL;
            data_visibility = D3D12_SHADER_VISIBILITY_ALL;
        }

        if ((flags & shader_flags::pixel) || (flags & shader_flags::compute))
        {
            data_visibility = D3D12_SHADER_VISIBILITY_ALL;
        }

        parameters[params::global_shader_data].as_cbv(D3D12_SHADER_VISIBILITY_ALL, 0);
        parameters[params::per_object_data].as_cbv(data_visibility, 1);
        parameters[params::position_buffer].as_srv(buffer_visibility, 0);
        parameters[params::element_buffer].as_srv(buffer_visibility, 1);
        parameters[params::material_data].as_srv(D3D12_SHADER_VISIBILITY_PIXEL, 2); // TODO: needs to be visible to any stages that need to sample textures.
        parameters[params::directional_lights].as_srv(D3D12_SHADER_VISIBILITY_PIXEL, 3);
        parameters[params::cullable_lights].as_srv(D3D12_SHADER_VISIBILITY_PIXEL, 4);
        parameters[params::light_grid].as_srv(D3D12_SHADER_VISIBILITY_PIXEL, 5);
        parameters[params::light_index_list].as_srv(D3D12_SHADER_VISIBILITY_PIXEL, 6);
        const D3D12_STATIC_SAMPLER_DESC samplers[]
        {
            d3dx::static_sampler(d3dx::sampler_state.static_point, 0, 0, D3D12_SHADER_VISIBILITY_PIXEL),
            d3dx::static_sampler(d3dx::sampler_state.static_linear, 1, 0, D3D12_SHADER_VISIBILITY_PIXEL),
            d3dx::static_sampler(d3dx::sampler_state.static_anisotropic, 2, 0, D3D12_SHADER_VISIBILITY_PIXEL),
        };

        root_signature = d3dx::d3d12_root_signature_desc
        {
            &parameters[0], _countof(parameters), get_root_signature_flags(flags),
            &samplers[0], _countof(samplers)
        }.create();
    }
    break;
    }

    assert(root_signature);
    const id::id_type id{ (id::id_type)root_signatures.size() };
    root_signatures.emplace_back(root_signature);
    mtl_rs_map[key] = id;
    NAME_D3D12_OBJECT_INDEXED(root_signature, key, L"GPass Root Signature - key");

    create_command_signature(type, id);

    return id;
}

id::id_type
create_pso_if_needed(const u8* const stream_ptr, u64 aligned_stream_size, [[maybe_unused]] bool is_depth)
{
    const u64 key{ math::calc_crc32_u64(stream_ptr, aligned_stream_size) };

    { // Lock scope to check if PSO already exists
        std::lock_guard lock{ pso_mutex };
        auto pair = pso_map.find(key);

        if (pair != pso_map.end())
        {
            assert(pair->first == key);
            return pair->second;
        }
    }

    // Creating a new PSO is lock-free
    d3dx::d3d12_pipeline_state_subobject_stream *const stream{ (d3dx::d3d12_pipeline_state_subobject_stream *const)stream_ptr };
    ID3D12PipelineState* pso{ d3dx::create_pipeline_state(stream, sizeof(d3dx::d3d12_pipeline_state_subobject_stream)) };

    { // Lock scope to add the new PSO's pointer and id (I know, scoping is not necessary, but it's more obvious this way.)
        std::lock_guard lock{ pso_mutex };
        const id::id_type id{ (u32)pipeline_states.size() };
        pipeline_states.emplace_back(pso);
        NAME_D3D12_OBJECT_INDEXED(pipeline_states.back(), key,
                                  is_depth ? L"Depth-only Pipeline State Object - key" : L"GPass Pipeline State Object - key");

        pso_map[key] = id;
        return id;
    }
}

#pragma intrinsic(_BitScanForward)
shader_type::type
get_shader_type(u32 flag)
{
    assert(flag);
    unsigned long index;
    _BitScanForward(&index, flag);
    return (shader_type::type)index;
}

D3D12_SHADER_BYTECODE
get_shader_byte_code(shader_type::type shader_type, u32 elements_type, const d3d12_material_stream& material, u32 shader_index)
{
    // NOTE: each type of shader may have keys that are generated from different properties of the submesh or material.
    //       At the moment, we only have different kinds of vertex shaders depending on elements_type 
    const u32 key{ shader_type == shader_type::vertex ? elements_type : u32_invalid_id };
    primal::content::compiled_shader_ptr shader{ primal::content::get_shader(material.shader_ids()[shader_index], key) };
    assert(shader);
    return { shader->byte_code(), shader->byte_code_size() };
}

pso_id
create_pso(id::id_type material_id, D3D12_PRIMITIVE_TOPOLOGY primitive_topology, u32 elements_type)
{
    constexpr u64 aligned_stream_size{ math::align_size_up<sizeof(u64)>(sizeof(d3dx::d3d12_pipeline_state_subobject_stream)) };
    u8 *const stream_ptr{ (u8 *const)alloca(aligned_stream_size) };
    ZeroMemory(stream_ptr, aligned_stream_size);
    new (stream_ptr) d3dx::d3d12_pipeline_state_subobject_stream{};

    d3dx::d3d12_pipeline_state_subobject_stream& stream{ *(d3dx::d3d12_pipeline_state_subobject_stream *const)stream_ptr };
    D3D12_SHADER_BYTECODE depth_prepass_vertex_shader{};

    { // Lock materials
        std::lock_guard lock{ material_mutex };
        const d3d12_material_stream material{ materials[material_id].get() };

        D3D12_RT_FORMAT_ARRAY rt_array{};
        rt_array.NumRenderTargets = 1;
        rt_array.RTFormats[0] = gpass::main_buffer_format;

        stream.render_target_formats = rt_array;
        stream.root_signature = root_signatures[material.root_signature_id()];
        stream.primitive_topology = get_d3d_primitive_topology_type(primitive_topology);
        stream.depth_stencil_format = gpass::depth_buffer_format;
        stream.rasterizer = d3dx::rasterizer_state.backface_cull;
        stream.depth_stencil1 = d3dx::depth_state.reversed_readonly;
        stream.blend = d3dx::blend_state.disabled;

        const shader_flags::flags flags{ material.shader_flags() };
        const bool use_position_only_for_depth_prepass{ !(flags & ((1 << shader_type::amplification) | (1 << shader_type::geometry) |
                                                                   (1 << shader_type::hull) | (1 << shader_type::domain))) };
        D3D12_SHADER_BYTECODE shaders[shader_type::count]{};
        u32 shader_index{ 0 };
        for (u32 i{ 0 }; i < shader_type::count; ++i)
        {
            const u32 flag{ flags & (1 << i) };

            if (flag)
            {

                const shader_type::type shader_type{ get_shader_type(flag) };
                shaders[i] = get_shader_byte_code(shader_type, elements_type, material, shader_index);

                ++shader_index;
            }
        }

        stream.vs = shaders[shader_type::vertex];
        stream.gs = shaders[shader_type::geometry];
        stream.hs = shaders[shader_type::hull];
        stream.ds = shaders[shader_type::domain];
        stream.cs = shaders[shader_type::compute];
        stream.ps = shaders[shader_type::pixel];
        stream.as = shaders[shader_type::amplification];
        stream.ms = shaders[shader_type::mesh];

        depth_prepass_vertex_shader = use_position_only_for_depth_prepass ?
            get_shader_byte_code(shader_type::vertex, 0, material, 0) : shaders[shader_type::vertex];
    }
    pso_id id_pair{};
    id_pair.gpass_pso_id = create_pso_if_needed(stream_ptr, aligned_stream_size, false);

    stream.ps = D3D12_SHADER_BYTECODE{};
    stream.vs = depth_prepass_vertex_shader;
    stream.depth_stencil1 = d3dx::depth_state.reversed;
    id_pair.depth_pso_id = create_pso_if_needed(stream_ptr, aligned_stream_size, true);

    return id_pair;
}
// NOTE: expects data to contain
// struct {
//     u32 width, height, array_size (or depth), flags, mip_levels, format,
//     struct {
//         u32 row_pitch, slice_pitch,
//         u8 image[mip_level][slice_pitch * depth_per_mip],
//     } images[]
// } texture
d3d12_texture
create_resource_from_texture_data(const u8 *const data)
{
    assert(data);
    utl::blob_stream_reader blob{ data };
    const u32 width{ blob.read<u32>() };
    const u32 height{ blob.read<u32>() };
    u32 depth{ 1 };
    u32 array_size{ blob.read<u32>() };
    const u32 flags{ blob.read<u32>() };
    const u32 mip_levels{ blob.read<u32>() };
    const DXGI_FORMAT format{ (DXGI_FORMAT)blob.read<u32>() };
    const bool is_3d{ (flags & primal::content::texture_flags::is_volume_map) != 0 };

    assert(mip_levels <= d3d12_texture::max_mips);

    u32 depth_per_mip_level[d3d12_texture::max_mips]{};
    for (u32 i{ 0 }; i < d3d12_texture::max_mips; ++i)
    {
        depth_per_mip_level[i] = 1;
    }

    if (is_3d)
    {
        depth = array_size;
        array_size = 1;
        u32 depth_per_mip{ depth };

        for (u32 i{ 0 }; i < mip_levels; ++i)
        {
            depth_per_mip_level[i] = depth_per_mip;
            depth_per_mip = std::max(depth_per_mip >> 1, (u32)1);
        }
    }

    utl::vector<D3D12_SUBRESOURCE_DATA> subresources{};

    for (u32 i{ 0 }; i < array_size; ++i)
    {
        for (u32 j{ 0 }; j < mip_levels; ++j)
        {
            const u32 row_pitch{ blob.read<u32>() };
            const u32 slice_pitch{ blob.read<u32>() };

            subresources.emplace_back(D3D12_SUBRESOURCE_DATA
                                      {
                                          blob.position(),
                                          row_pitch,
                                          slice_pitch
                                      });

            // skip the rest of slices.
            blob.skip(slice_pitch * depth_per_mip_level[j]);
        }
    }

    D3D12_RESOURCE_DESC desc{};
    // TODO: handle 1D textures.
    desc.Dimension = is_3d ? D3D12_RESOURCE_DIMENSION_TEXTURE3D : D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    desc.Alignment = 0;
    desc.Width = width;
    desc.Height = height;
    desc.DepthOrArraySize = is_3d ? (u16)depth : (u16)array_size;
    desc.MipLevels = (u16)mip_levels;
    desc.Format = format;
    desc.SampleDesc = { 1,0 };
    desc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
    desc.Flags = D3D12_RESOURCE_FLAG_NONE;

    assert(!(flags & primal::content::texture_flags::is_cube_map && (array_size % 6)));
    const u32 subresource_count{ array_size * mip_levels };
    assert(subresource_count);

    const u32 footprints_data_size{ (sizeof(D3D12_PLACED_SUBRESOURCE_FOOTPRINT) + sizeof(u32) + sizeof(u64)) * subresource_count };
    std::unique_ptr<u8[]> footprints_data{ std::make_unique<u8[]>(footprints_data_size) };

    D3D12_PLACED_SUBRESOURCE_FOOTPRINT *const layouts{ (D3D12_PLACED_SUBRESOURCE_FOOTPRINT *const)footprints_data.get() };
    u32 *const num_rows{ (u32 *const)&layouts[subresource_count] };
    u64 *const row_sizes{ (u64 *const)&num_rows[subresource_count] };
    u64 required_size{ 0 };
    id3d12_device* device{ core::device() };

    device->GetCopyableFootprints(&desc, 0, subresource_count, 0, layouts, num_rows, row_sizes, &required_size);

    assert(required_size);
    upload::d3d12_upload_context context{ (u32)required_size };
    u8 *const cpu_address{ (u8 *const)context.cpu_address() };

    for (u32 subresource_idx{ 0 }; subresource_idx < subresource_count; ++subresource_idx)
    {
        const D3D12_PLACED_SUBRESOURCE_FOOTPRINT& layout{ layouts[subresource_idx] };
        const u32 subresource_height{ num_rows[subresource_idx] };
        const u32 subresource_depth{ layout.Footprint.Depth };
        const D3D12_SUBRESOURCE_DATA& subresource{ subresources[subresource_idx] };

        const D3D12_MEMCPY_DEST copy_dst
        {
            cpu_address + layout.Offset,
            layout.Footprint.RowPitch,
            layout.Footprint.RowPitch * subresource_height
        };

        for (u32 depth_idx{ 0 }; depth_idx < subresource_depth; ++depth_idx)
        {
            u8 *const src_slice{ (u8 *const)subresource.pData + subresource.SlicePitch * depth_idx };
            u8 *const dst_slice{ (u8 *const)copy_dst.pData + copy_dst.SlicePitch * depth_idx };

            for (u32 row_idx{ 0 }; row_idx < subresource_height; ++row_idx)
            {
                memcpy(dst_slice + copy_dst.RowPitch * row_idx, src_slice + subresource.RowPitch * row_idx, row_sizes[subresource_idx]);
            }
        }
    }

    ID3D12Resource* resource{ nullptr };
    DXCall(device->CreateCommittedResource(&d3dx::heap_properties.default_heap, D3D12_HEAP_FLAG_NONE, &desc,
                                           D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&resource)));

    ID3D12Resource* upload_buffer{ context.upload_buffer() };
    for (u32 i{ 0 }; i < subresource_count; ++i)
    {
        D3D12_TEXTURE_COPY_LOCATION src{};
        src.pResource = upload_buffer;
        src.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
        src.PlacedFootprint = layouts[i];

        D3D12_TEXTURE_COPY_LOCATION dst{};
        dst.pResource = resource;
        dst.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        dst.SubresourceIndex = i;

        context.command_list()->CopyTextureRegion(&dst, 0, 0, 0, &src, nullptr);
    }

    context.end_upload();

    assert(resource);

    D3D12_SHADER_RESOURCE_VIEW_DESC srv_desc{};
    d3d12_texture_init_info info{};
    info.resource = resource;

    if (flags & primal::content::texture_flags::is_cube_map)
    {
        assert(array_size % 6 == 0);
        srv_desc.Format = format;
        srv_desc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;

        if (array_size > 6)
        {
            srv_desc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURECUBEARRAY;
            srv_desc.TextureCubeArray.MostDetailedMip = 0;
            srv_desc.TextureCubeArray.MipLevels = mip_levels;
            srv_desc.TextureCubeArray.NumCubes = array_size / 6;
        }
        else
        {
            srv_desc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURECUBE;
            srv_desc.TextureCube.MostDetailedMip = 0;
            srv_desc.TextureCube.MipLevels = mip_levels;
            srv_desc.TextureCube.ResourceMinLODClamp = 0.0f;
        }

        info.srv_desc = &srv_desc;
    }

    return d3d12_texture{ info };
}

} // anonymous namespace

bool
initialize()
{
    return true;
}

void
shutdown()
{
    // NOTE: we only release data that were created as a side-effect to adding resources,
    //       which the user of this module has no control over. The rest of data should be released
    //       by the user, by calling "remove" functions, prior to shutting down the renderer.
    //       That way we make sure the book-keeping of content is correct.

    for (auto& item : root_signatures)
    {
        core::release(item);
    }

    for (auto& item : cmd_signatures)
    {
        core::release(item);
    }

    mtl_rs_map.clear();
    root_signatures.clear();
    cmd_signatures.clear();

    for (auto& item : pipeline_states)
    {
        core::release(item);
    }

    pso_map.clear();
    pipeline_states.clear();
}

namespace submesh {

// NOTE: Expects 'data' to contain:
// 
// u32 size_of_mesh_data
// struct{
//     u8 positions[sizeof(f32) * 3 * vertex_count],     // sizeof(positions) must be a multiple of 4 bytes. Pad if needed.
//     u8 elements[elements_size * vertex_count],        // sizeof(elements) must be a multiple of 4 bytes. Pad if needed.
//     u8 indices[index_size * index_count],             // sizeof(indices) must be a multiple of 4 bytes. Pad if needed.
// } submesh_data[total_submesh_count]
// 
// struct{
//     u32 elements_size, u32 vertex_count,
//     u32 index_count, u32 elements_type, u32 primitive_topology
// } submesh_info[total_submesh_count]
//
// Remarks:
// - Position and element buffers should be padded to be a multiple of 4 bytes in length.
//   This 16 bytes is defined as D3D12_STANDARD_MAXIMUM_ELEMENT_ALIGNMENT_BYTE_MULTIPLE.
void
add(const u8 *const data, id::id_type *const ids, u32 count)
{
    assert(data && ids && count);

    utl::blob_stream_reader blob{ data };
    const u32 mesh_data_size{ blob.read<u32>() };
    ID3D12Resource* resource{ d3dx::create_buffer(blob.position(), mesh_data_size) };
    D3D12_GPU_VIRTUAL_ADDRESS address{ resource->GetGPUVirtualAddress() };
    blob.skip(mesh_data_size); // skip the mesh data to read submesh info.

    std::lock_guard lock{ mesh_mutex };

    const u32 resource_id{ mesh_buffers.add(resource) };

    constexpr u32 alignment{ D3D12_STANDARD_MAXIMUM_ELEMENT_ALIGNMENT_BYTE_MULTIPLE };

    for (u32 i{ 0 }; i < count; ++i)
    {
        const u32 element_size{ blob.read<u32>() };
        const u32 vertex_count{ blob.read<u32>() };
        const u32 index_count{ blob.read<u32>() };
        const u32 elements_type{ blob.read<u32>() };
        const u32 primitive_topology{ blob.read<u32>() };
        const u32 index_size{ (vertex_count < (1 << 16)) ? sizeof(u16) : sizeof(u32) };

        // NOTE: element size may be 0, for position-only vertex formats.
        const u32 position_buffer_size{ sizeof(math::v3) * vertex_count };
        const u32 element_buffer_size{ element_size * vertex_count };
        const u32 index_buffer_size{ index_size * index_count };

        const u32 aligned_position_buffer_size{ (u32)math::align_size_up<alignment>(position_buffer_size) };
        const u32 aligned_element_buffer_size{ (u32)math::align_size_up<alignment>(element_buffer_size) };
        const u32 aligned_index_buffer_size{ (u32)math::align_size_up<alignment>(index_buffer_size) };

        submesh_view view{};
        view.position_buffer_view.BufferLocation = address;
        view.position_buffer_view.SizeInBytes = position_buffer_size;
        view.position_buffer_view.StrideInBytes = sizeof(math::v3);

        if (element_size)
        {
            view.element_buffer_view.BufferLocation = address + aligned_position_buffer_size;
            view.element_buffer_view.SizeInBytes = element_buffer_size;
            view.element_buffer_view.StrideInBytes = element_size;
        }

        view.index_buffer_view.BufferLocation = address + aligned_position_buffer_size + aligned_element_buffer_size;
        view.index_buffer_view.SizeInBytes = index_buffer_size;
        view.index_buffer_view.Format = (index_size == sizeof(u16)) ? DXGI_FORMAT_R16_UINT : DXGI_FORMAT_R32_UINT;

        view.primitive_topology = get_d3d_primitive_topology((primitive_topology::type)primitive_topology);
        view.elements_type = elements_type;
        view.resource_index = resource_id;

        ids[i] = submesh_views.add(view);

        address += aligned_position_buffer_size + aligned_element_buffer_size + aligned_index_buffer_size;
    }
}

void
remove(const id::id_type *const ids, u32 count)
{
    assert(ids && count);
    std::lock_guard lock{ mesh_mutex };
    const u32 resource_id{ submesh_views[ids[0]].resource_index };
    assert(resource_id != u32_invalid_id);

    for (u32 i{ 0 }; i < count; ++i)
    {
        submesh_views.remove(ids[i]);
    }

    core::deferred_release(mesh_buffers[resource_id]);
    mesh_buffers.remove(resource_id);
}

void
get_views(const id::id_type *const gpu_ids, u32 id_count, const views_cache& cache)
{
    assert(gpu_ids && id_count);
    assert(cache.position_buffers && cache.element_buffers && cache.index_buffer_views &&
           cache.primitive_topologies && cache.elements_types);

    std::lock_guard lock{ mesh_mutex };
    for (u32 i{ 0 }; i < id_count; ++i)
    {
        const submesh_view& view{ submesh_views[gpu_ids[i]] };
        cache.position_buffers[i] = view.position_buffer_view.BufferLocation;
        cache.element_buffers[i] = view.element_buffer_view.BufferLocation;
        cache.index_buffer_views[i] = view.index_buffer_view;
        cache.primitive_topologies[i] = view.primitive_topology;
        cache.elements_types[i] = view.elements_type;
    }
}
} // namespace submesh

namespace texture {
// NOTE: expects data to contain
// struct {
//     u32 width, height, array_size (or depth), flags, mip_levels, format,
//     struct {
//         u32 row_pitch, slice_pitch,
//         u8 image[mip_level][slice_pitch * depth_per_mip],
//     } images[]
// } texture
id::id_type
add(const u8* const data)
{
    assert(data);
    d3d12_texture texture{ create_resource_from_texture_data(data) };

    std::lock_guard lock{ texture_mutex };
    const id::id_type id{ textures.add(std::move(texture)) };
    descriptor_indices.add(textures[id].srv().index);
    return id;
}

void
remove(id::id_type id)
{
    std::lock_guard lock{ texture_mutex };
    textures.remove(id);
    descriptor_indices.remove(id);
}

void
get_descriptor_indices(const id::id_type *const texture_ids, u32 id_count, u32 *const indices)
{
    assert(texture_ids && id_count && indices);
    std::lock_guard lock{ texture_mutex };
    for (u32 i{ 0 }; i < id_count; ++i)
    {
        assert(id::is_valid(texture_ids[i]));
        indices[i] = descriptor_indices[texture_ids[i]];
    }
}
} // namespace texture

namespace material {

// Output format:
//
// struct {
// material_type::type  type,
// shader_flags::flags  flags,
// id::id_type          root_signature_id,
// u32                  texture_count,
// id::id_type          shader_ids[shader_count],
// id::id_type          texture_ids[texture_count],
// u32*                 descriptor_indices[texture_count]
// } d3d12_material
id::id_type
add(material_init_info info)
{
    std::unique_ptr<u8[]> buffer;
    std::lock_guard lock{ material_mutex };
    d3d12_material_stream stream{ buffer, info };
    assert(buffer);
    return materials.add(std::move(buffer));
}

void
remove(id::id_type id)
{
    std::lock_guard lock{ material_mutex };
    materials.remove(id);
}

void
get_materials(const id::id_type *const material_ids, u32 material_count, const materials_cache& cache, u32& descriptor_index_count)
{
    assert(material_ids && material_count);
    assert(cache.root_signatures && cache.material_types);
    std::lock_guard lock{ material_mutex };

    u32 total_index_count{ 0 };

    for (u32 i{ 0 }; i < material_count; ++i)
    {
        const d3d12_material_stream stream{ materials[material_ids[i]].get() };
        cache.root_signatures[i] = root_signatures[stream.root_signature_id()];
        cache.cmd_signatures[i] = cmd_signatures[stream.root_signature_id()];
        cache.material_types[i] = stream.material_type();
        cache.descriptor_indices[i] = stream.descriptor_indices();
        cache.texture_count[i] = stream.texture_count();
        cache.material_surfaces[i] = stream.surface();
        total_index_count += stream.texture_count();
    }

    descriptor_index_count = total_index_count;
}

} // namespace material

namespace render_item {

// Creates a buffer that's basically an array of id::id_types.
// buffer[0] = geometry_content_id
// buffer[1 .. n] = d3d12_render_item_ids (n is the number of low-level render item ids which must also equal the number of submeshes/material ids).
// buffer[n + 1] = id::invalid_id (this marks the end of submesh_gpu_id array).
//
id::id_type
add(id::id_type entity_id, id::id_type geometry_content_id,
    u32 material_count, const id::id_type *const material_ids)
{
    assert(id::is_valid(entity_id) && id::is_valid(geometry_content_id));
    assert(material_count && material_ids);
    id::id_type *const gpu_ids{ (id::id_type *const)alloca(material_count * sizeof(id::id_type)) };
    primal::content::get_submesh_gpu_ids(geometry_content_id, material_count, gpu_ids);

    submesh::views_cache views_cache
    {
        (D3D12_GPU_VIRTUAL_ADDRESS *const)alloca(material_count * sizeof(D3D12_GPU_VIRTUAL_ADDRESS)),
            (D3D12_GPU_VIRTUAL_ADDRESS *const)alloca(material_count * sizeof(D3D12_GPU_VIRTUAL_ADDRESS)),
            (D3D12_INDEX_BUFFER_VIEW *const)alloca(material_count * sizeof(D3D12_INDEX_BUFFER_VIEW)),
            (D3D_PRIMITIVE_TOPOLOGY *const)alloca(material_count * sizeof(D3D_PRIMITIVE_TOPOLOGY)),
            (u32 *const)alloca(material_count * sizeof(u32))
    };

    submesh::get_views(gpu_ids, material_count, views_cache);

    // NOTE: the list of ids starts with geomtery id and ends with an invalid id to mark the end of the list.
    std::unique_ptr<id::id_type[]> items{ std::make_unique<id::id_type[]>(sizeof(id::id_type) * (1 + (u64)material_count + 1)) };

    items[0] = geometry_content_id;
    id::id_type *const item_ids{ &items[1] };

    d3d12_render_item *const d3d12_items{ (d3d12_render_item *const)alloca(material_count * sizeof(d3d12_render_item)) };

    for (u32 i{ 0 }; i < material_count; ++i)
    {
        d3d12_render_item& item{ d3d12_items[i] };
        item.entity_id = entity_id;
        item.submesh_gpu_id = gpu_ids[i];
        item.material_id = material_ids[i];
        pso_id id_pair{ create_pso(item.material_id, views_cache.primitive_topologies[i], views_cache.elements_types[i]) };
        item.pso_id = id_pair.gpass_pso_id;
        item.depth_pso_id = id_pair.depth_pso_id;

        assert(id::is_valid(item.submesh_gpu_id) && id::is_valid(item.material_id));
    }

    std::lock_guard lock{ render_item_mutex };

    for (u32 i{ 0 }; i < material_count; ++i)
    {
        item_ids[i] = render_items.add(d3d12_items[i]);
    }

    // mark the end of ids list.
    item_ids[material_count] = id::invalid_id;

    return render_item_ids.add(std::move(items));
}

void
remove(id::id_type id)
{
    std::lock_guard lock{ render_item_mutex };
    const id::id_type *const item_ids{ &render_item_ids[id][1] };

    // NOTE: the last element in the list of ids is always an invalid id.
    for (u32 i{ 0 }; item_ids[i] != id::invalid_id; ++i)
    {
        render_items.remove(item_ids[i]);
    }

    render_item_ids.remove(id);
}

void
get_d3d12_render_item_ids(const frame_info& info, utl::vector<id::id_type>& d3d12_render_item_ids)
{
    assert(info.render_item_ids && info.thresholds && info.render_item_count);
    assert(d3d12_render_item_ids.empty());

    frame_cache.lod_offsets.clear();
    frame_cache.geometry_ids.clear();
    const u32 count{ info.render_item_count };

    std::lock_guard lock{ render_item_mutex };

    for (u32 i{ 0 }; i < count; ++i)
    {
        const id::id_type *const buffer{ render_item_ids[info.render_item_ids[i]].get() };
        frame_cache.geometry_ids.emplace_back(buffer[0]);
    }

    primal::content::get_lod_offsets(frame_cache.geometry_ids.data(), info.thresholds, count, frame_cache.lod_offsets);
    assert(frame_cache.lod_offsets.size() == count);

    u32 d3d12_render_item_count{ 0 };
    for (u32 i{ 0 }; i < count; ++i)
    {
        d3d12_render_item_count += frame_cache.lod_offsets[i].count;
    }

    assert(d3d12_render_item_count);
    d3d12_render_item_ids.resize(d3d12_render_item_count);

    u32 item_index{ 0 };
    for (u32 i{ 0 }; i < count; ++i)
    {
        const id::id_type *const item_ids{ &render_item_ids[info.render_item_ids[i]][1] };
        const primal::content::lod_offset& lod_offset{ frame_cache.lod_offsets[i] };
        memcpy(&d3d12_render_item_ids[item_index], &item_ids[lod_offset.offset], sizeof(id::id_type) * lod_offset.count);
        item_index += lod_offset.count;
        assert(item_index <= d3d12_render_item_count);
    }

    assert(item_index == d3d12_render_item_count);
}

void
get_items(const id::id_type *const d3d12_render_item_ids, u32 id_count, const items_cache& cache)
{
    assert(d3d12_render_item_ids && id_count);
    assert(cache.entity_ids && cache.submesh_gpu_ids && cache.material_ids &&
           cache.gpass_psos && cache.depth_psos);

    std::lock_guard lock1{ render_item_mutex };
    std::lock_guard lock2{ pso_mutex };

    for (u32 i{ 0 }; i < id_count; ++i)
    {
        const d3d12_render_item& item{ render_items[d3d12_render_item_ids[i]] };
        cache.entity_ids[i] = item.entity_id;
        cache.submesh_gpu_ids[i] = item.submesh_gpu_id;
        cache.material_ids[i] = item.material_id;
        cache.gpass_psos[i] = pipeline_states[item.pso_id];
        cache.depth_psos[i] = pipeline_states[item.depth_pso_id];
    }
}

} // namespace render_item
}