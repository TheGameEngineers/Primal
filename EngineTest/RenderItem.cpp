// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include <filesystem>
#include "CommonHeaders.h"
#include "Content/ContentToEngine.h"
#include "Graphics/Renderer.h"
#include "ShaderCompilation.h"
#include "Components/Entity.h"
#include "Components/Geometry.h"
#include "../ContentTools/Geometry.h"
#include "Test.h"

#if TEST_RENDERER

using namespace primal;

game_entity::entity create_one_game_entity(math::v3 position, math::v3 rotation, geometry::init_info* geometry_info, const char* script_name);
void remove_game_entity(game_entity::entity_id id);
bool read_file(std::filesystem::path, std::unique_ptr<u8[]>&, u64&);

namespace {

id::id_type fan_model_id{ id::invalid_id };
id::id_type int_model_id{ id::invalid_id };
id::id_type lab_model_id{ id::invalid_id };
id::id_type fembot_model_id{ id::invalid_id };
id::id_type sphere_model_id{ id::invalid_id };

game_entity::entity_id fan_entity_id{ id::invalid_id };
game_entity::entity_id int_entity_id{ id::invalid_id };
game_entity::entity_id lab_entity_id{ id::invalid_id };
game_entity::entity_id fembot_entity_id{ id::invalid_id };
game_entity::entity_id sphere_entity_ids[12];

struct texture_usage {
    enum usage : u32 {
        ambient_occlusion = 0,
        base_color,
        emissive,
        metal_rough,
        normal,

        count
    };
};

id::id_type texture_ids[texture_usage::count];

id::id_type vs_id{ id::invalid_id };
id::id_type ps_id{ id::invalid_id };
id::id_type textured_ps_id{ id::invalid_id };
id::id_type default_mtl_id{ id::invalid_id };
id::id_type fembot_mtl_id{ id::invalid_id };

id::id_type pbr_mtl_ids[12];

[[nodiscard]] id::id_type
load_asset(const char* path, content::asset_type::type type)
{
    std::unique_ptr<u8[]> buffer;
    u64 size{ 0 };
    read_file(path, buffer, size);

    const id::id_type asset_id{ content::create_resource(buffer.get(), type) };
    assert(id::is_valid(asset_id));
    return asset_id;
}


[[nodiscard]] id::id_type
load_model(const char* path)
{
    // load test model
    return load_asset(path, content::asset_type::mesh);
}

[[nodiscard]] id::id_type
load_texture(const char* path)
{
    // load test texture
    return load_asset(path, content::asset_type::texture);
}

void
load_shaders()
{
    // Let's say our material uses a vertex shader and a pixel shader.
    shader_file_info info{};
    info.file_name = "TestShader.hlsl";
    info.function = "TestShaderVS";
    info.type = shader_type::vertex;

    const char* shader_path{ "..\\..\\enginetest\\" };

    std::wstring defines[]{ L"ELEMENTS_TYPE=1", L"ELEMENTS_TYPE=3" };
    utl::vector<u32> keys;
    keys.emplace_back(tools::elements::elements_type::static_normal);
    keys.emplace_back(tools::elements::elements_type::static_normal_texture);

    utl::vector<std::wstring> extra_args{};
    utl::vector<std::unique_ptr<u8[]>> vertex_shaders;
    utl::vector<const u8*> vertex_shader_pointers;
    for (u32 i{ 0 }; i < _countof(defines); ++i)
    {
        extra_args.clear();
        extra_args.emplace_back(L"-D");
        extra_args.emplace_back(defines[i]);
        vertex_shaders.emplace_back(std::move(compile_shader(info, shader_path, extra_args)));
        assert(vertex_shaders.back().get());
        vertex_shader_pointers.emplace_back(vertex_shaders.back().get());
    }

    extra_args.clear();
    info.function = "TestShaderPS";
    info.type = shader_type::pixel;
    utl::vector<std::unique_ptr<u8[]>> pixel_shaders;

    pixel_shaders.emplace_back(compile_shader(info, shader_path, extra_args));
    assert(pixel_shaders.back().get());

    defines[0] = L"TEXTURED_MTL=1";
    extra_args.emplace_back(L"-D");
    extra_args.emplace_back(defines[0]);

    pixel_shaders.emplace_back(compile_shader(info, shader_path, extra_args));
    assert(pixel_shaders.back().get());

    vs_id = content::add_shader_group(vertex_shader_pointers.data(), (u32)vertex_shader_pointers.size(), keys.data());

    const u8* pixel_shader_pointers[]{ pixel_shaders[0].get() };
    ps_id = content::add_shader_group(pixel_shader_pointers, 1, &u32_invalid_id);

    pixel_shader_pointers[0] = pixel_shaders[1].get();
    textured_ps_id = content::add_shader_group(pixel_shader_pointers, 1, &u32_invalid_id);
}

void
create_material()
{
    assert(id::is_valid(vs_id) && id::is_valid(ps_id) && id::is_valid(textured_ps_id));
    graphics::material_init_info info{};
    info.shader_ids[shader_type::vertex] = vs_id;
    info.shader_ids[shader_type::pixel] = ps_id;
    info.type = graphics::material_type::opaque;
    default_mtl_id = content::create_resource(&info, content::asset_type::material);

    memset(pbr_mtl_ids, 0xff, sizeof(pbr_mtl_ids));
    math::v2 metal_rough[_countof(pbr_mtl_ids)]{
        {0.f, 0.0f}, {0.f, 0.2f}, {0.f, 0.4f}, {0.f, 0.6f}, {0.f, 0.8f}, {0.f, 1.f},
        {1.f, 0.0f}, {1.f, 0.2f}, {1.f, 0.4f}, {1.f, 0.6f}, {1.f, 0.8f}, {1.f, 1.f},
    };
    graphics::material_surface& s{ info.surface };
    s.base_color = { 0.5f, 0.5f, 0.5f, 1.f };

    for (u32 i{ 0 }; i < _countof(pbr_mtl_ids); ++i)
    {
        s.metallic = metal_rough[i].x;
        s.roughness = metal_rough[i].y;
        pbr_mtl_ids[i] = content::create_resource(&info, content::asset_type::material);
    }

    info.shader_ids[shader_type::pixel] = textured_ps_id;
    info.texture_count = texture_usage::count;
    info.texture_ids = &texture_ids[0];
    fembot_mtl_id = content::create_resource(&info, content::asset_type::material);
}

void
remove_model(id::id_type model_id)
{
    if (id::is_valid(model_id))
    {
        content::destroy_resource(model_id, content::asset_type::mesh);
    }
}
} // anonymous namespace

void
create_render_items()
{
    // NOTE: you can get these models if you're a patreon or ko-fi supporter of Primal Engine.
    //       https://www.patreon.com/collection/270663
    //       https://ko-fi.com/gameengineseries/shop
    //       Use the editor to import the scene and put the 3 models in this location.
    //       You can replace them with any model that's available to you.
    assert(std::filesystem::exists("..\\..\\x64\\lab_model.model"));
    assert(std::filesystem::exists("..\\..\\x64\\fan_model.model"));
    assert(std::filesystem::exists("..\\..\\x64\\int_model.model"));
    assert(std::filesystem::exists("..\\..\\x64\\fembot_model.model"));

    memset(&texture_ids[0], 0xff, sizeof(id::id_type) * _countof(texture_ids));

    std::thread threads[]{
        std::thread{ [] { texture_ids[texture_usage::ambient_occlusion] = load_texture("..\\..\\x64\\ambient_occlusion.texture"); }},
        std::thread{ [] { texture_ids[texture_usage::base_color] = load_texture("..\\..\\x64\\base_color.texture"); }},
        std::thread{ [] { texture_ids[texture_usage::emissive] = load_texture("..\\..\\x64\\emissive.texture"); }},
        std::thread{ [] { texture_ids[texture_usage::metal_rough] = load_texture("..\\..\\x64\\metal_rough.texture"); }},
        std::thread{ [] { texture_ids[texture_usage::normal] = load_texture("..\\..\\x64\\normal.texture"); }},

        std::thread{ [] { lab_model_id = load_model("..\\..\\x64\\lab_model.model"); } },
        std::thread{ [] { fan_model_id = load_model("..\\..\\x64\\fan_model.model"); } },
        std::thread{ [] { int_model_id = load_model("..\\..\\x64\\int_model.model"); } },
        std::thread{ [] { fembot_model_id = load_model("..\\..\\x64\\fembot_model.model"); } },
        std::thread{ [] { sphere_model_id = load_model("..\\..\\x64\\sphere_model.model"); } },
        std::thread{ [] { load_shaders(); } },
    };

    for (auto& t : threads)
    {
        t.join();
    }

    // NOTE: we need shaders to be ready before creating materials
    create_material();
    id::id_type materials[]{ default_mtl_id };
    id::id_type fembot_materials[]{ fembot_mtl_id, fembot_mtl_id };

    geometry::init_info geometry_info{};
    geometry_info.material_count = _countof(materials);
    geometry_info.material_ids = &materials[0];

    geometry_info.geometry_content_id = lab_model_id;
    lab_entity_id = create_one_game_entity({}, {}, &geometry_info, nullptr).get_id();

    geometry_info.geometry_content_id = fan_model_id;
    fan_entity_id = create_one_game_entity({ -10.47f, 5.93f, -6.7f }, {}, &geometry_info, "fan_script").get_id();
    
    geometry_info.geometry_content_id = int_model_id;
    int_entity_id = create_one_game_entity({ 0.f, 1.3f, -6.6f }, {}, &geometry_info, "wibbly_wobbly_script").get_id();
    
    geometry_info.geometry_content_id = fembot_model_id;
    geometry_info.material_count = _countof(fembot_materials);
    geometry_info.material_ids = &fembot_materials[0];
    fembot_entity_id = create_one_game_entity({ -6.f, 0.f, 10.f }, { 0.f, math::pi, 0.f }, &geometry_info, nullptr/*"rotator_script"*/).get_id();

    geometry_info.geometry_content_id = sphere_model_id;
    geometry_info.material_count = 1;
    for (u32 i{ 0 }; i < _countof(sphere_entity_ids); ++i)
    {
        id::id_type id{ pbr_mtl_ids[i] };
        id::id_type sphere_mtls[]{ id };
        geometry_info.material_ids = &sphere_mtls[0];
        const f32 x{ -6.f + i % 6 };
        const f32 y{ (i < 6) ? 7.f : 5.5f };
        const f32 z = x;
        sphere_entity_ids[i] = create_one_game_entity({ x, y, z }, {}, &geometry_info, nullptr).get_id();
    }
}

void
destroy_render_items()
{
    remove_game_entity(lab_entity_id);
    remove_game_entity(fan_entity_id);
    remove_game_entity(int_entity_id);
    remove_game_entity(fembot_entity_id);

    for (u32 i{ 0 }; i < _countof(sphere_entity_ids); ++i)
    {
        remove_game_entity(sphere_entity_ids[i]);
    }

    remove_model(lab_model_id);
    remove_model(fan_model_id);
    remove_model(int_model_id);
    remove_model(fembot_model_id);
    remove_model(sphere_model_id);

    // remove material
    if (id::is_valid(default_mtl_id))
    {
        content::destroy_resource(default_mtl_id, content::asset_type::material);
    }

    if (id::is_valid(fembot_mtl_id))
    {
        content::destroy_resource(fembot_mtl_id, content::asset_type::material);
    }

    for (id::id_type id : pbr_mtl_ids)
    {
        if (id::is_valid(id))
        {
            content::destroy_resource(id, content::asset_type::material);
        }
    }

    // remove textures
    for (id::id_type id : texture_ids)
    {
        if (id::is_valid(id))
        {
            content::destroy_resource(id, content::asset_type::texture);
        }
    }

    // remove shaders and textures
    if (id::is_valid(vs_id))
    {
        content::remove_shader_group(vs_id);
    }

    if (id::is_valid(ps_id))
    {
        content::remove_shader_group(ps_id);
    }

    if (id::is_valid(textured_ps_id))
    {
        content::remove_shader_group(textured_ps_id);
    }
}
#endif // TEST_RENDERER