// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "ContentLoader.h"
#include "Components/Entity.h"
#include "Components/Geometry.h"
#include "Components/Transform.h"
#include "Components/Script.h"
#include "Graphics/Renderer.h"
#include "Utilities/IOStream.h"
#include "ContentToEngine.h"

#if !defined(SHIPPING) && defined(_WIN64)

#include <fstream>
#include <filesystem>
#include <cstring>
#include <string>

namespace primal::content {
namespace {

enum entity_type : u32
{
    game_entity,
    light,
    camera,

    count
};

struct loaded_asset
{
    asset_type::type    type;
    id::id_type         id;
};

constexpr const u32 guid_str_size{ 36 };
constexpr const u32 max_mtl_inputs{ 1024 }; // Arbitrary limit. Shaders are unlikely to have 1k+ inputs.

utl::vector<game_entity::entity>            entities;
utl::vector<graphics::light>                lights;
utl::vector<graphics::camera>               cameras;
utl::vector<u64>                            light_set_keys;
utl::vector<loaded_asset>                   loaded_assets;
utl::vector<std::string>                    asset_guids;
utl::vector<graphics::material_init_info>   materials;
std::unordered_map<u64, id::id_type>        applied_materials;

bool
read_transform(utl::blob_stream_reader& blob, transform::init_info& info)
{
    using namespace DirectX;
    f32 position[3]{ blob.read<f32>(), blob.read<f32>() , blob.read<f32>() };
    f32 rotation[3]{ blob.read<f32>(), blob.read<f32>() , blob.read<f32>() };
    f32 scale[3]{ blob.read<f32>(), blob.read<f32>() , blob.read<f32>() };

    math::v3 rot{ &rotation[0] };
    XMVECTOR quat{ XMQuaternionRotationRollPitchYawFromVector(XMLoadFloat3(&rot)) };
    math::v4 rot_quat{};
    XMStoreFloat4(&rot_quat, quat);

    memcpy(info.position, &position[0], sizeof(info.position));
    memcpy(info.rotation, &rot_quat.x, sizeof(info.rotation));
    memcpy(info.scale, &scale[0], sizeof(info.scale));

    return true;
}

bool
read_script(utl::blob_stream_reader& blob, script::init_info& info)
{
    assert(!info.script_creator);
    const u32 name_length{ blob.read<u32>() };
    // if a script name is longer than 255 characters then something is probably
    // very wrong, either with the binary writer or the game programmer.
    assert(name_length < 256);
    if (!name_length || name_length > 255) return false;
    char script_name[256]{};
    blob.read((u8*)&script_name[0], name_length);
    // make the name a zero-terminated c-string.
    script_name[name_length] = 0;
    info.script_creator = script::detail::get_script_creator(script::detail::string_hash()(script_name));
    return info.script_creator != nullptr;
}

id::id_type
create_applied_material_if_necessary(const graphics::material_init_info& mtl_info)
{
    // We allocate a block of memory that contains everything in mtl_inf,
    // including the texture ids (not the pointer to those ids)
    // The block is small enough for stack allocations
    assert(mtl_info.texture_count <= max_mtl_inputs);
    const u32 ids_size{ mtl_info.texture_count * sizeof(id::id_type) };
    const u64 aligned_size{ math::align_size_up<sizeof(u64)>(sizeof(mtl_info) + ids_size - sizeof(id::id_type*)) };
    u8* data{ (u8*)alloca(aligned_size) };
    memset(data, 0, aligned_size);

    if (mtl_info.texture_count)
    {
        memcpy(data, mtl_info.texture_ids, ids_size);
    }

    // NOTE: assumes texture_ids is the first member of material_init_info. 
    memcpy(data + ids_size, &mtl_info.surface, sizeof(mtl_info) - sizeof(id::id_type*));

    const u64 key{ math::calc_crc32_u64(data, aligned_size) };
    auto pair = applied_materials.find(key);

    if (pair != applied_materials.end())
    {
        assert(pair->first == key);
        return pair->second;
    }

    const id::id_type id{ create_resource(&mtl_info, asset_type::material) };
    assert(id::is_valid(id));
    applied_materials[key] = id;
    return id;
}

bool
read_geometry(utl::blob_stream_reader& blob, geometry::init_info& info, utl::vector<id::id_type>& mtl_ids)
{
    const u32 geometry_idx{ blob.read<u32>() };
    assert(geometry_idx < loaded_assets.size() &&
        id::is_valid(loaded_assets[geometry_idx].id) && loaded_assets[geometry_idx].type == asset_type::mesh);
    if (geometry_idx >= loaded_assets.size() ||
        !id::is_valid(loaded_assets[geometry_idx].id) ||
        loaded_assets[geometry_idx].type != asset_type::mesh) return false;

    info.geometry_content_id = loaded_assets[geometry_idx].id;
    info.material_count = blob.read<u32>();
    assert(info.material_count);
    if (!info.material_count) return false;

    for (u32 i{ 0 }; i < info.material_count; ++i)
    {
        const u32 mtl_idx{ blob.read<u32>() };
        assert(mtl_idx < loaded_assets.size() && loaded_assets[mtl_idx].type == asset_type::material);
        if (mtl_idx >= loaded_assets.size() || loaded_assets[mtl_idx].type != asset_type::material) return false;

        graphics::material_init_info& mtl_info{ materials[loaded_assets[mtl_idx].id] };

        const u32 count{ blob.read<u32>() };
        //NOTE: count maybe 0. Textures are optional.
        assert(count <= max_mtl_inputs);
        if (count > max_mtl_inputs) return false;
        mtl_info.texture_count = count;

        // NOTE: we set this explicitly, to make sure the correct crc32 is generated (see below).
        mtl_info.texture_ids = nullptr;
        id::id_type texture_ids[max_mtl_inputs]{};

        if (count)
        {
            const u32 *const input_indices{ (const u32*)blob.position() };
            blob.skip(count * sizeof(u32)); // skip indices

            for (u32 input_idx{ 0 }; input_idx < count; ++input_idx)
            {
                const u32 idx{ input_indices[input_idx] };
                assert(idx < loaded_assets.size() &&
                    id::is_valid(loaded_assets[idx].id) && loaded_assets[idx].type == asset_type::texture);
                if ((idx >= loaded_assets.size() ||
                    !id::is_valid(loaded_assets[idx].id) || loaded_assets[idx].type != asset_type::texture))
                    return false;

                texture_ids[input_idx] = loaded_assets[idx].id;
            }

            mtl_info.texture_ids = &texture_ids[0];
        }

        graphics::material_surface& s{ mtl_info.surface };
        blob.read(&s.base_color[0], sizeof(s.base_color));
        blob.read(&s.emissive[0], sizeof(s.emissive));
        s.metallic = blob.read<u8>();
        s.roughness = blob.read<u8>();
        s.input_mask = blob.read<u8>();
        s.emissive_intensity = blob.read<u16>();

        // Make sure that we only create a material if it's different (like we do in the editor).        
        id::id_type mtl_id{ create_applied_material_if_necessary(mtl_info) };
        assert(id::is_valid(mtl_id));
        if (!id::is_valid(mtl_id)) return false;
        mtl_ids.emplace_back(mtl_id);
        assert(mtl_ids.size() == info.material_count);
        info.material_ids = mtl_ids.data();
    }

    return true;
}

bool
read_file(std::filesystem::path path, std::unique_ptr<u8[]>& data, u64& size)
{
    if (!std::filesystem::exists(path)) return false;

    size = std::filesystem::file_size(path);
    assert(size);
    if (!size) return false;
    data = std::make_unique<u8[]>(size);
    std::ifstream file{ path, std::ios::in | std::ios::binary };
    if (!file || !file.read((char*)data.get(), size))
    {
        file.close();
        return false;
    }

    file.close();
    return true;
}

void
read_guids_array(utl::blob_stream_reader& blob)
{
    const u32 count{ blob.read<u32>() };
    char buffer[guid_str_size]{};

    asset_guids.clear();
    asset_guids.resize(count);
    loaded_assets.resize(count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        blob.read((u8*)&buffer[0], guid_str_size);
        asset_guids[i] = { buffer, guid_str_size };
        loaded_assets[i].id = id::invalid_id;
    }
}

id::id_type
load_shader(std::filesystem::path path, u32 count)
{
    std::unique_ptr<u8[]> data{};
    u64 size{ 0 };
    if (!read_file(path, data, size) || !size) return id::invalid_id;

    utl::blob_stream_reader blob{ data.get() };
    const graphics::shader_type::type shader_type{ blob.read<u32>() };
    assert(shader_type < graphics::shader_type::count);
    if (shader_type >= graphics::shader_type::count) return id::invalid_id;

    const u32 *const keys{ (const u32*)blob.position() };
    blob.skip(count * sizeof(u32)); // skip keys

    const u8** shader_pointers{ (const u8**)alloca(count * sizeof(u8*)) };

    for (u32 i{ 0 }; i < count; ++i)
    {
        // NOTE: byteCodeLength is a 64-bit value!
        const u32 block_size{ sizeof(u64) + compiled_shader::hash_length + *(u32*)blob.position() };
        shader_pointers[i] = blob.position();
        blob.skip(block_size);
    }

    assert(blob.position() == (data.get() + size));

    return add_shader_group(shader_pointers, count, keys);
}

id::id_type
load_material(utl::blob_stream_reader& blob)
{
    graphics::material_init_info info{};
    info.type = (graphics::material_type::type)blob.read<u32>();
    assert(info.type < graphics::material_type::count);

    const u32 shader_flags{ blob.read<u32>() };
    const u32 shader_file_count{ blob.read<u32>() };
    assert(shader_file_count < graphics::shader_type::count);
    if (shader_file_count >= graphics::shader_type::count) return id::invalid_id;

    u32 group_counts[graphics::shader_type::count]{};

    for (u32 i{ 0 }; i < shader_file_count; ++i)
    {
        group_counts[i] = blob.read<u32>();
    }

    u32 file_idx{ 0 };

    for (u32 shader_idx{ 0 }; shader_idx < graphics::shader_type::count; ++shader_idx)
    {
        if (shader_flags & (1 << shader_idx))
        {
            assert(file_idx < shader_file_count);
            if (file_idx >= shader_file_count) return id::invalid_id;

            const u32 file_name_size{ blob.read<u32>() };
            assert(file_name_size < 256);
            if (file_name_size > 255) return id::invalid_id;
            std::string shader_file{ (const char*)blob.position(), file_name_size };
            std::filesystem::path path{ "/Content/" + shader_file };
            blob.skip(file_name_size);

            info.shader_ids[shader_idx] = load_shader(path, group_counts[file_idx]);
            ++file_idx;
            assert(id::is_valid(info.shader_ids[shader_idx]));
            if (!id::is_valid(info.shader_ids[shader_idx])) return id::invalid_id;
        }
    }

    const id::id_type id{ (id::id_type)materials.size() };
    materials.emplace_back(info);

    return id;
}

bool
load_scene_assets(utl::blob_stream_reader& blob)
{
    const u32 count{ blob.read<u32>() };

    for (u32 i{ 0 }; i < count; ++i)
    {
        const u32 index{ blob.read<u32>() };
        assert(index < asset_guids.size() && index < loaded_assets.size());

        if (id::is_valid(loaded_assets[index].id)) continue;

        std::filesystem::path path{ "/Content/" + asset_guids[index] + ".asset" };
        u64 size{ 0 };
        std::unique_ptr<u8[]> data{};
        if (!read_file(path, data, size) || !size) return false;

        utl::blob_stream_reader reader{ &data[0] };
        const asset_type::type asset_type{ reader.read<u32>() };
        assert(asset_type < asset_type::count);

        loaded_assets[index].type = asset_type;
        loaded_assets[index].id = asset_type != asset_type::material ?
            create_resource(reader.position(), asset_type) :
            load_material(reader);

        assert(id::is_valid(loaded_assets[index].id));
        if (!id::is_valid(loaded_assets[index].id)) return false;
    }

    return true;
}

graphics::light_init_info
read_light_info(utl::blob_stream_reader& blob, game_entity::entity_id entity_id)
{
    assert(id::is_valid(entity_id));
    const graphics::light::type light_type{ blob.read<u32>() };
    assert(light_type < graphics::light::count);

    const u32 light_set_key_size{ blob.read<u32>() };
    assert(light_set_key_size > 0 && light_set_key_size < 256);
    const u64 key{ std::hash<std::string>()({(const char*)blob.position(), light_set_key_size}) };
    blob.skip(light_set_key_size);

    if (std::find(light_set_keys.begin(), light_set_keys.end(), key) == light_set_keys.end())
    {
        light_set_keys.emplace_back(key);
        graphics::create_light_set(key);
    }

    graphics::light_init_info info{};
    info.light_set_key = key;
    info.entity_id = entity_id;
    info.type = light_type;
    info.intensity = blob.read<f32>();
    info.color = { blob.read<f32>(), blob.read<f32>(), blob.read<f32>() };
    info.is_enabled = blob.read<u32>();

    graphics::ambient_params ambient_params{};
    const u32 diffuse_idx{ blob.read<u32>() };
    const u32 specular_idx{ blob.read<u32>() };
    const u32 brdf_lut_idx{ blob.read<u32>() };
    if (diffuse_idx != u32_invalid_id && specular_idx != u32_invalid_id && brdf_lut_idx != u32_invalid_id)
    {
        assert(light_type == graphics::light::ambient);
        assert(diffuse_idx < loaded_assets.size() && id::is_valid(loaded_assets[diffuse_idx].id));
        assert(specular_idx < loaded_assets.size() && id::is_valid(loaded_assets[specular_idx].id));
        assert(brdf_lut_idx < loaded_assets.size() && id::is_valid(loaded_assets[brdf_lut_idx].id));

        ambient_params.diffuse_texture_id = loaded_assets[diffuse_idx].id;
        ambient_params.specular_texture_id = loaded_assets[specular_idx].id;
        ambient_params.brdf_lut_texture_id = loaded_assets[brdf_lut_idx].id;

        assert(id::is_valid(ambient_params.diffuse_texture_id) &&
            id::is_valid(ambient_params.specular_texture_id) &&
            id::is_valid(ambient_params.brdf_lut_texture_id));
    }

    const f32 range{ blob.read<f32>() };
    const math::v3 attenuation{ blob.read<f32>(), blob.read<f32>(), blob.read<f32>() };
    const f32 umbra{ blob.read<f32>() };
    const f32 penumbra{ blob.read<f32>() };

    graphics::point_light_params point_params
    {
        .attenuation = attenuation,
        .range = range,
    };

    graphics::spot_light_params spot_params
    {
        .attenuation = attenuation,
        .range = range,
        .umbra = umbra,
        .penumbra = penumbra
    };

    if (light_set_key_size == graphics::light::point)
    {
        info.point_params = point_params;
    }
    else if (light_set_key_size == graphics::light::spot)
    {
        info.spot_params = spot_params;
    }
    else if (light_set_key_size == graphics::light::ambient)
    {
        info.ambient_params = ambient_params;
    }

    return info;
}

graphics::camera_init_info
read_camera_info(utl::blob_stream_reader& blob, game_entity::entity_id entity_id)
{
    assert(id::is_valid(entity_id));
    const graphics::camera::type camera_type{ blob.read<u32>() };
    assert(camera_type < graphics::camera::count);

    const f32 fov_or_size{ blob.read<f32>() };
    assert(fov_or_size > 0.f);

    const f32 near_z{ blob.read<f32>() };
    const f32 far_z{ blob.read<f32>() };
    assert(near_z > 0.f && near_z < far_z);

    return
    {
        .entity_id = entity_id,
        .type = camera_type,
        .up = {0.f, 1.f, 0.f},
        .field_of_view = fov_or_size,
        .aspect_ratio = 16.f / 10.f,
        .near_z = near_z,
        .far_z = far_z
    };
}

} // anonymous namespace

bool
load_game()
{
    auto fail = [] { unload_game(); return false; };
    unload_game();
    std::unique_ptr<u8[]> game_data{};
    u64 size{ 0 };
    if (!read_file("game.bin", game_data, size)) return fail();

    utl::blob_stream_reader blob{ game_data.get() };

    read_guids_array(blob);

    const u32 scene_count{ blob.read<u32>() };

    for (u32 scene_index{ 0 }; scene_index < scene_count; ++scene_index)
    {
        const u32 scene_size{ blob.read<u32>() };
        assert(scene_size > sizeof(u32));
        const u32 is_active{ blob.read<u32>() };

        if (!is_active)
        {
            blob.skip(scene_size - sizeof(u32));
            continue;
        }

        if (!load_scene_assets(blob)) return fail();

        const u32 entity_count{ blob.read<u32>() };

        for (u32 entity_index{ 0 }; entity_index < entity_count; ++entity_index)
        {
            const entity_type entity_type{ blob.read<u32>() };
            const u32 component_count{ blob.read<u32>() };
            assert(component_count > 0 && component_count <= component_type::count);
            if (!component_count || component_count > component_type::count) return fail();

            game_entity::entity_info entity_info{};
            transform::init_info transform_info{};
            script::init_info script_info{};
            geometry::init_info geometry_info{};
            utl::vector<id::id_type> mtl_ids{};

            for (u32 component_index{ 0 }; component_index < component_count; ++component_index)
            {
                const component_type::type component_type{ blob.read<u32>() };
                assert(component_type < component_type::count);

                switch (component_type)
                {
                case component_type::transform:
                {
                    if (entity_info.transform || !read_transform(blob, transform_info)) return fail();
                    entity_info.transform = &transform_info;
                }
                break;
                case component_type::script:
                {
                    if (entity_info.script || !read_script(blob, script_info)) return fail();
                    entity_info.script = &script_info;
                }
                break;
                case component_type::geometry:
                {
                    if (entity_info.geometry || !read_geometry(blob, geometry_info, mtl_ids)) return fail();
                    entity_info.geometry = &geometry_info;
                }
                break;
                default:
                    return fail();
                };
            }

            game_entity::entity entity{ game_entity::create(entity_info) };
            entities.emplace_back(entity);
            assert(entity.is_valid());
            if (!entity.is_valid()) return fail();

            graphics::light_init_info light_info{};
            graphics::camera_init_info camera_info{};

            if (entity_type == entity_type::light)
            {
                light_info = read_light_info(blob, entity.get_id());
                lights.emplace_back(graphics::create_light(light_info));
                if (!lights.back().is_valid()) return fail();
            }
            else if (entity_type == entity_type::camera)
            {
                camera_info = read_camera_info(blob, entity.get_id());
                cameras.emplace_back(graphics::create_camera(camera_info));
                if (!cameras.back().is_valid()) return fail();
            }
            else
            {
                return fail();
            }
        }
    }

    return true;
}

void
unload_game()
{
    for (auto camera : cameras) graphics::remove_camera(camera.get_id());
    cameras.clear();

    for (auto light : lights) graphics::remove_light(light.get_id(), light.light_set_key());
    lights.clear();

    for (auto key : light_set_keys) graphics::remove_light_set(key);
    light_set_keys.clear();

    for (auto entity : entities)
    {
        if (entity.is_valid() && game_entity::is_alive(entity.get_id())) game_entity::remove(entity.get_id());
    }
    entities.clear();

    for (auto& asset : loaded_assets)
    {
        if (id::is_valid(asset.id) && asset.type != asset_type::material)
        {
            destroy_resource(asset.id, asset.type);
            // TODO: unload materials
        }
    }
    loaded_assets.clear();
}

bool
load_engine_shaders(std::unique_ptr<u8[]>& shaders, u64& size)
{
    auto path = graphics::get_engine_shaders_path();
    return read_file(path, shaders, size);
}

}
#endif // !defined(SHIPPING)