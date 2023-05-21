// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "D3D12GPass.h"
#include "D3D12Core.h"
#include "D3D12Shaders.h"
#include "D3D12Content.h"
#include "D3D12Light.h"
#include "D3D12Camera.h"
#include "D3D12LightCulling.h"
#include "Shaders/SharedTypes.h"
#include "Components/Entity.h"
#include "Components/Transform.h"

namespace primal::graphics::d3d12::gpass {
namespace {

constexpr math::u32v2           initial_dimensions{ 100, 100 };

d3d12_render_texture            gpass_main_buffer{};
d3d12_depth_buffer              gpass_depth_buffer{};
math::u32v2                     dimensions{ initial_dimensions };

#if _DEBUG
constexpr f32                   clear_value[4]{ 0.5f, 0.5f, 0.5f, 1.f };
#else
constexpr f32                   clear_value[4]{ };
#endif

// NOTE (to myself): don't forget to #undef CONSTEXPR when you copy/paste this block of code!
#if USE_STL_VECTOR
#define CONSTEXPR
#else
#define CONSTEXPR constexpr
#endif

struct gpass_cache
{
    utl::vector<id::id_type>    d3d12_render_item_ids;

    // NOTE: when adding new arrays, make sure to update resize() and struct_size.
    id::id_type*                entity_ids{ nullptr };
    id::id_type*                submesh_gpu_ids{ nullptr };
    id::id_type*                material_ids{ nullptr };
    ID3D12PipelineState**       gpass_pipeline_states{ nullptr };
    ID3D12PipelineState**       depth_pipeline_states{ nullptr };
    ID3D12RootSignature**       root_signatures{ nullptr };
    material_type::type*        material_types{ nullptr };
    D3D12_GPU_VIRTUAL_ADDRESS*  position_buffers{ nullptr };
    D3D12_GPU_VIRTUAL_ADDRESS*  element_buffers{ nullptr };
    D3D12_INDEX_BUFFER_VIEW*    index_buffer_views{ nullptr };
    D3D_PRIMITIVE_TOPOLOGY*     primitive_topologies{ nullptr };
    u32*                        elements_types{ nullptr };
    D3D12_GPU_VIRTUAL_ADDRESS*  per_object_data{ nullptr };

    constexpr content::render_item::items_cache items_cache() const
    {
        return{
            entity_ids,
            submesh_gpu_ids,
            material_ids,
            gpass_pipeline_states,
            depth_pipeline_states
        };
    }

    constexpr content::submesh::views_cache views_cache() const
    {
        return{
            position_buffers,
            element_buffers,
            index_buffer_views,
            primitive_topologies,
            elements_types
        };
    }

    constexpr content::material::materials_cache materials_cache() const
    {
        return{
            root_signatures,
            material_types
        };
    }

    CONSTEXPR u32 size() const
    {
        return (u32)d3d12_render_item_ids.size();
    }

    CONSTEXPR void clear()
    {
        d3d12_render_item_ids.clear();
    }

    CONSTEXPR void resize()
    {
        const u64 items_count{ d3d12_render_item_ids.size() };
        const u64 new_buffer_size{ items_count * struct_size };
        const u64 old_buffer_size{ _buffer.size() };
        if (new_buffer_size > old_buffer_size)
        {
            _buffer.resize(new_buffer_size);
        }

        if (new_buffer_size != old_buffer_size)
        {
            entity_ids = (id::id_type*)_buffer.data();
            submesh_gpu_ids = (id::id_type*)(&entity_ids[items_count]);
            material_ids = (id::id_type*)(&submesh_gpu_ids[items_count]);
            gpass_pipeline_states = (ID3D12PipelineState**)(&material_ids[items_count]);
            depth_pipeline_states = (ID3D12PipelineState**)(&gpass_pipeline_states[items_count]);
            root_signatures = (ID3D12RootSignature**)(&depth_pipeline_states[items_count]);
            material_types = (material_type::type*)(&root_signatures[items_count]);
            position_buffers = (D3D12_GPU_VIRTUAL_ADDRESS*)(&material_types[items_count]);
            element_buffers = (D3D12_GPU_VIRTUAL_ADDRESS*)(&position_buffers[items_count]);
            index_buffer_views = (D3D12_INDEX_BUFFER_VIEW*)(&element_buffers[items_count]);
            primitive_topologies = (D3D_PRIMITIVE_TOPOLOGY*)(&index_buffer_views[items_count]);
            elements_types = (u32*)(&primitive_topologies[items_count]);
            per_object_data = (D3D12_GPU_VIRTUAL_ADDRESS*)(&elements_types[items_count]);
        }
    }

private:
    constexpr static u32 struct_size{
        sizeof(id::id_type) +                   // entity_ids
        sizeof(id::id_type) +                   // submesh_gpu_ids
        sizeof(id::id_type) +                   // material_ids
        sizeof(ID3D12PipelineState *) +         // gpass_pipeline_states
        sizeof(ID3D12PipelineState *) +         // depth_pipeline_states
        sizeof(ID3D12RootSignature*) +          // root_signatures
        sizeof(material_type::type) +           // material_types
        sizeof(D3D12_GPU_VIRTUAL_ADDRESS) +     // position_buffers
        sizeof(D3D12_GPU_VIRTUAL_ADDRESS) +     // element_buffers
        sizeof(D3D12_INDEX_BUFFER_VIEW) +       // index_buffer_views
        sizeof(D3D_PRIMITIVE_TOPOLOGY) +        // primitive_topologies
        sizeof(u32) +                           // elements_types
        sizeof(D3D12_GPU_VIRTUAL_ADDRESS)       // per_object_data
    };

    utl::vector<u8> _buffer;
} frame_cache;

// Good boy!
#undef CONSTEXPR

bool
create_buffers(math::u32v2 size)
{
    assert(size.x && size.y);
    gpass_main_buffer.release();
    gpass_depth_buffer.release();

    D3D12_RESOURCE_DESC desc{};
    desc.Alignment = 0; // NOTE: 0 is the same as 64KB (or 4MB for MSAA)
    desc.DepthOrArraySize = 1;
    desc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    desc.Flags = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
    desc.Format = main_buffer_format;
    desc.Height = size.y;
    desc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
    desc.MipLevels = 0; // make space for all mip levels
    desc.SampleDesc = { 1, 0 };
    desc.Width = size.x;

    // Create the main buffer
    {
        d3d12_texture_init_info info{};
        info.desc = &desc;
        info.initial_state = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
        info.clear_value.Format = desc.Format;
        memcpy(&info.clear_value.Color, &clear_value[0], sizeof(clear_value));
        gpass_main_buffer = d3d12_render_texture{ info };
    }

    desc.Flags = D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;
    desc.Format = depth_buffer_format;
    desc.MipLevels = 1;

    // Create the depth buffer
    {
        d3d12_texture_init_info info{};
        info.desc = &desc;
        info.initial_state = D3D12_RESOURCE_STATE_DEPTH_READ | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
        info.clear_value.Format = desc.Format;
        info.clear_value.DepthStencil.Depth = 0.f;
        info.clear_value.DepthStencil.Stencil = 0;

        gpass_depth_buffer = d3d12_depth_buffer{ info };
    }

    NAME_D3D12_OBJECT(gpass_main_buffer.resource(), L"GPass Main Buffer");
    NAME_D3D12_OBJECT(gpass_depth_buffer.resource(), L"GPass Depth Buffer");

    return gpass_main_buffer.resource() && gpass_depth_buffer.resource();
}

void
fill_per_object_data(const d3d12_frame_info& d3d12_info)
{
    const gpass_cache& cache{ frame_cache };
    const u32 render_items_count{ (u32)cache.size() };
    id::id_type current_entity_id{ id::invalid_id };
    hlsl::PerObjectData* current_data_pointer{ nullptr };

    constant_buffer& cbuffer{ core::cbuffer() };

    using namespace DirectX;
    for (u32 i{ 0 }; i < render_items_count; ++i)
    {
        if (current_entity_id != cache.entity_ids[i])
        {
            current_entity_id = cache.entity_ids[i];
            hlsl::PerObjectData data{};
            transform::get_transform_matrices(game_entity::entity_id{ current_entity_id }, data.World, data.InvWorld);
            XMMATRIX world{ XMLoadFloat4x4(&data.World) };
            XMMATRIX wvp{ XMMatrixMultiply(world, d3d12_info.camera->view_projection()) };
            XMStoreFloat4x4(&data.WorldViewProjection, wvp);

            current_data_pointer = cbuffer.allocate<hlsl::PerObjectData>();
            memcpy(current_data_pointer, &data, sizeof(hlsl::PerObjectData));
        }

        assert(current_data_pointer);
        cache.per_object_data[i] = cbuffer.gpu_address(current_data_pointer);
    }
}

void
set_root_parameters(id3d12_graphics_command_list *const cmd_list, u32 cache_index)
{
    gpass_cache& cache{ frame_cache };
    assert(cache_index < cache.size());

    const material_type::type mtl_type{ cache.material_types[cache_index] };
    switch (mtl_type)
    {
    case material_type::opaque:
    {
        using params = opaque_root_parameter;
        cmd_list->SetGraphicsRootShaderResourceView(params::position_buffer, cache.position_buffers[cache_index]);
        cmd_list->SetGraphicsRootShaderResourceView(params::element_buffer, cache.element_buffers[cache_index]);
        cmd_list->SetGraphicsRootConstantBufferView(params::per_object_data, cache.per_object_data[cache_index]);
    }
    break;
    }
}

void
prepare_render_frame(const d3d12_frame_info& d3d12_info)
{
    assert(d3d12_info.info && d3d12_info.camera);
    assert(d3d12_info.info->render_item_ids && d3d12_info.info->render_item_count);
    gpass_cache& cache{ frame_cache };
    cache.clear();

    using namespace content;
    render_item::get_d3d12_render_item_ids(*d3d12_info.info, cache.d3d12_render_item_ids);
    cache.resize();
    const u32 items_count{ cache.size() };
    const render_item::items_cache items_cache{ cache.items_cache() };
    render_item::get_items(cache.d3d12_render_item_ids.data(), items_count, items_cache);

    const submesh::views_cache views_cache{ cache.views_cache() };
    submesh::get_views(items_cache.submesh_gpu_ids, items_count, views_cache);

    const material::materials_cache materials_cache{ cache.materials_cache() };
    material::get_materials(items_cache.material_ids, items_count, materials_cache);

    fill_per_object_data(d3d12_info);

}

} // anonymous namespace

bool
initialize()
{
    return create_buffers(initial_dimensions);
}

void
shutdown()
{
    gpass_main_buffer.release();
    gpass_depth_buffer.release();
    dimensions = initial_dimensions;
}

const d3d12_render_texture&
main_buffer()
{
    return gpass_main_buffer;
}

const d3d12_depth_buffer&
depth_buffer()
{
    return gpass_depth_buffer;
}

void
set_size(math::u32v2 size)
{
    math::u32v2& d{ dimensions };
    if (size.x > d.x || size.y > d.y)
    {
        d = { std::max(size.x, d.x), std::max(size.y, d.y) };
        create_buffers(d);
    }
}

void depth_prepass(id3d12_graphics_command_list* cmd_list, const d3d12_frame_info& d3d12_info)
{
    prepare_render_frame(d3d12_info);

    const gpass_cache& cache{ frame_cache };
    const u32 items_count{ cache.size() };

    ID3D12RootSignature* current_root_signature{ nullptr };
    ID3D12PipelineState* current_pipeline_state{ nullptr };

    for (u32 i{ 0 }; i < items_count; ++i)
    {
        if (current_root_signature != cache.root_signatures[i])
        {
            current_root_signature = cache.root_signatures[i];
            cmd_list->SetGraphicsRootSignature(current_root_signature);
            cmd_list->SetGraphicsRootConstantBufferView(opaque_root_parameter::global_shader_data, d3d12_info.global_shader_data);
        }

        if (current_pipeline_state != cache.depth_pipeline_states[i])
        {
            current_pipeline_state = cache.depth_pipeline_states[i];
            cmd_list->SetPipelineState(current_pipeline_state);
        }

        set_root_parameters(cmd_list, i);

        const D3D12_INDEX_BUFFER_VIEW& ibv{ cache.index_buffer_views[i] };
        const u32 index_count{ ibv.SizeInBytes >> (ibv.Format == DXGI_FORMAT_R16_UINT ? 1 : 2) };

        cmd_list->IASetIndexBuffer(&ibv);
        cmd_list->IASetPrimitiveTopology(cache.primitive_topologies[i]);
        cmd_list->DrawIndexedInstanced(index_count, 1, 0, 0, 0);
    }
}

void render(id3d12_graphics_command_list* cmd_list, const d3d12_frame_info& d3d12_info)
{
    const gpass_cache& cache{ frame_cache };
    const u32 items_count{ cache.size() };
    const u32 frame_index{ d3d12_info.frame_index };
    const id::id_type light_culling_id{ d3d12_info.light_culling_id };

    ID3D12RootSignature* current_root_signature{ nullptr };
    ID3D12PipelineState* current_pipeline_state{ nullptr };

    for (u32 i{ 0 }; i < items_count; ++i)
    {
        if (current_root_signature != cache.root_signatures[i])
        {
            using idx = opaque_root_parameter;
            current_root_signature = cache.root_signatures[i];
            cmd_list->SetGraphicsRootSignature(current_root_signature);
            cmd_list->SetGraphicsRootConstantBufferView(idx::global_shader_data, d3d12_info.global_shader_data);
            cmd_list->SetGraphicsRootShaderResourceView(idx::directional_lights, light::non_cullable_light_buffer(frame_index));
            cmd_list->SetGraphicsRootShaderResourceView(idx::cullable_lights, light::cullable_light_buffer(frame_index));
            cmd_list->SetGraphicsRootShaderResourceView(idx::light_grid, delight::light_grid_opaque(light_culling_id, frame_index));
            cmd_list->SetGraphicsRootShaderResourceView(idx::light_index_list, delight::light_index_list_opaque (light_culling_id, frame_index));
        }

        if (current_pipeline_state != cache.gpass_pipeline_states[i])
        {
            current_pipeline_state = cache.gpass_pipeline_states[i];
            cmd_list->SetPipelineState(current_pipeline_state);
        }

        set_root_parameters(cmd_list, i);

        const D3D12_INDEX_BUFFER_VIEW& ibv{ cache.index_buffer_views[i] };
        const u32 index_count{ ibv.SizeInBytes >> (ibv.Format == DXGI_FORMAT_R16_UINT ? 1 : 2) };

        cmd_list->IASetIndexBuffer(&ibv);
        cmd_list->IASetPrimitiveTopology(cache.primitive_topologies[i]);
        cmd_list->DrawIndexedInstanced(index_count, 1, 0, 0, 0);
    }
}

void
add_transitions_for_depth_prepass(d3dx::d3d12_resource_barrier& barriers)
{
    barriers.add(gpass_main_buffer.resource(),
                 D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,
                 D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_BARRIER_FLAG_BEGIN_ONLY);
    barriers.add(gpass_depth_buffer.resource(),
                 D3D12_RESOURCE_STATE_DEPTH_READ | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,
                 D3D12_RESOURCE_STATE_DEPTH_WRITE);
}

void
add_transitions_for_gpass(d3dx::d3d12_resource_barrier& barriers)
{
    barriers.add(gpass_main_buffer.resource(),
                 D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,
                 D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_BARRIER_FLAG_END_ONLY);
    barriers.add(gpass_depth_buffer.resource(),
                 D3D12_RESOURCE_STATE_DEPTH_WRITE,
                 D3D12_RESOURCE_STATE_DEPTH_READ | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
}

void
add_transitions_for_post_process(d3dx::d3d12_resource_barrier& barriers)
{
    barriers.add(gpass_main_buffer.resource(),
                 D3D12_RESOURCE_STATE_RENDER_TARGET,
                 D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
}

void
set_render_targets_for_depth_prepass(id3d12_graphics_command_list* cmd_list)
{
    const D3D12_CPU_DESCRIPTOR_HANDLE dsv{ gpass_depth_buffer.dsv() };
    cmd_list->ClearDepthStencilView(dsv, D3D12_CLEAR_FLAG_DEPTH | D3D12_CLEAR_FLAG_STENCIL, 0.f, 0, 0, nullptr);
    cmd_list->OMSetRenderTargets(0, nullptr, 0, &dsv);
}

void
set_render_targets_for_gpass(id3d12_graphics_command_list* cmd_list)
{
    const D3D12_CPU_DESCRIPTOR_HANDLE rtv{ gpass_main_buffer.rtv(0) };
    const D3D12_CPU_DESCRIPTOR_HANDLE dsv{ gpass_depth_buffer.dsv() };

    cmd_list->ClearRenderTargetView(rtv, clear_value, 0, nullptr);
    cmd_list->OMSetRenderTargets(1, &rtv, 0, &dsv);
}

}