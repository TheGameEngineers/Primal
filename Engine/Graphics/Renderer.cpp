// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Renderer.h"
#include "GraphicsPlatformInterface.h"
#include "Direct3D12\D3D12Interface.h"
#include "Utilities/MutationCache.h"

namespace primal::graphics {
namespace {

// Defines where the compiled engine shaders file is located for each one of the supported APIs.
constexpr const char* engine_shader_paths[]{
    ".\\Shaders\\D3D12\\Shaders.bin",
    // ".\\shaders\\vulkan\\shaders.bin", etc.
};

struct light_mutation_cache
{
    enum flags : u8 {
        color_flag = 0x01,
        attenuation_flag = 0x02,
        cone_angles_flag = 0x04,
        intensity_flag = 0x08,
        range_flag = 0x10,
        is_enabled_flag = 0x20,

        all_flag =
        color_flag | attenuation_flag | cone_angles_flag |
        intensity_flag | range_flag | is_enabled_flag
    };

    u64         light_set_key{};
    math::v3    color;
    math::v3    attenuation;
    math::v2    cone_angles;
    f32         intensity;
    f32         range;
    light_id    id{ id::invalid_id };
    bool        is_enabled;
    u8          flags;
};

struct camera_mutation_cache
{
    enum flags : u8
    {
        up_flag = 0x01,
        fov_flag = 0x02,
        aspect_flag = 0x04,
        size_flag = 0x08,
        range_flag = 0x10,

        all_flag =
        up_flag | fov_flag | aspect_flag | size_flag | range_flag
    };

    math::v3    up;
    f32         field_of_view;
    math::v2    range;
    f32         aspect_ratio;
    f32         orthographic_size;
    camera_id   id;
    u8          flags;
};

platform_interface                                      gfx{};
std::unordered_map<id::id_type, light>                  light_id_map{};
std::unordered_map<id::id_type, camera>                 camera_id_map{};

utl::mutation_cache<light_mutation_cache, light_id>     light_cache{};
utl::mutation_cache<camera_mutation_cache, camera_id>   camera_cache{};

bool
set_platform_interface(graphics_platform platform, platform_interface& pi)
{
    switch (platform)
    {
    case graphics_platform::direct3d12:
        d3d12::get_platform_interface(pi);
        break;
    default:
        return false;
    }

    assert(pi.platform == platform);
    return true;
}

} // anonymous namespace

bool
initialize(graphics_platform platform)
{
    return set_platform_interface(platform, gfx) && gfx.initialize();
}

void
shutdown()
{
    if (gfx.platform != (graphics_platform)-1) gfx.shutdown();
}

void
set_option(renderer_option::option option, const void *const parameter, u32 parameter_size)
{
    gfx.set_option(option, parameter, parameter_size);
}

void
get_option(renderer_option::option option, void *const parameter, u32 parameter_size)
{
    gfx.get_option(option, parameter, parameter_size);
}

const char*
get_engine_shaders_path()
{
    return engine_shader_paths[(u32)gfx.platform];
}

const char*
get_engine_shaders_path(graphics_platform platform)
{
    return engine_shader_paths[(u32)platform];
}

surface
create_surface(platform::window window)
{
    return gfx.surface.create(window);
}

void
remove_surface(surface_id id)
{
    assert(id::is_valid(id));
    gfx.surface.remove(id);
}

void
surface::resize(u32 width, u32 height) const
{
    assert(is_valid());
    gfx.surface.resize(_id, width, height);
}

u32
surface::width() const
{
    assert(is_valid());
    return gfx.surface.width(_id);
}

u32
surface::height() const
{
    assert(is_valid());
    return gfx.surface.height(_id);
}

void
surface::render(frame_info info) const
{
    assert(is_valid());
    gfx.surface.render(_id, info);
}

void
create_light_set(u64 light_set_key)
{
    gfx.light.create_light_set(light_set_key);
}
void
remove_light_set(u64 light_set_key)
{
    gfx.light.remove_light_set(light_set_key);
}

light
create_light(light_init_info info)
{
    light l{ gfx.light.create(info) };
    if (id::is_valid(l.get_id()) && id::is_valid(info.entity_id))
    {
        light_id_map[info.entity_id] = l;
    }

    return l;
}

void
remove_light(light_id id, u64 light_set_key)
{
    light l{ id, light_set_key };
    if (l.is_valid() && id::is_valid(l.entity_id()))
    {
        light_id_map.erase(l.entity_id());
    }

    gfx.light.remove(id, light_set_key);
}

light
get_light(id::id_type entity_id)
{
    return light_id_map.contains(entity_id) ? light_id_map[entity_id] : light{};
}

void
update_lights()
{
    light_mutation_cache* cache{ nullptr };
    u32 count{};
    light_cache.get_cache_array(&cache, &count);

    if (!count) return;

    assert(cache);

    for (u32 i{ 0 }; i < count; ++i)
    {
        const light_mutation_cache c{ cache[i] };
        assert(light(c.id, c.light_set_key).is_valid());

        if (c.flags & light_mutation_cache::color_flag)
        {
            gfx.light.set_parameter(c.id, c.light_set_key, light_parameter::color, &c.color, sizeof(c.color));
        }
        if (c.flags & light_mutation_cache::attenuation_flag)
        {
            gfx.light.set_parameter(c.id, c.light_set_key, light_parameter::attenuation, &c.attenuation, sizeof(c.attenuation));
        }
        if (c.flags & light_mutation_cache::cone_angles_flag)
        {
            gfx.light.set_parameter(c.id, c.light_set_key, light_parameter::umbra, &c.cone_angles.x, sizeof(c.cone_angles.x));
            gfx.light.set_parameter(c.id, c.light_set_key, light_parameter::penumbra, &c.cone_angles.y, sizeof(c.cone_angles.y));
        }
        if (c.flags & light_mutation_cache::intensity_flag)
        {
            gfx.light.set_parameter(c.id, c.light_set_key, light_parameter::intensity, &c.intensity, sizeof(c.intensity));
        }
        if (c.flags & light_mutation_cache::range_flag)
        {
            gfx.light.set_parameter(c.id, c.light_set_key, light_parameter::range, &c.range, sizeof(c.range));
        }
        if (c.flags & light_mutation_cache::is_enabled_flag)
        {
            gfx.light.set_parameter(c.id, c.light_set_key, light_parameter::is_enabled, &c.is_enabled, sizeof(c.is_enabled));
        }
    }

    light_cache.clear();
}

void
light::is_enabled(bool is_enabled) const
{
    assert(is_valid());
    light_mutation_cache& cache{ *light_cache.get_cache_ptr(_id) };
    cache.light_set_key = _light_set_key;
    cache.flags |= light_mutation_cache::is_enabled_flag;
    cache.is_enabled = is_enabled;
}

void
light::intensity(f32 intensity) const
{
    assert(is_valid());
    light_mutation_cache& cache{ *light_cache.get_cache_ptr(_id) };
    cache.light_set_key = _light_set_key;
    cache.flags |= light_mutation_cache::intensity_flag;
    cache.intensity = intensity;
}

void
light::color(math::v3 color) const
{
    assert(is_valid());
    light_mutation_cache& cache{ *light_cache.get_cache_ptr(_id) };
    cache.light_set_key = _light_set_key;
    cache.flags |= light_mutation_cache::color_flag;
    cache.color = color;
}

void
light::attenuation(math::v3 attenuation) const
{
    assert(is_valid());
    light_mutation_cache& cache{ *light_cache.get_cache_ptr(_id) };
    cache.light_set_key = _light_set_key;
    cache.flags |= light_mutation_cache::attenuation_flag;
    cache.attenuation = attenuation;
}

void
light::range(f32 range) const
{
    assert(is_valid());
    light_mutation_cache& cache{ *light_cache.get_cache_ptr(_id) };
    cache.light_set_key = _light_set_key;
    cache.flags |= light_mutation_cache::range_flag;
    cache.range = range;
}

void
light::cone_angles(f32 umbra, f32 penumbra) const
{
    assert(is_valid());
    light_mutation_cache& cache{ *light_cache.get_cache_ptr(_id) };
    cache.light_set_key = _light_set_key;
    cache.flags |= light_mutation_cache::cone_angles_flag;
    cache.cone_angles = { umbra, penumbra };
}

bool
light::is_enabled() const
{
    assert(is_valid());
    bool is_enabled;
    gfx.light.get_parameter(_id, _light_set_key, light_parameter::is_enabled, &is_enabled, sizeof(is_enabled));
    return is_enabled;
}

f32
light::intensity() const
{
    assert(is_valid());
    f32 intensity;
    gfx.light.get_parameter(_id, _light_set_key, light_parameter::intensity, &intensity, sizeof(intensity));
    return intensity;
}

math::v3
light::color() const
{
    assert(is_valid());
    math::v3 color;
    gfx.light.get_parameter(_id, _light_set_key, light_parameter::color, &color, sizeof(color));
    return color;
}

math::v3
light::attenuation() const
{
    assert(is_valid());
    math::v3 attenuation;
    gfx.light.get_parameter(_id, _light_set_key, light_parameter::attenuation, &attenuation, sizeof(attenuation));
    return attenuation;
}

f32
light::range() const
{
    assert(is_valid());
    f32 range;
    gfx.light.get_parameter(_id, _light_set_key, light_parameter::range, &range, sizeof(range));
    return range;
}

f32
light::umbra() const
{
    assert(is_valid());
    f32 umbra;
    gfx.light.get_parameter(_id, _light_set_key, light_parameter::umbra, &umbra, sizeof(umbra));
    return umbra;
}

f32
light::penumbra() const
{
    assert(is_valid());
    f32 penumbra;
    gfx.light.get_parameter(_id, _light_set_key, light_parameter::penumbra, &penumbra, sizeof(penumbra));
    return penumbra;
}

light::type
light::light_type() const
{
    assert(is_valid());
    type type;
    gfx.light.get_parameter(_id, _light_set_key, light_parameter::type, &type, sizeof(type));
    return type;
}

id::id_type
light::entity_id() const
{
    assert(is_valid());
    id::id_type id;
    gfx.light.get_parameter(_id, _light_set_key, light_parameter::entity_id, &id, sizeof(id));
    return id;
}

camera
create_camera(camera_init_info info)
{
    camera c{ gfx.camera.create(info) };
    if (id::is_valid(c.get_id()))
    {
        camera_id_map[info.entity_id] = c;
    }

    return c;
}

void
remove_camera(camera_id id)
{
    camera c{ id };
    if (id::is_valid(c.get_id()))
    {
        camera_id_map.erase(c.entity_id());
    }

    gfx.camera.remove(id);
}

camera
get_camera(id::id_type entity_id)
{
    return camera_id_map.contains(entity_id) ? camera_id_map[entity_id] : camera{};
}

void
update_cameras()
{
    camera_mutation_cache* cache{ nullptr };
    u32 count{};
    camera_cache.get_cache_array(&cache, &count);

    if (!count) return;

    assert(cache);

    for (u32 i{ 0 }; i < count; ++i)
    {
        const camera_mutation_cache c{ cache[i] };
        assert(camera(c.id).is_valid());

        if (c.flags & camera_mutation_cache::up_flag)
        {
            gfx.camera.set_parameter(c.id, camera_parameter::up_vector, &c.up, sizeof(c.up));
        }
        if (c.flags & camera_mutation_cache::fov_flag)
        {
            gfx.camera.set_parameter(c.id, camera_parameter::field_of_view, &c.field_of_view, sizeof(c.field_of_view));
        }
        if (c.flags & camera_mutation_cache::aspect_flag)
        {
            gfx.camera.set_parameter(c.id, camera_parameter::aspect_ratio, &c.aspect_ratio, sizeof(c.aspect_ratio));
        }
        if (c.flags & camera_mutation_cache::size_flag)
        {
            gfx.camera.set_parameter(c.id, camera_parameter::orthographic_size, &c.orthographic_size, sizeof(c.orthographic_size));
        }
        if (c.flags & camera_mutation_cache::range_flag)
        {
            gfx.camera.set_parameter(c.id, camera_parameter::near_z, &c.range.x, sizeof(c.range.x));
            gfx.camera.set_parameter(c.id, camera_parameter::far_z, &c.range.y, sizeof(c.range.y));
        }
    }

    camera_cache.clear();
}

void
camera::up(math::v3 up) const
{
    assert(is_valid());
    camera_mutation_cache& cache{ *camera_cache.get_cache_ptr(_id) };
    cache.flags |= camera_mutation_cache::up_flag;
    cache.up = up;
}

void
camera::field_of_view(f32 fov) const
{
    assert(is_valid());
    camera_mutation_cache& cache{ *camera_cache.get_cache_ptr(_id) };
    cache.flags |= camera_mutation_cache::fov_flag;
    cache.field_of_view = fov;
}

void
camera::aspect_ratio(f32 aspect_ratio) const
{
    assert(is_valid());
    camera_mutation_cache& cache{ *camera_cache.get_cache_ptr(_id) };
    cache.flags |= camera_mutation_cache::aspect_flag;
    cache.aspect_ratio = aspect_ratio;
}

void
camera::orthographic_size(f32 size) const
{
    assert(is_valid());
    camera_mutation_cache& cache{ *camera_cache.get_cache_ptr(_id) };
    cache.flags |= camera_mutation_cache::size_flag;
    cache.orthographic_size = size;
}

void
camera::range(f32 near_z, f32 far_z) const
{
    assert(is_valid());
    camera_mutation_cache& cache{ *camera_cache.get_cache_ptr(_id) };
    cache.flags |= camera_mutation_cache::range_flag;
    cache.range = { near_z, far_z };
}

math::m4x4
camera::view() const
{
    assert(is_valid());
    math::m4x4 matrix;
    gfx.camera.get_parameter(_id, camera_parameter::view, &matrix, sizeof(matrix));
    return matrix;
}

math::m4x4
camera::projection() const
{
    assert(is_valid());
    math::m4x4 matrix;
    gfx.camera.get_parameter(_id, camera_parameter::projection, &matrix, sizeof(matrix));
    return matrix;
}

math::m4x4
camera::inverse_projection() const
{
    assert(is_valid());
    math::m4x4 matrix;
    gfx.camera.get_parameter(_id, camera_parameter::inverse_projection, &matrix, sizeof(matrix));
    return matrix;
}

math::m4x4
camera::view_projection() const
{
    assert(is_valid());
    math::m4x4 matrix;
    gfx.camera.get_parameter(_id, camera_parameter::view_projection, &matrix, sizeof(matrix));
    return matrix;
}

math::m4x4
camera::inverse_view_projection() const
{
    assert(is_valid());
    math::m4x4 matrix;
    gfx.camera.get_parameter(_id, camera_parameter::inverse_view_projection, &matrix, sizeof(matrix));
    return matrix;
}

math::v3
camera::up() const
{
    assert(is_valid());
    math::v3 up_vector;
    gfx.camera.get_parameter(_id, camera_parameter::up_vector, &up_vector, sizeof(up_vector));
    return up_vector;
}

f32
camera::near_z() const
{
    assert(is_valid());
    f32 near_z;
    gfx.camera.get_parameter(_id, camera_parameter::near_z, &near_z, sizeof(near_z));
    return near_z;
}

f32
camera::far_z() const
{
    assert(is_valid());
    f32 far_z;
    gfx.camera.get_parameter(_id, camera_parameter::far_z, &far_z, sizeof(far_z));
    return far_z;
}

f32
camera::field_of_view() const
{
    assert(is_valid());
    f32 fov;
    gfx.camera.get_parameter(_id, camera_parameter::field_of_view, &fov, sizeof(fov));
    return fov;
}

f32
camera::aspect_ratio() const
{
    assert(is_valid());
    f32 aspect_ratio;
    gfx.camera.get_parameter(_id, camera_parameter::aspect_ratio, &aspect_ratio, sizeof(aspect_ratio));
    return aspect_ratio;
}

f32
camera::orthographic_size() const
{
    assert(is_valid());
    f32 size;
    gfx.camera.get_parameter(_id, camera_parameter::orthographic_size, &size, sizeof(size));
    return size;
}

camera::type
camera::projection_type() const
{
    assert(is_valid());
    camera::type type;
    gfx.camera.get_parameter(_id, camera_parameter::type, &type, sizeof(type));
    return type;
}

id::id_type
camera::entity_id() const
{
    assert(is_valid());
    id::id_type entity_id;
    gfx.camera.get_parameter(_id, camera_parameter::entity_id, &entity_id, sizeof(entity_id));
    return entity_id;
}

void
add_mesh(const u8 *const data, id::id_type *const ids, u32 count)
{
    gfx.resources.add_mesh(data, ids, count);
}

void
remove_mesh(const id::id_type *const ids, u32 count)
{
    gfx.resources.remove_mesh(ids, count);
}

id::id_type
add_texture(const u8 *const data)
{
    return gfx.resources.add_texture(data);
}

void
remove_texture(id::id_type id)
{
    gfx.resources.remove_texture(id);
}

id::id_type
add_material(material_init_info info)
{
    return gfx.resources.add_material(info);
}

void
remove_material(id::id_type id)
{
    gfx.resources.remove_material(id);
}

id::id_type
add_render_item(id::id_type entity_id, id::id_type geometry_content_id,
    u32 material_count, const id::id_type *const material_ids)
{
    return gfx.resources.add_render_item(entity_id, geometry_content_id, material_count, material_ids);
}

void
remove_render_item(id::id_type id)
{
    gfx.resources.remove_render_item(id);
}
}