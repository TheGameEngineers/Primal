// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Common.h"
#include "CommonHeaders.h"
#include "Components/Script.h"
#include "Graphics/Renderer.h"
#include "Platform/PlatformTypes.h"
#include "Platform/Platform.h"
#include "Content/ContentToEngine.h"
#include "ShaderCompilation.h"
#include "../ContentTools/ToolsCommon.h"
#include "Utilities/IOStream.h"
#include "Components/Entity.h"
#include "Components/Geometry.h"
#include "Components/Transform.h"
#include "Utilities/Threading.h"
#include "../Engine/EditorAPI/EditorAPI.h"
#include "Input/Input.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif

#include <Windows.h>

using namespace primal;

namespace {
HMODULE game_code_dll{ nullptr };
using _get_script_creator = primal::script::detail::script_creator(*)(size_t);
_get_script_creator get_script_creator{ nullptr };
using _get_script_names = LPSAFEARRAY(*)(void);
_get_script_names get_script_names{ nullptr };
utl::ticket_mutex mutex{};

struct viewport_surface : public graphics::render_surface
{
    graphics::camera            camera{};
    utl::vector<id::id_type>    geometry_ids{};
};

utl::vector<viewport_surface> surfaces;
utl::vector<u8> frame_info_buffer;

struct engine_init_error {
    enum error_code :u32 {
        succeeded = 0,
        unknown,
        shader_compilation,
        graphics,
    };
};

struct shader_data
{
    u32 type;
    u32 code_size;
    u32 byte_code_size;
    u32 errors_size;
    u32 assembly_size;
    u32 hash_size;
    u8* code;
    u8* byte_code_error_assembly_hash;
    const char* function_name;
    const char* extra_args;
};

struct shader_group_data
{
    u32 type;
    u32 count;
    u32 data_size;
    u8* data;
};

struct transform_component
{
    f32 position[3];
    f32 rotation[3];
    f32 scale[3];

    transform::init_info to_init_info() const;
};

struct script_component
{
    script::detail::script_creator script_creator;

    script::init_info to_init_info() const
    {
        script::init_info info{};
        info.script_creator = script_creator;
        return info;
    }
};

struct geometry_component
{
    id::id_type     geometry_content_id;
    u32             material_count;
    id::id_type*    material_ids;

    geometry::init_info to_init_info() const
    {
        geometry::init_info info{};
        info.geometry_content_id = geometry_content_id;
        info.material_count = material_count;
        info.material_ids = material_ids;
        return info;
    }
};

struct game_entity_descriptor
{
    transform_component transform;
    script_component    script;
    geometry_component  geometry;
};

utl::vector <transform::component_cache> transform_cache{};

math::v4 to_quat(math::v3 angles, bool is_degrees)
{
    using namespace DirectX;
    if (is_degrees) angles = math::to_radians(angles);

    math::v4 quat_result{};
    XMVECTOR quat{ XMQuaternionRotationRollPitchYawFromVector(XMLoadFloat3(&angles)) };
    XMStoreFloat4(&quat_result, quat);
    return quat_result;
}

transform::init_info
transform_component::to_init_info() const
{
    using namespace DirectX;
    transform::init_info info{};
    memcpy(&info.position[0], &position[0], sizeof(position));
    memcpy(&info.scale[0], &scale[0], sizeof(scale));
    math::v3 rot{ &rotation[0] };
    math::v4 rot_quat{ to_quat(rot, true) };
    memcpy(&info.rotation[0], &rot_quat.x, sizeof(info.rotation));
    return info;
}

bool tracking{ false };
void track_mouse(HWND hwnd)
{
    TRACKMOUSEEVENT tme;
    tme.cbSize = sizeof(TRACKMOUSEEVENT);
    tme.dwFlags = TME_HOVER | TME_LEAVE;
    tme.dwHoverTime = 10;
    tme.hwndTrack = hwnd;

    TrackMouseEvent(&tme);
}

LRESULT
WinProc(HWND hwnd, UINT msg, WPARAM wparam, LPARAM lparam)
{
    switch (msg)
    {
    case WM_MOUSEMOVE:
        if (!tracking)
        {
            track_mouse(hwnd);
            tracking = true;
        }
        break;
    case WM_MOUSELEAVE:
        tracking = false;
        break;
    }
    return DefWindowProc(hwnd, msg, wparam, lparam);
}

u8*
patch_material_data(u8* data)
{
    utl::blob_stream_reader blob{ data };
    const u32 texture_count{ blob.read<u32>() };
    if (texture_count)
    {
        id::id_type *const texture_ids{ (id::id_type *const)blob.position() };
        blob.skip(sizeof(id::id_type) * texture_count);
        *((id::id_type**)blob.position()) = texture_ids;
    }

    return (u8*)blob.position();
}

void
calculate_thresholds(const game_entity::entity_id *const entity_ids,
    f32 *const thresholds, u32 count, u32 surface_id)
{
    game_entity::entity camera{ game_entity::entity_id{ surfaces[surface_id].camera.entity_id() } };

    using namespace DirectX;
    math::v3 pos{ camera.position() };
    XMVECTOR camera_pos{ XMLoadFloat3(&pos) };

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(entity_ids[i]));
        pos = game_entity::entity{ entity_ids[i] }.position();
        XMVECTOR entity_pos{ XMLoadFloat3(&pos) };
        XMVECTOR distance{ XMVector3LengthEst(camera_pos - entity_pos) };
        XMStoreFloat(&thresholds[i], distance);
    }
}

game_entity::entity
create_one_game_entity(math::v3 position, math::v3 rotation, geometry::init_info* geometry_info, const char* script_name, math::v3 scale = { 1.f, 1.f, 1.f })
{
    transform::init_info transform_info{};
    DirectX::XMVECTOR quat{ DirectX::XMQuaternionRotationRollPitchYawFromVector(DirectX::XMLoadFloat3(&rotation)) };
    math::v4a rot_quat{};
    DirectX::XMStoreFloat4A(&rot_quat, quat);
    memcpy(&transform_info.rotation[0], &rot_quat.x, sizeof(transform_info.rotation));
    memcpy(&transform_info.position[0], &position.x, sizeof(transform_info.position));
    memcpy(&transform_info.scale[0], &scale.x, sizeof(transform_info.scale));

    script::init_info script_info{};
    if (script_name)
    {
        script_info.script_creator = script::detail::get_script_creator(script::detail::string_hash()(script_name));
        assert(script_info.script_creator);
    }

    game_entity::entity_info entity_info{};
    entity_info.transform = &transform_info;
    entity_info.script = &script_info;
    entity_info.geometry = geometry_info;
    game_entity::entity ntt{ game_entity::create(entity_info) };
    assert(ntt.is_valid());
    return ntt;
}

graphics::camera create_camera()
{
    game_entity::entity ntt{ create_one_game_entity({0, 1, 10}, {0, -math::pi, 0}, nullptr, nullptr) };
    return graphics::create_camera(graphics::perspective_camera_init_info{ ntt.get_id() });
}

void remove_camera(graphics::camera& camera)
{
    const game_entity::entity_id id{ camera.entity_id() };
    graphics::remove_camera(camera.get_id());
    camera = {};
    game_entity::remove(id);
}

math::v3
get_euler_angles_from_local_frame(const math::m3x3& m)
{
    float pitch{ 0.f }, yaw{ 0.f }, roll{ 0.f };
    // Assuming front is Z, up is Y and right is X
    if (m._32 < 1.f)
    {
        if (m._32 > -1.f)
        {
            pitch = asinf(-m._32);
            yaw = atan2f(m._31, m._33);
            roll = atan2f(m._12, m._22);
        }
        else
        {
            // m._32 == -1
            pitch = math::half_pi;
            yaw = -atan2f(-m._13, m._11);
            roll = 0.f;
        }
    }
    else
    {
        // m._32 == +1
        pitch = -math::half_pi;
        yaw = atan2f(-m._13, m._11);
        roll = 0.f;
    }

    return { pitch * math::to_deg, yaw * math::to_deg, roll * math::to_deg };
}

math::v3
get_local_pos(math::v3 pos, u32 id)
{
    transform::component xform{ transform::transform_id{id} };
    DirectX::XMVECTOR position{ xform.calculate_local_position(pos) };
    math::v3 result{};
    DirectX::XMStoreFloat3(&result, position);
    return result;
}

math::v4
get_world_quat(math::v3 w, u32 id)
{
    w = math::to_radians(w);
    transform::component xform{ transform::transform_id{id} };
    DirectX::XMVECTOR q{ xform.calculate_world_rotation(w) };
    math::v4 result{};
    DirectX::XMStoreFloat4(&result, q);
    return result;
}

math::v4
get_local_quat(math::v3 w, u32 id)
{
    w = math::to_radians(w);
    transform::component xform{ transform::transform_id{id} };
    DirectX::XMVECTOR q{ xform.calculate_local_rotation(w) };
    math::v4 result{};
    DirectX::XMStoreFloat4(&result, q);
    return result;
}

math::v4
get_absolute_quat(math::v3 w, u32 id)
{
    w = math::to_radians(w);
    transform::component xform{ transform::transform_id{id} };
    DirectX::XMVECTOR q{ xform.calculate_absolute_rotation(w) };
    math::v4 result{};
    DirectX::XMStoreFloat4(&result, q);
    return result;
}
} // anonymous namespace

editor::editor_api GetEditorAPI();

EDITOR_INTERFACE void
GetEngineVersion(int* major, int* minor, int* revision)
{
    *major = primal::version.major;
    *minor = primal::version.minor;
    *revision = primal::version.revision;
}

EDITOR_INTERFACE engine_init_error::error_code
InitializeEngine()
{
    while (!compile_shaders())
    {
        // Pop up a message box allowing the user to retry compilation.
        if (MessageBoxA(nullptr, "Failed to compile engine shaders.", "Shader Compilation Error", MB_RETRYCANCEL) != IDRETRY)
            return engine_init_error::shader_compilation;
    }

    if (!graphics::initialize(graphics::graphics_platform::direct3d12))
    {
        return engine_init_error::graphics;
    }

    // Create a light-set for uninitialized light-set keys in the editor
    graphics::create_light_set(0);

    // TODO: define key bindings in the editor and load them from the game file. For now, we will just use the default key bindings.
    input::input_source source{};
    source.binding = std::hash<std::string>()("move");
    source.source_type = input::input_source::keyboard;
    source.code = input::input_code::key_a;
    source.multiplier = 1.f;
    source.axis = input::axis::x;
    input::bind(source);

    source.code = input::input_code::key_d;
    source.multiplier = -1.f;
    input::bind(source);

    source.code = input::input_code::key_w;
    source.multiplier = 1.f;
    source.axis = input::axis::z;
    input::bind(source);

    source.code = input::input_code::key_s;
    source.multiplier = -1.f;
    input::bind(source);

    source.code = input::input_code::key_q;
    source.multiplier = -1.f;
    source.axis = input::axis::y;
    input::bind(source);

    source.code = input::input_code::key_e;
    source.multiplier = 1.f;
    input::bind(source);

    return engine_init_error::succeeded;
}

EDITOR_INTERFACE void
ShutdownEngine()
{
    graphics::remove_light_set(0);
    graphics::shutdown();
}

EDITOR_INTERFACE u32
LoadGameCodeDll(const char* dll_path)
{
    if (game_code_dll) return FALSE;
    game_code_dll = LoadLibraryA(dll_path);
    assert(game_code_dll);

    get_script_creator = (_get_script_creator)GetProcAddress(game_code_dll, "get_script_creator");
    get_script_names = (_get_script_names)GetProcAddress(game_code_dll, "get_script_names");

    using _set_editor_api = void(*)(uintptr_t);
    _set_editor_api set_editor_api = (_set_editor_api)GetProcAddress(game_code_dll, "SetEditorAPI");
    if (set_editor_api)
    {
        editor::editor_api api{ GetEditorAPI() };
        set_editor_api((uintptr_t)&api);
    }

    return (game_code_dll && get_script_creator && get_script_names && set_editor_api) ? TRUE : FALSE;
}

EDITOR_INTERFACE u32
UnloadGameCodeDll()
{
    if (!game_code_dll) return FALSE;
    assert(game_code_dll);
    [[maybe_unused]] int result{ FreeLibrary(game_code_dll) };
    assert(result);
    game_code_dll = nullptr;
    return TRUE;
}
EDITOR_INTERFACE script::detail::script_creator
GetScriptCreator(const char* name)
{
    return (game_code_dll && get_script_creator) ? get_script_creator(script::detail::string_hash()(name)) : nullptr;
}

EDITOR_INTERFACE LPSAFEARRAY
GetScriptNames()
{
    return (game_code_dll && get_script_names) ? get_script_names() : nullptr;
}

EDITOR_INTERFACE u32
CreateRenderSurface(HWND host, s32 width, s32 height)
{
    std::lock_guard lock{ mutex };

    assert(host);
    platform::window_init_info info{ &WinProc, host, nullptr, 0, 0, width, height };
    viewport_surface surface{};
    surface.window = platform::create_window(&info);
    surface.surface = graphics::create_surface(surface.window);
    surface.camera = create_camera();
    assert(surface.window.is_valid());
    surfaces.emplace_back(surface);
    return (u32)surfaces.size() - 1;
}

EDITOR_INTERFACE void
RemoveRenderSurface(u32 id)
{
    std::lock_guard lock{ mutex };
    assert(id < surfaces.size());
    remove_camera(surfaces[id].camera);
    graphics::remove_surface(surfaces[id].surface.get_id());
    platform::remove_window(surfaces[id].window.get_id());
}

EDITOR_INTERFACE void
ResizeRenderSurface(u32 id)
{
    std::lock_guard lock{ mutex };
    assert(id < surfaces.size());
    platform::window& window{ surfaces[id].window };
    window.resize(0, 0);
    surfaces[id].surface.resize(window.width(), window.height());
    surfaces[id].camera.aspect_ratio((f32)window.width() / window.height());
}

EDITOR_INTERFACE HWND
GetWindowHandle(u32 id)
{
    std::lock_guard lock{ mutex };
    assert(id < surfaces.size());
    return (HWND)surfaces[id].window.handle();
}

EDITOR_INTERFACE id::id_type
CreateResource(u8* data, content::asset_type::type type)
{
    if (type == content::asset_type::material)
    {
        data = patch_material_data(data);
    }

    assert(data && type < content::asset_type::count);
    return content::create_resource(data, type);
}

EDITOR_INTERFACE void
DestroyResource(id::id_type id, content::asset_type::type type)
{
    assert(id::is_valid(id) && type < content::asset_type::count);
    content::destroy_resource(id, type);
}

EDITOR_INTERFACE id::id_type
AddShaderGroup(shader_group_data* data)
{
    assert(data && data->type < graphics::shader_type::count && data->count && data->data_size && data->data);
    const u32 count{ data->count };

    // data->data =
    // {
    //    u32 keys[count];
    //    struct{
    //      u64 bytecode_length;
    //      u8  hash[hash_length];
    //      u8  bytecode[bytecode_length];
    //    } blocks[count];
    // }
    //
    utl::blob_stream_reader blob{ data->data };
    const u32 *const keys{ (const u32*)blob.position() };
    blob.skip(count * sizeof(u32)); // skip keys

    const u8** shader_pointers{ (const u8**)alloca(count * sizeof(u8*)) };

    for (u32 i{ 0 }; i < count; ++i)
    {
        // NOTE: byteCodeLength is a 64-bit value!
        const u32 block_size{ sizeof(u64) + content::compiled_shader::hash_length + *(u32*)blob.position() };
        shader_pointers[i] = blob.position();
        blob.skip(block_size);
    }

    assert(blob.position() == (data->data + data->data_size));

    return content::add_shader_group(shader_pointers, count, keys);
}

EDITOR_INTERFACE void
RemoveShaderGroup(id::id_type id)
{
    content::remove_shader_group(id);
}

EDITOR_INTERFACE u32
CompileShader(shader_data* data)
{
    assert(data && data->code && data->code_size && data->function_name);
    shader_file_info info{};
    info.function = data->function_name;
    info.type = (graphics::shader_type::type)data->type;

    utl::vector<std::string> extra_args{ split(data->extra_args, ';') };
    utl::vector<std::wstring> w_extra_args{};

    for (const auto& str : extra_args)
    {
        w_extra_args.emplace_back(to_wstring(str.c_str()));
    }

    std::unique_ptr<u8[]> compiled_shader{ compile_shader(info, data->code, data->code_size, w_extra_args, true) };

    if (!compiled_shader) return FALSE;

    u64 buffer_size{ 0 };

    {
        utl::blob_stream_reader blob{ compiled_shader.get() };
        data->byte_code_size = (u32)blob.read<u64>();
        data->hash_size = content::compiled_shader::hash_length;
        blob.skip(data->hash_size + data->byte_code_size);
        data->errors_size = (u32)blob.read<u64>();
        data->assembly_size = (u32)blob.read<u64>();
        buffer_size = data->byte_code_size + data->hash_size + data->errors_size + data->assembly_size;
    }

    assert(buffer_size);

    data->byte_code_error_assembly_hash = (u8*)CoTaskMemAlloc(buffer_size);
    assert(data->byte_code_error_assembly_hash);

    {
        utl::blob_stream_reader blob{ compiled_shader.get() };
        blob.skip(sizeof(u64)); // skip the size of byte-code buffer.
        blob.read(&data->byte_code_error_assembly_hash[buffer_size - data->hash_size], data->hash_size);
        blob.read(data->byte_code_error_assembly_hash, data->byte_code_size);
        blob.skip(2 * sizeof(u64)); // skip the size of error and assembly buffers.
        blob.read(&data->byte_code_error_assembly_hash[data->byte_code_size], data->errors_size + data->assembly_size);
    }

    return TRUE;
}

EDITOR_INTERFACE void
SetGeometryIds(u32 surface_id, id::id_type* geometry_ids, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(surface_id < surfaces.size());

    viewport_surface& surface{ surfaces[surface_id] };
    surface.geometry_ids.resize(count);

    if (count)
    {
        memcpy(surface.geometry_ids.data(), geometry_ids, count * sizeof(id::id_type));
    }
}

EDITOR_INTERFACE void
RenderFrame(u32 surface_id, id::id_type camera_id, u64 light_set)
{
    std::lock_guard lock{ mutex };
    assert(surface_id < surfaces.size());

    const viewport_surface& surface{ surfaces[surface_id] };
    const u32 count{ (u32)surface.geometry_ids.size() };
    const u64 item_id_buffer_size{ sizeof(id::id_type) * count };
    const u64 threshold_buffer_size{ sizeof(f32) * count };
    const u64 entity_id_buffer_size{ sizeof(game_entity::entity_id) * count };

    frame_info_buffer.resize(item_id_buffer_size + threshold_buffer_size + entity_id_buffer_size);

    id::id_type* item_ids{ (id::id_type*)frame_info_buffer.data() };
    f32* thresholds{ (f32*)(frame_info_buffer.data() + item_id_buffer_size) };
    game_entity::entity_id* entity_ids
    { (game_entity::entity_id*)(frame_info_buffer.data() + item_id_buffer_size + threshold_buffer_size) };

    if (count)
    {
        const id::id_type *const geometry_ids{ surface.geometry_ids.data() };
        geometry::get_entity_ids(geometry_ids, entity_ids, count);
        geometry::get_render_item_ids(geometry_ids, item_ids, count);
        calculate_thresholds(entity_ids, thresholds, count, surface_id);
    }

    graphics::frame_info info{};
    info.render_item_ids = item_ids;
    info.render_item_count = count;
    info.thresholds = thresholds;
    info.camera_id = !id::is_valid(camera_id) ? surface.camera.get_id() : graphics::camera_id{ camera_id };
    info.light_set_key = light_set;

    surface.surface.render(info);
}

EDITOR_INTERFACE void
UpdateEditorCamera(u32 surface_id, f32 pos_x, f32 pos_y, f32 pos_z, f32 rot_x, f32 rot_y, f32 rot_z)
{
    std::lock_guard lock{ mutex };
    assert(surface_id < surfaces.size());
    const id::id_type entity_id{ surfaces[surface_id].camera.entity_id() };
    assert(id::is_valid(entity_id));
    transform::component_cache cache{};
    cache.position = { pos_x, pos_y, pos_z };
    cache.rotation = to_quat({ rot_x, rot_y, rot_z }, false);
    cache.flags = transform::component_flags::position | transform::component_flags::rotation;
    cache.id = transform::transform_id{ entity_id };

    transform::update(&cache, 1);
}

EDITOR_INTERFACE id::id_type
GetSurfaceCameraId(u32 surface_id)
{
    std::lock_guard lock{ mutex };
    assert(surface_id < surfaces.size());
    return surfaces[surface_id].camera.get_id();
}

// data = {
//  u32         type;
//  f32         fov or orthographic_size;
//  f32         near_z;
//  f32         far_z;
//
EDITOR_INTERFACE id::id_type
CreateCamera(id::id_type entity_id, const u8 *const data, [[maybe_unused]] u32 data_size)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(entity_id) && data && data_size);
    math::v3 up{ game_entity::entity{ game_entity::entity_id{entity_id} }.up() };
    utl::blob_stream_reader blob{ data };
    graphics::camera_init_info info{
        .entity_id = entity_id,
        .type = (graphics::camera::type)blob.read<u32>(),
        .up = up,
        .field_of_view = blob.read<f32>() * (1.f / 180.f),
        .aspect_ratio = 16.f / 10.f,
        .near_z = blob.read<f32>(),
        .far_z = blob.read<f32>()
    };

    assert(info.type == graphics::camera::perspective || info.type == graphics::camera::orthographic);
    assert(info.field_of_view > 0.f && info.field_of_view <= 1.f);
    assert(info.orthographic_size > 0 && info.aspect_ratio > 0);
    assert(info.near_z > 0 && info.near_z <= info.far_z);

    return graphics::create_camera(info).get_id();
}

EDITOR_INTERFACE void
RemoveCamera(id::id_type camera_id)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(camera_id));
    graphics::remove_camera(graphics::camera_id{ camera_id });
}

EDITOR_INTERFACE void
GetCameraId(id::id_type entity_id, id::id_type* camera_id)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(entity_id) && camera_id);
    const graphics::camera camera{ graphics::get_camera(entity_id) };
    assert(camera.is_valid());
    *camera_id = camera.get_id();
}

EDITOR_INTERFACE void
GetCameraUp(id::id_type* ids, f32* x, f32* y, f32* z, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && x && y && z && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        const math::v3 up{ graphics::camera{ graphics::camera_id{ ids[i] } }.up() };
        x[i] = up.x; y[i] = up.y; z[i] = up.z;
    }
}

EDITOR_INTERFACE void
GetCameraFieldOfView(id::id_type* ids, f32* fovs, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && fovs && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        fovs[i] = graphics::camera{ graphics::camera_id{ ids[i] } }.field_of_view() * 180.f;
    }
}

EDITOR_INTERFACE void
GetCameraAspectRatio(id::id_type* ids, f32* aspect_ratios, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && aspect_ratios && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        aspect_ratios[i] = graphics::camera{ graphics::camera_id{ ids[i] } }.aspect_ratio();
    }
}

EDITOR_INTERFACE void
GetCameraOrthographicSize(id::id_type* ids, f32* sizes, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && sizes && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        sizes[i] = graphics::camera{ graphics::camera_id{ ids[i] } }.orthographic_size();
    }
}

EDITOR_INTERFACE void
GetCameraRange(id::id_type* ids, f32* near_zs, f32* far_zs, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && near_zs && far_zs && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        const graphics::camera camera{ graphics::camera_id{ ids[i] } };
        near_zs[i] = camera.near_z();
        far_zs[i] = camera.far_z();
    }
}

EDITOR_INTERFACE void
GetCameraView(id::id_type* ids, math::m4x4* matrices, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && matrices && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        matrices[i] = graphics::camera{ graphics::camera_id{ ids[i] } }.view();
    }
}

EDITOR_INTERFACE void
GetCameraProjection(id::id_type* ids, math::m4x4* matrices, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && matrices && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        matrices[i] = graphics::camera{ graphics::camera_id{ ids[i] } }.projection();
    }
}

EDITOR_INTERFACE void
GetCameraInverseProjection(id::id_type* ids, math::m4x4* matrices, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && matrices && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        matrices[i] = graphics::camera{ graphics::camera_id{ ids[i] } }.inverse_projection();
    }
}

EDITOR_INTERFACE void
GetCameraViewProjection(id::id_type* ids, math::m4x4* matrices, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && matrices && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        matrices[i] = graphics::camera{ graphics::camera_id{ ids[i] } }.view_projection();
    }
}

EDITOR_INTERFACE void
GetCameraInverseViewProjection(id::id_type* ids, math::m4x4* matrices, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && matrices && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        matrices[i] = graphics::camera{ graphics::camera_id{ ids[i] } }.inverse_view_projection();
    }
}

EDITOR_INTERFACE void
SetCameraUp(id::id_type* ids, f32* x, f32* y, f32* z, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && x && y && z && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        graphics::camera{ graphics::camera_id{ ids[i] } }.up({ x[i], y[i], z[i] });
    }
}

EDITOR_INTERFACE void
SetCameraFieldOfView(id::id_type* ids, f32* fovs, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && fovs && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        const f32 fov{ fovs[i] * (1.f / 180.f) };
        assert(fov > 0);
        const graphics::camera camera{ graphics::camera_id{ids[i]} };
        camera.field_of_view(fov);
    }
}

EDITOR_INTERFACE void
SetCameraAspectRatio(id::id_type* ids, f32* aspect_ratios, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && aspect_ratios && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]) && aspect_ratios[i] > 0);
        graphics::camera{ graphics::camera_id{ ids[i] } }.aspect_ratio(aspect_ratios[i]);
    }
}

EDITOR_INTERFACE void
SetCameraOrthographicSize(id::id_type* ids, f32* sizes, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && sizes && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]) && sizes[i] > 0);
        const graphics::camera camera{ graphics::camera_id{ids[i]} };
        camera.orthographic_size(sizes[i]);
    }
}

EDITOR_INTERFACE void
SetCameraRange(id::id_type* ids, f32* near_zs, f32* far_zs, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && near_zs && far_zs && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        const f32 near_z{ near_zs[i] };
        const f32 far_z{ far_zs[i] };
        assert(near_z > 0 && near_z <= far_z);
        const graphics::camera camera{ graphics::camera_id{ids[i]} };
        camera.range(near_z, far_z);
    }
}

EDITOR_INTERFACE u64
CreateLightSet(const char* light_set_key)
{
    std::lock_guard lock{ mutex };
    assert(light_set_key && light_set_key[0]);
    const u64 key{ std::hash<std::string>()(light_set_key) };
    graphics::create_light_set(key);
    return key;
}

EDITOR_INTERFACE void
RemoveLightSet(u64 light_set_key)
{
    std::lock_guard lock{ mutex };
    graphics::remove_light_set(light_set_key);
}

// data = {
//  u32         type;
//  f32         intensity;
//  math::v3    color;
//  u32         is_enabled;
//  math::u32v3 diffuse, specular, brdf_lut; (texture ids for ambient lights)
//  f32         range; (for point and spot lights)
//  math::v3    attenuation; (for point and spot lights)
//  f32         umbra; (for spot lights)
//  f32         penumbra; (for spot lights)
// }
//
EDITOR_INTERFACE id::id_type
CreateLight(id::id_type entity_id, u64 light_set_key, const u8 *const data, [[maybe_unused]] u32 data_size)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(entity_id) && data && data_size);
    utl::blob_stream_reader blob{ data };
    graphics::light_init_info info{};
    info.light_set_key = light_set_key;
    info.entity_id = entity_id;
    info.type = (graphics::light::type)blob.read<u32>();
    info.intensity = blob.read<f32>();
    info.color = { blob.read<f32>(), blob.read<f32>(), blob.read<f32>() };
    info.is_enabled = blob.read<u32>() != 0;

    if (info.type == graphics::light::ambient)
    {
        info.ambient_params.diffuse_texture_id = blob.read<u32>();
        info.ambient_params.specular_texture_id = blob.read<u32>();
        info.ambient_params.brdf_lut_texture_id = blob.read<u32>();
    }
    else if (info.type == graphics::light::point)
    {
        info.point_params.range = blob.read<f32>();
        info.point_params.attenuation = { blob.read<f32>(), blob.read<f32>(), blob.read<f32>() };
    }
    else   if (info.type == graphics::light::spot)
    {
        info.spot_params.range = blob.read<f32>();
        info.spot_params.attenuation = { blob.read<f32>(), blob.read<f32>(), blob.read<f32>() };
        info.spot_params.umbra = blob.read<f32>() * math::to_rad;
        info.spot_params.penumbra = blob.read<f32>() * math::to_rad;
    }

    return graphics::create_light(info).get_id();
}

EDITOR_INTERFACE void
RemoveLight(id::id_type light_id, u64 light_set_key)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(light_id));
    graphics::remove_light(graphics::light_id{ light_id }, light_set_key);
}

EDITOR_INTERFACE void
GetLightId(id::id_type entity_id, id::id_type* light_id, u64* light_set_key)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(entity_id) && light_id && light_set_key);
    const graphics::light light{ graphics::get_light(entity_id) };
    *light_id = light.get_id();
    *light_set_key = light.light_set_key();
}

EDITOR_INTERFACE void
GetLightIsEnabled(id::id_type* ids, u64* light_set_keys, u32* is_enabled, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && light_set_keys && is_enabled && count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        graphics::light light{ graphics::light_id{ ids[i] }, light_set_keys[i] };
        is_enabled[i] = light.is_enabled() ? 1 : 0;
    }
}

EDITOR_INTERFACE void
GetLightIntensity(id::id_type* ids, u64* light_set_keys, f32* intensities, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && light_set_keys && intensities && count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        graphics::light light{ graphics::light_id{ ids[i] }, light_set_keys[i] };
        intensities[i] = light.intensity();
    }
}

EDITOR_INTERFACE void
GetLightRange(id::id_type* ids, u64* light_set_keys, f32* ranges, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && light_set_keys && ranges && count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        graphics::light light{ graphics::light_id{ ids[i] }, light_set_keys[i] };
        ranges[i] = light.range();
    }
}

EDITOR_INTERFACE void
GetLightConeAngles(id::id_type* ids, u64* light_set_keys, f32* umbras, f32* penumbras, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && light_set_keys && umbras && penumbras && count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        graphics::light light{ graphics::light_id{ ids[i] }, light_set_keys[i] };
        umbras[i] = light.umbra() * math::to_deg;
        penumbras[i] = light.penumbra() * math::to_deg;
    }
}

EDITOR_INTERFACE void
GetLightColor(id::id_type* ids, u64* light_set_keys, f32* r, f32* g, f32* b, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && light_set_keys && r && g && b && count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        graphics::light light{ graphics::light_id{ ids[i] }, light_set_keys[i] };
        const math::v3 color{ light.color() };
        r[i] = color.x; g[i] = color.y; b[i] = color.z;
    }
}

EDITOR_INTERFACE void
GetLightAttenuation(id::id_type* ids, u64* light_set_keys, f32* a, f32* b, f32* c, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && light_set_keys && a && b && c && count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        graphics::light light{ graphics::light_id{ ids[i] }, light_set_keys[i] };
        const math::v3 attenuation{ light.attenuation() };
        a[i] = attenuation.x; b[i] = attenuation.y; c[i] = attenuation.z;
    }
}

EDITOR_INTERFACE void
SetLightIsEnabled(id::id_type* ids, u64* light_set_keys, u32* is_enabled, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && light_set_keys && is_enabled && count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        graphics::light light{ graphics::light_id{ ids[i] }, light_set_keys[i] };
        light.is_enabled(is_enabled[i] != 0);
    }
}

EDITOR_INTERFACE void
SetLightIntensity(id::id_type* ids, u64* light_set_keys, f32* intensities, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && light_set_keys && intensities && count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        graphics::light light{ graphics::light_id{ ids[i] }, light_set_keys[i] };
        light.intensity(intensities[i]);
    }
}

EDITOR_INTERFACE void
SetLightRange(id::id_type* ids, u64* light_set_keys, f32* ranges, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && light_set_keys && ranges && count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        graphics::light light{ graphics::light_id{ ids[i] }, light_set_keys[i] };
        light.range(ranges[i]);
    }
}

EDITOR_INTERFACE void
SetLightConeAngles(id::id_type* ids, u64* light_set_keys, f32* umbras, f32* penumbras, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && light_set_keys && umbras && penumbras && count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        graphics::light light{ graphics::light_id{ ids[i] }, light_set_keys[i] };
        light.cone_angles(umbras[i] * math::to_rad, penumbras[i] * math::to_rad);
    }
}

EDITOR_INTERFACE void
SetLightColor(id::id_type* ids, u64* light_set_keys, f32* r, f32* g, f32* b, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && light_set_keys && r && g && b && count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        graphics::light light{ graphics::light_id{ ids[i] }, light_set_keys[i] };
        light.color({ r[i], g[i], b[i] });
    }
}

EDITOR_INTERFACE void
SetLightAttenuation(id::id_type* ids, u64* light_set_keys, f32* a, f32* b, f32* c, u32 count)
{
    std::lock_guard lock{ mutex };
    assert(ids && light_set_keys && a && b && c && count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(id::is_valid(ids[i]));
        graphics::light light{ graphics::light_id{ ids[i] }, light_set_keys[i] };
        light.attenuation({ a[i], b[i], c[i] });
    }
}

EDITOR_INTERFACE id::id_type
CreateGameEntity(game_entity_descriptor* e)
{
    assert(e);
    game_entity_descriptor& desc{ *e };
    transform::init_info transform_info{ desc.transform.to_init_info() };
    script::init_info script_info{ desc.script.to_init_info() };
    geometry::init_info geometry_info{ desc.geometry.to_init_info() };
    game_entity::entity_id id{ id::invalid_id };
    {
        game_entity::entity_info entity_info
        {
            .transform = &transform_info,
            .script = nullptr,
            .geometry = id::is_valid(desc.geometry.geometry_content_id) ? &geometry_info : nullptr,
        };

        std::lock_guard lock{ mutex };
        id = game_entity::create(entity_info).get_id();
    }

    // NOTE: The script component is added without a lock to avoid dead-locks if the script's constructor
    //       tries to access the game entity API. The script component is added after the entity is created
    if (script_info.script_creator && id::is_valid(id))
    {
        game_entity::entity_info entity_info
        {
            .transform = nullptr,
            .script = &script_info,
            .geometry = nullptr,
        };

        game_entity::update_component(id, entity_info, component_type::script);
    }

    return id;
}

EDITOR_INTERFACE void
RemoveGameEntity(id::id_type id)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(id));
    game_entity::remove(game_entity::entity_id{ id });
}

EDITOR_INTERFACE b32
UpdateComponent(id::id_type entity_id, game_entity_descriptor* e, component_type::type type)
{
    assert(id::is_valid(entity_id) && e && type != component_type::transform);
    game_entity_descriptor& desc{ *e };
    script::init_info script_info{ desc.script.to_init_info() };
    geometry::init_info geometry_info{ desc.geometry.to_init_info() };
    game_entity::entity_info entity_info
    {
        nullptr,
        &script_info,
        id::is_valid(desc.geometry.geometry_content_id) ? &geometry_info : nullptr,
    };
    if (type != component_type::script)
    {
        std::lock_guard lock{ mutex };
        return game_entity::update_component(game_entity::entity_id{ entity_id }, entity_info, type) ? 1 : 0;
    }
    else
    {
        // NOTE: The script component is added without a lock to avoid dead-locks if the script's constructor
        //       tries to access the game entity API. The script component is added after the entity is created
        return game_entity::update_component(game_entity::entity_id{ entity_id }, entity_info, type) ? 1 : 0;
    }
}

EDITOR_INTERFACE id::id_type
GetComponentId(id::id_type entity_id, u32 type)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(entity_id));
    game_entity::entity entity{ game_entity::entity_id{ entity_id } };

    switch (type)
    {
    case component_type::transform: return entity.transform().get_id();
    case component_type::script:    return entity.script().get_id();
    case component_type::geometry:  return entity.geometry().get_id();
    default: return id::invalid_id;
    }
}

EDITOR_INTERFACE void
GetPosition(id::id_type* ids, f32* x, f32* y, f32* z, u32 count)
{
    assert(ids && count);
    std::lock_guard lock{ mutex };

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(transform::transform_id{ ids[i] } == game_entity::entity{ game_entity::entity_id{ids[i]} }.transform().get_id());
        // NOTE: transform id is the same as entity id
        const transform::transform_id id{ ids[i] };
        transform::component t{ id };
        math::v3 pos{ t.position() };
        x[i] = pos.x; y[i] = pos.y; z[i] = pos.z;
    }
}

EDITOR_INTERFACE void
GetRotation(id::id_type* ids, f32* x, f32* y, f32* z, u32 count)
{
    assert(ids && count);
    std::lock_guard lock{ mutex };
    using namespace DirectX;

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(transform::transform_id{ ids[i] } == game_entity::entity{ game_entity::entity_id{ids[i]} }.transform().get_id());
        // NOTE: transform id is the same as entity id
        const transform::transform_id id{ ids[i] };
        transform::component t{ id };
        math::v3 euler{ get_euler_angles_from_local_frame(t.local_frame()) };
        x[i] = euler.x; y[i] = euler.y; z[i] = euler.z;
    }
}

EDITOR_INTERFACE void
GetScale(id::id_type* ids, f32* x, f32* y, f32* z, u32 count)
{
    assert(ids && count);
    std::lock_guard lock{ mutex };

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(transform::transform_id{ ids[i] } == game_entity::entity{ game_entity::entity_id{ids[i]} }.transform().get_id());
        // NOTE: transform id is the same as entity id
        const transform::transform_id id{ ids[i] };
        transform::component t{ id };
        math::v3 scale{ t.scale() };
        x[i] = scale.x; y[i] = scale.y; z[i] = scale.z;
    }
}

EDITOR_INTERFACE void
SetPosition(id::id_type* ids, f32* x, f32* y, f32* z, u32 count, b32 is_local)
{
    assert(ids && count);
    std::lock_guard lock{ mutex };

    transform_cache.resize(count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        math::v3 v{ x[i], y[i], z[i] };
        transform_cache[i].position = !is_local ? v : get_local_pos(v, ids[i]);
        transform_cache[i].flags = transform::component_flags::position;
        assert(transform::transform_id{ ids[i] } == game_entity::entity{ game_entity::entity_id{ids[i]} }.transform().get_id());
        // NOTE: transform id is the same as entity id
        transform_cache[i].id = transform::transform_id{ ids[i] };
    }

    transform::update(transform_cache.data(), count);
}

EDITOR_INTERFACE void
SetRotation(id::id_type* ids, f32* x, f32* y, f32* z, u32 count, u32 frame)
{
    assert(ids && count);
    std::lock_guard lock{ mutex };

    transform_cache.resize(count);

    using frame_fn_ptr = math::v4(*)(math::v3, u32);
    frame_fn_ptr frame_fn{ &get_absolute_quat };

    if (frame == transform::space::local)
    {
        frame_fn = &get_local_quat;
    }
    else if (frame == transform::space::world)
    {
        frame_fn = &get_world_quat;
    }

    for (u32 i{ 0 }; i < count; ++i)
    {
        math::v3 v{ x[i], y[i], z[i] };
        transform_cache[i].rotation = frame_fn(v, ids[i]);
        transform_cache[i].flags = transform::component_flags::rotation;
        assert(transform::transform_id{ ids[i] } == game_entity::entity{ game_entity::entity_id{ids[i]} }.transform().get_id());
        // NOTE: transform id is the same as entity id
        transform_cache[i].id = transform::transform_id{ ids[i] };
    }

    transform::update(transform_cache.data(), count);
}

EDITOR_INTERFACE void
SetScale(id::id_type* ids, f32* x, f32* y, f32* z, u32 count, b32)
{
    assert(ids && count);
    std::lock_guard lock{ mutex };

    transform_cache.resize(count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        transform_cache[i].scale = { x[i], y[i], z[i] };
        transform_cache[i].flags = transform::component_flags::scale;
        assert(transform::transform_id{ ids[i] } == game_entity::entity{ game_entity::entity_id{ids[i]} }.transform().get_id());
        // NOTE: transform id is the same as entity id
        transform_cache[i].id = transform::transform_id{ ids[i] };
    }

    transform::update(transform_cache.data(), count);
}

EDITOR_INTERFACE void
BeginPlay(id::id_type id)
{
    script::begin_play(script::script_id{ id });
}

EDITOR_INTERFACE void
BeginPlayAll()
{
    script::begin_play();
}

EDITOR_INTERFACE void
UpdateScripts(float dt)
{
    script::update(dt);
}
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Additional exports for Game Code DLL
EDITOR_INTERFACE void
GetLocalFrame(id::id_type id, math::m3x3* frame)
{
    assert(id::is_valid(id) && frame);
    std::lock_guard lock{ mutex };
    assert(transform::transform_id{ id } == game_entity::entity{ game_entity::entity_id{id} }.transform().get_id());
    // NOTE: transform id is the same as entity id
    const transform::transform_id transform_id{ id };
    transform::component t{ transform_id };
    *frame = t.local_frame();
}

EDITOR_INTERFACE void
ScriptSetRotation(id::id_type entity_id, f32 x, f32 y, f32 z, f32 w)
{
    std::lock_guard lock{ mutex };
    math::v4 q{ x, y, z, w };
    struct s : public script::entity_script {
        explicit s(game_entity::entity entity, math::v4 rotation)
            : script::entity_script{ entity } {
            set_rotation(rotation);
        }
    } s{ game_entity::entity{ game_entity::entity_id{ entity_id } }, q };
}

EDITOR_INTERFACE void
ScriptSetPosition(id::id_type entity_id, f32 x, f32 y, f32 z)
{
    std::lock_guard lock{ mutex };
    math::v3 pos{ x, y, z };
    struct s : public script::entity_script {
        explicit s(game_entity::entity entity, math::v3 position)
            : script::entity_script{ entity } {
            set_position(position);
        }
    } s{ game_entity::entity{ game_entity::entity_id{ entity_id } }, pos };
}

EDITOR_INTERFACE void
ScriptSetScale(id::id_type entity_id, f32 x, f32 y, f32 z)
{
    std::lock_guard lock{ mutex };
    math::v3 scale{ x, y, z };
    struct s : public script::entity_script {
        explicit s(game_entity::entity entity, math::v3 scale)
            : script::entity_script{ entity } {
            set_scale(scale);
        }
    } s{ game_entity::entity{ game_entity::entity_id{ entity_id } }, scale };
}

EDITOR_INTERFACE u8
RegisterScript(size_t tag, uintptr_t creator)
{
    //std::lock_guard lock{ mutex };
    return script::detail::register_script(tag, (script::detail::script_creator)creator);
}

EDITOR_INTERFACE u8
AddScriptName(const char* name)
{
    //std::lock_guard lock{ mutex };
    return script::detail::add_script_name(name);
}

namespace primal::input {
void add_input_callback(detail::input_system_base*);
void remove_input_callback(detail::input_system_base*);
}

EDITOR_INTERFACE void
AddInputCallback(void* callback)
{
    assert(callback);
    primal::input::add_input_callback(static_cast<primal::input::detail::input_system_base*>(callback));
}

EDITOR_INTERFACE void
RemoveInputCallback(void* callback)
{
    assert(callback);
    primal::input::remove_input_callback(static_cast<primal::input::detail::input_system_base*>(callback));
}

EDITOR_INTERFACE void
GetInput(u32 type, u32 code, f32* value)
{
    assert(value);
    //std::lock_guard lock{ mutex };
    input::get((input::input_source::type)type, (input::input_code::code)code, (input::input_value&)*value);
}

EDITOR_INTERFACE void
GetInputBinding(u64 binding, f32* value)
{
    assert(value);
    //std::lock_guard lock{ mutex };
    input::get(binding, (input::input_value&)*value);
}

EDITOR_INTERFACE u32
GetCameraProjectionType(id::id_type id)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(id));
    return graphics::camera{ graphics::camera_id{ id } }.projection_type();
}

EDITOR_INTERFACE id::id_type
GetCameraEntityId(id::id_type id)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(id));
    return graphics::camera{ graphics::camera_id{ id } }.entity_id();
}

EDITOR_INTERFACE u32
GetLightType(id::id_type id, u64 light_set_key)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(id));
    return graphics::light{ graphics::light_id{ id }, light_set_key }.light_type();
}

EDITOR_INTERFACE id::id_type
GetLightEntityId(id::id_type id, u64 light_set_key)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(id));
    return graphics::light{ graphics::light_id{ id }, light_set_key }.entity_id();
}

#include "../Engine/EditorAPI/EditorAPI.h"

editor::editor_api
GetEditorAPI()
{
    return {
        .GetEngineVersion = GetEngineVersion,
        // CAMERA
        .GetCameraId = GetCameraId,
        .GetCameraUp = GetCameraUp,
        .GetCameraFieldOfView = GetCameraFieldOfView,
        .GetCameraAspectRatio = GetCameraAspectRatio,
        .GetCameraOrthographicSize = GetCameraOrthographicSize,
        .GetCameraRange = GetCameraRange,
        .GetCameraView = GetCameraView,
        .GetCameraProjection = GetCameraProjection,
        .GetCameraInverseProjection = GetCameraInverseProjection,
        .GetCameraViewProjection = GetCameraViewProjection,
        .GetCameraInverseViewProjection = GetCameraInverseViewProjection,
        .GetCameraProjectionType = GetCameraProjectionType,
        .GetCameraEntityId = GetCameraEntityId,
        .SetCameraUp = SetCameraUp,
        .SetCameraFieldOfView = SetCameraFieldOfView,
        .SetCameraAspectRatio = SetCameraAspectRatio,
        .SetCameraOrthographicSize = SetCameraOrthographicSize,
        .SetCameraRange = SetCameraRange,
        // LIGHT
        .GetLightId = GetLightId,
        .GetLightIsEnabled = GetLightIsEnabled,
        .GetLightIntensity = GetLightIntensity,
        .GetLightRange = GetLightRange,
        .GetLightConeAngles = GetLightConeAngles,
        .GetLightColor = GetLightColor,
        .GetLightAttenuation = GetLightAttenuation,
        .GetLightType = GetLightType,
        .GetLightEntityId = GetLightEntityId,
        .SetLightIsEnabled = SetLightIsEnabled,
        .SetLightIntensity = SetLightIntensity,
        .SetLightRange = SetLightRange,
        .SetLightConeAngles = SetLightConeAngles,
        .SetLightColor = SetLightColor,
        .SetLightAttenuation = SetLightAttenuation,
        // ENTITY / COMPONENT
        .GetComponentId = GetComponentId,
        .GetPosition = GetPosition,
        .GetRotation = GetRotation,
        .GetScale = GetScale,
        .SetPosition = SetPosition,
        .SetRotation = SetRotation,
        .SetScale = SetScale,
        .GetLocalFrame = GetLocalFrame,
        .ScriptSetRotation = ScriptSetRotation,
        .ScriptSetPosition = ScriptSetPosition,
        .ScriptSetScale = ScriptSetScale,
        .RegisterScript = RegisterScript,
        .AddScriptName = AddScriptName,
        // INPUT
        .AddInputCallback = AddInputCallback,
        .RemoveInputCallback = RemoveInputCallback,
        .GetInput = GetInput,
        .GetInputBinding = GetInputBinding
    };
}
