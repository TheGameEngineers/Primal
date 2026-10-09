// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#if !defined(SHIPPING) && defined(_WIN64)
#include "Content/ContentLoader.h"
#include "Components/Script.h"
#include "Components/Geometry.h"
#include "Platform/PlatformTypes.h"
#include "Platform/Platform.h"
#include "Graphics/Renderer.h"
#include "Input/Input.h"
#include <thread>

using namespace primal;
namespace {

struct game_window : public graphics::render_surface {
    utl::vector<id::id_type> geometry_ids{};
    utl::vector<u8> frame_info_buffer{};
    graphics::camera camera{};
    bool resized{ false };
} game_window;

LRESULT win_proc(HWND hwnd, UINT msg, WPARAM wparam, LPARAM lparam)
{
    bool toggle_fullscreen{ false };

    switch (msg)
    {
    case WM_DESTROY:
    {
        if (game_window.window.is_closed())
        {
            PostQuitMessage(0);
            return 0;
        }
    }
    break;
    case WM_SIZE:
        game_window.resized = (wparam != SIZE_MINIMIZED);
        break;
    case WM_SYSCHAR:
        toggle_fullscreen = (wparam == VK_RETURN && (HIWORD(lparam) & KF_ALTDOWN));
        break;
    case WM_KEYDOWN:
        if (wparam == VK_ESCAPE)
        {
            PostMessage(hwnd, WM_CLOSE, 0, 0);
            return 0;
        }
    }

    if ((game_window.resized && GetKeyState(VK_LBUTTON) >= 0) || toggle_fullscreen)
    {
        platform::window win{ game_window.window };

        if (toggle_fullscreen)
        {
            win.set_fullscreen(!win.is_fullscreen());
            // The default window procedure will play a system notification sound
            // when pressing the Alt+Enter keyboard combination if WM_SYSCHAR is
            // not handled. By returning 0 we can tell the system that we handled
            // this message.
            return 0;
        }
        else if (win.width() && win.height())
        {
            game_window.surface.resize(win.width(), win.height());
            game_window.camera.aspect_ratio((f32)win.width() / win.height());
            game_window.resized = false;
        }
    }

    return DefWindowProc(hwnd, msg, wparam, lparam);
}

void
calculate_thresholds(const game_entity::entity_id *const entity_ids,
    f32 *const thresholds, u32 count)
{
    game_entity::entity camera{ game_entity::entity_id{ game_window.camera.get_id() } };

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

} // anonymous namespace

bool
engine_initialize()
{
    // TODO: implement and get engine and game settings. For now use D3D12
    if (!graphics::initialize(graphics::graphics_platform::direct3d12)) return false;

    if (!content::load_game()) return false;

    platform::window_init_info info
    {
        &win_proc, nullptr, L"Primal Game" // TODO: get the game name from the loaded game file
    };

    platform::window win{ platform::create_window(&info) };
    game_window.window = win;
    if (!win.is_valid() || !win.width() || !win.height()) return false;

    game_window.surface = graphics::create_surface(win);
    if (!game_window.surface.is_valid()) return false;

    // TODO: a scene can have multiple cameras, but for now we will just use the first camera in the scene.
    game_window.camera = graphics::camera{ graphics::camera_id{ content::get_camera() } };
    if (!id::is_valid(game_window.camera.get_id())) return false;

    game_window.camera.aspect_ratio((f32)win.width() / win.height());

    game_window.geometry_ids.resize(geometry::get_render_item_count());
    geometry::get_render_item_ids(game_window.geometry_ids.data(), (u32)game_window.geometry_ids.size());

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

    return true;
}

void
engine_update()
{
    script::update(1.f / 60.f);

    const u32 count{ (u32)game_window.geometry_ids.size() };
    const u64 item_id_buffer_size{ sizeof(id::id_type) * count };
    const u64 threshold_buffer_size{ sizeof(f32) * count };
    const u64 entity_id_buffer_size{ sizeof(game_entity::entity_id) * count };

    game_window.frame_info_buffer.resize(item_id_buffer_size + threshold_buffer_size + entity_id_buffer_size);

    id::id_type* item_ids{ (id::id_type*)game_window.frame_info_buffer.data() };
    f32* thresholds{ (f32*)(game_window.frame_info_buffer.data() + item_id_buffer_size) };
    game_entity::entity_id* entity_ids
    { (game_entity::entity_id*)(game_window.frame_info_buffer.data() + item_id_buffer_size + threshold_buffer_size) };

    if (count)
    {
        const id::id_type *const geometry_ids{ game_window.geometry_ids.data() };
        geometry::get_entity_ids(geometry_ids, entity_ids, count);
        geometry::get_render_item_ids(geometry_ids, item_ids, count);
        calculate_thresholds(entity_ids, thresholds, count);
    }

    graphics::frame_info info{};
    info.render_item_ids = item_ids;
    info.render_item_count = count;
    info.thresholds = thresholds;
    info.camera_id = game_window.camera.get_id();
    info.light_set_key = content::get_current_light_set_key();

    game_window.surface.render(info);
}

void
engine_shutdown()
{
    graphics::remove_surface(game_window.surface.get_id());
    platform::remove_window(game_window.window.get_id());
    content::unload_game();
    graphics::shutdown();
}
#endif // !defined(SHIPPING)