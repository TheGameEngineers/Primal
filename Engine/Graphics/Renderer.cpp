// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Renderer.h"
#include "GraphicsPlatformInterface.h"
#include "Direct3D12\D3D12Interface.h"


namespace primal::graphics {
namespace {

// Defines where the compiled engine shaders file is located for each one of the supported APIs.
constexpr const char* engine_shader_paths[]{
    ".\\shaders\\d3d12\\shaders.bin",
    // ".\\shaders\\vulkan\\shaders.bin", etc.
};

platform_interface gfx{};

#ifndef PRIMAL_PLUS
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
#endif
} // anonymous namespace

#ifdef PRIMAL_PLUS
extern bool set_platform_interface(graphics_platform platform, platform_interface& pi);
#endif

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
surface::render() const
{
    assert(is_valid());
    gfx.surface.render(_id);
}

camera
create_camera(camera_init_info info)
{
    return gfx.camera.create(info);
}

void
remove_camera(camera_id id)
{
    gfx.camera.remove(id);
}

void
camera::up(math::v3 up) const
{
    assert(is_valid());
    gfx.camera.set_parameter(_id, camera_parameter::up_vector, &up, sizeof(up));
}

void
camera::field_of_view(f32 fov) const
{
    assert(is_valid());
    gfx.camera.set_parameter(_id, camera_parameter::field_of_view, &fov, sizeof(fov));
}

void
camera::aspect_ratio(f32 aspect_ratio) const
{
    assert(is_valid());
    gfx.camera.set_parameter(_id, camera_parameter::aspect_ratio, &aspect_ratio, sizeof(aspect_ratio));
}

void
camera::view_width(f32 width) const
{
    assert(is_valid());
    gfx.camera.set_parameter(_id, camera_parameter::view_width, &width, sizeof(width));
}

void
camera::view_height(f32 height) const
{
    assert(is_valid());
    gfx.camera.set_parameter(_id, camera_parameter::view_height, &height, sizeof(height));
}

void
camera::range(f32 near_z, f32 far_z) const
{
    assert(is_valid());
    gfx.camera.set_parameter(_id, camera_parameter::near_z, &near_z, sizeof(near_z));
    gfx.camera.set_parameter(_id, camera_parameter::far_z, &far_z, sizeof(far_z));
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
camera::view_width() const
{
    assert(is_valid());
    f32 width;
    gfx.camera.get_parameter(_id, camera_parameter::view_width, &width, sizeof(width));
    return width;
}

f32
camera::view_height() const
{
    assert(is_valid());
    f32 height;
    gfx.camera.get_parameter(_id, camera_parameter::view_height, &height, sizeof(height));
    return height;
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

id::id_type
add_submesh(const u8*& data)
{
    return gfx.resources.add_submesh(data);
}

void remove_submesh(id::id_type id)
{
    gfx.resources.remove_submesh(id);
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
}