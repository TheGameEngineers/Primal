// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#if !defined(SHIPPING) && defined(_WIN64)
#include "Content/ContentLoader.h"
#include "Components/Script.h"
#include "Platform/PlatformTypes.h"
#include "Platform/Platform.h"
#include "Graphics/Renderer.h"
#include <thread>

using namespace primal;
namespace {

graphics::render_surface game_window{};

LRESULT win_proc(HWND hwnd, UINT msg, WPARAM wparam, LPARAM lparam)
{
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
    case WM_SYSCHAR:
        if (wparam == VK_RETURN && (HIWORD(lparam) & KF_ALTDOWN))
        {
            game_window.window.set_fullscreen(!game_window.window.is_fullscreen());
            return 0;
        }
        break;
    }

    return DefWindowProc(hwnd, msg, wparam, lparam);
}

} // anonymous namespace

bool engine_initialize()
{
    if (!primal::content::load_game()) return false;

    platform::window_init_info info
    {
        &win_proc, nullptr, L"Primal Game" // TODO: get the game name from the loaded game file
    };

    game_window.window = platform::create_window(&info);
    if (!game_window.window.is_valid()) return false;

    return true;
}

void engine_update()
{
    primal::script::update(10.f);
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
}

void engine_shutdown()
{
    platform::remove_window(game_window.window.get_id());
    primal::content::unload_game();
}
#endif // !defined(SHIPPING)