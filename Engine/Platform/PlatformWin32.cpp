// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#ifdef _WIN64
#include "Platform.h"
#include "Input/Input.h"
#include "Input/InputWin32.h"

namespace primal::platform {

namespace {
struct window_info {
    enum flags : u8 {
        fullscreen = 0x01,
        closed = 0x02,
    };

    HWND            hwnd{ nullptr };
    RECT            client_area{ 0, 0, 1920, 1080 };
    RECT            fullscreen_area{};
    RECT            fullscreen_bounds{};
    POINT           top_left{ 0, 0 };
    WINDOWPLACEMENT windowed_placement{ sizeof(WINDOWPLACEMENT) };
    DWORD           style{ WS_VISIBLE };
    DWORD           extended_style{ 0 };
    u8              flags{};

    bool is_fullscreen() const { return flags & fullscreen; }
    bool is_closed() const { return flags & closed; }
};

utl::free_list<window_info> windows;

constexpr wchar_t window_class_name[]{ L"PrimalWindow" };

window_info&
get_from_id(window_id id)
{
    assert(windows[id].hwnd);
    return windows[id];
}

window_info*
try_get_from_handle(window_handle handle)
{
    static window_info dummy{};

    if (!handle || !IsWindow(handle)) return &dummy;

#ifdef _DEBUG
    wchar_t class_name[sizeof(window_class_name) / sizeof(window_class_name[0])]{};
    if (!GetClassNameW(handle, class_name, (u32)(sizeof(class_name) / sizeof(class_name[0])))
        || lstrcmpW(class_name, window_class_name) != 0) return &dummy;
#endif

    const id::id_type encoded_id{ (id::id_type)GetWindowLongPtr(handle, GWLP_USERDATA) };
    if (encoded_id <= 0) return &dummy;

    const id::id_type id{ encoded_id - 1 };
    if (id >= windows.capacity()) return &dummy;

    window_info& info{ windows[id] };
    return info.hwnd == handle ? &info : &dummy;
}

window_info&
get_from_handle(window_handle handle)
{
    window_info* info{ try_get_from_handle(handle) };
    assert(info->hwnd);
    return *info;
}

bool resized{ false };
bool moved{ false };

LRESULT
CALLBACK internal_window_proc(HWND hwnd, UINT msg, WPARAM wparam, LPARAM lparam)
{
    switch (msg)
    {
    case WM_NCCREATE:
    {
        const CREATESTRUCTW* create_info{ (const CREATESTRUCTW*)lparam };
        const UINT_PTR encoded_id{ (UINT_PTR)create_info->lpCreateParams };
        if (!encoded_id) return FALSE;
        const window_id id{ (id::id_type)(encoded_id - 1) };
        if (id >= windows.capacity()) return FALSE;

        windows[id].hwnd = hwnd;
        SetLastError(0);
        if (!SetWindowLongPtr(hwnd, GWLP_USERDATA, (LONG_PTR)encoded_id) && GetLastError())
        {
            windows[id].hwnd = nullptr;
            return FALSE;
        }
    }
    break;
    case WM_DESTROY:
        get_from_handle(hwnd).flags |= window_info::closed;
        break;
#ifdef USE_WITH_EDITOR
    case WM_INPUT:
    {
        constexpr u32 buffer_size{ 64 * sizeof(RAWINPUT) };
        u8 buffer[buffer_size]{};
        u32 required_size{ buffer_size };
        if (GetRawInputData((HRAWINPUT)lparam, RID_INPUT, buffer, &required_size, sizeof(RAWINPUTHEADER)) != u32_invalid_id)
        {
            input::process_raw_input(&buffer[0], required_size);
        }
        input::process();
    }
    break;
#endif // USE_WITH_EDITOR
    case WM_SETFOCUS:
        input::update_mouse_behavior(hwnd, true);
        break;
    case WM_ACTIVATE:
        input::update_mouse_behavior(hwnd, LOWORD(wparam) != WA_INACTIVE);
        break;    
    case WM_KILLFOCUS:
        input::update_mouse_behavior(hwnd, false);
        break;
    case WM_SIZE:
        resized = (wparam != SIZE_MINIMIZED);
        break;
    case WM_MOVE:
        moved = true;
        break;
    case WM_DISPLAYCHANGE:
    {
        window_info& info{ get_from_handle(hwnd) };
        if (info.is_fullscreen())
        {
            MONITORINFO monitor_info{ sizeof(MONITORINFO) };
            const HMONITOR monitor{ MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST) };
            if (GetMonitorInfoW(monitor, &monitor_info))
            {
                const RECT bounds{ monitor_info.rcMonitor };
                if (SetWindowPos(hwnd, HWND_TOPMOST, bounds.left, bounds.top,
                    bounds.right - bounds.left, bounds.bottom - bounds.top,
                    SWP_FRAMECHANGED | SWP_NOOWNERZORDER | SWP_NOACTIVATE))
                {
                    info.fullscreen_bounds = bounds;
                }
            }
        }
        break;
    }
    }

    if ((resized || moved) && GetKeyState(VK_LBUTTON) >= 0)
    {
        window_info& info{ get_from_handle(hwnd) };
        if (resized)
        {
            RECT area{};
            if (GetClientRect(hwnd, &area))
            {
                (info.is_fullscreen() ? info.fullscreen_area : info.client_area) = area;
                input::update_mouse_behavior(hwnd, true);
                resized = false;
            }
        }

        if (moved && !info.is_fullscreen())
        {
            RECT rect{};
            if (GetWindowRect(hwnd, &rect))
            {
                POINT top_left{ rect.left, rect.top };
                if ((info.style & WS_CHILD) && GetParent(hwnd))
                {
                    MapWindowPoints(nullptr, GetParent(hwnd), &top_left, 1);
                }
                info.top_left = top_left;
                input::update_mouse_behavior(hwnd, true);
                moved = false;
            }
        }
    }

    if (msg == WM_SYSCOMMAND && (wparam & 0xfff0) == SC_KEYMENU)
    {
        return 0;
    }
    else if (msg == WM_NCDESTROY)
    {
        SetWindowLongPtr(hwnd, GWLP_USERDATA, 0);
    }

    LONG_PTR long_ptr{ GetWindowLongPtr(hwnd, 0) };
    return long_ptr
        ? ((window_proc)long_ptr)(hwnd, msg, wparam, lparam)
        : DefWindowProc(hwnd, msg, wparam, lparam);
}

bool
register_window_class()
{
    // Try to register this window class only once.
    static const bool registered{ []
    {
        const HINSTANCE instance{ GetModuleHandleW(nullptr) };
        if (!instance) return false;
        HBRUSH background{ CreateSolidBrush(RGB(26, 48, 76)) };
        if (!background) return false;

        WNDCLASSEXW wc{};
        wc.cbSize = sizeof(wc);
        wc.style = CS_HREDRAW | CS_VREDRAW;
        wc.lpfnWndProc = internal_window_proc;
        wc.cbWndExtra = sizeof(window_proc);
        wc.hInstance = instance;
        wc.hIcon = LoadIconW(nullptr, IDI_APPLICATION);
        wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        wc.hbrBackground = background;
        wc.lpszClassName = window_class_name;
        wc.hIconSm = LoadIconW(nullptr, IDI_APPLICATION);

        if (RegisterClassExW(&wc)) return true;

        const DWORD error{ GetLastError() };
        DeleteObject(background);
        if (error != ERROR_CLASS_ALREADY_EXISTS) return false;

        WNDCLASSEXW existing{};
        existing.cbSize = sizeof(existing);
        return GetClassInfoExW(instance, window_class_name, &existing)
            && existing.lpfnWndProc == internal_window_proc;
    }() };
    return registered;
}

bool
set_window_style(HWND hwnd, DWORD style)
{
    SetLastError(0);
    return SetWindowLongPtr(hwnd, GWL_STYLE, style) != 0 || GetLastError() == 0;
}

bool
set_window_ex_style(HWND hwnd, DWORD style)
{
    SetLastError(0);
    return SetWindowLongPtr(hwnd, GWL_EXSTYLE, style) != 0 || GetLastError() == 0;
}

bool
resize_window(const window_info& info, const RECT& area)
{
    RECT window_rect{ area };
    UINT dpi{ GetDpiForWindow(info.hwnd) };
    if (!dpi) dpi = USER_DEFAULT_SCREEN_DPI;
    if (!AdjustWindowRectExForDpi(&window_rect, info.style, FALSE, info.extended_style, dpi)) return false;

    const s32 width{ window_rect.right - window_rect.left };
    const s32 height{ window_rect.bottom - window_rect.top };

    return MoveWindow(info.hwnd, info.top_left.x, info.top_left.y, width, height, TRUE) != FALSE;
}

void
resize_window(window_id id, u32 width, u32 height)
{
    window_info& info{ get_from_id(id) };

    // NOTE: when we host the window in the level editor we just update
    //       the internal data (i.e. the client area dimensions).
    if (info.style & WS_CHILD)
    {
        GetClientRect(info.hwnd, &info.client_area);
    }
    else
    {
        RECT area{ 0, 0, (LONG)width, (LONG)height };
        if (info.is_fullscreen())
        {
            if (SetWindowPos(info.hwnd, nullptr, info.fullscreen_bounds.left, info.fullscreen_bounds.top,
                (s32)width, (s32)height, SWP_NOZORDER | SWP_NOACTIVATE))
            {
                info.fullscreen_area = area;
            }
        }
        else if (resize_window(info, area))
        {
            info.client_area = area;
        }
    }
}

void
set_window_fullscreen(window_id id, bool is_fullscreen)
{
    window_info& info{ get_from_id(id) };
    
    if (info.is_fullscreen() == is_fullscreen) return;

    if (is_fullscreen)
    {
        // Child windows cannot become standalone fullscreen windows.
        if (info.style & WS_CHILD) return;

        // Save the current windowed state so we can restore it later.
        WINDOWPLACEMENT placement{ sizeof(WINDOWPLACEMENT) };
        RECT client_area{};
        RECT window_rect{};
        MONITORINFO monitor_info{ sizeof(MONITORINFO) };
        const HMONITOR monitor{ MonitorFromWindow(info.hwnd, MONITOR_DEFAULTTONEAREST) };
        if (!GetWindowPlacement(info.hwnd, &placement)
            || !GetClientRect(info.hwnd, &client_area)
            || !GetWindowRect(info.hwnd, &window_rect)
            || !GetMonitorInfoW(monitor, &monitor_info)) return;

        // Store the non-fullscreen configuration before we replace the style.
        info.windowed_placement = placement;
        info.client_area = client_area;
        info.top_left = { window_rect.left, window_rect.top };
        info.fullscreen_bounds = monitor_info.rcMonitor;
        info.fullscreen_area = { 0, 0,
            info.fullscreen_bounds.right - info.fullscreen_bounds.left,
            info.fullscreen_bounds.bottom - info.fullscreen_bounds.top };

        // Switch the window to a borderless popup so it can occupy the monitor.
        if (!set_window_style(info.hwnd, WS_POPUP | WS_VISIBLE)) return;

        // Mark the state as fullscreen and restore the window before sizing it.
        info.flags |= window_info::fullscreen;
        ShowWindow(info.hwnd, SW_RESTORE);

        // Position the popup over the target monitor and size it to the monitor area.
        // If this fails, roll the state back to the previous windowed configuration.
        if (!SetWindowPos(info.hwnd, HWND_TOPMOST,
            info.fullscreen_bounds.left, info.fullscreen_bounds.top,
            info.fullscreen_bounds.right - info.fullscreen_bounds.left,
            info.fullscreen_bounds.bottom - info.fullscreen_bounds.top,
            SWP_FRAMECHANGED | SWP_NOOWNERZORDER | SWP_NOACTIVATE))
        {
            info.flags &= ~window_info::fullscreen;
            set_window_style(info.hwnd, info.style);
            set_window_ex_style(info.hwnd, info.extended_style);
            const HWND insert_after{ (info.extended_style & WS_EX_TOPMOST) ? HWND_TOPMOST : HWND_NOTOPMOST };
            SetWindowPos(info.hwnd, insert_after, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_NOOWNERZORDER);
            SetWindowPlacement(info.hwnd, &info.windowed_placement);
        }
    }
    else
    {
        // Restore the original window style before re-enabling the normal z-order.
        if (!set_window_style(info.hwnd, info.style)) return;
        if (!set_window_ex_style(info.hwnd, info.extended_style))
        {
            set_window_style(info.hwnd, WS_POPUP | WS_VISIBLE);
            return;
        }

        const HWND insert_after{ (info.extended_style & WS_EX_TOPMOST) ? HWND_TOPMOST : HWND_NOTOPMOST };
        if (!SetWindowPos(info.hwnd, insert_after, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_NOOWNERZORDER))
        {
            // Failed to restore the window hierarchy; keep the popup style active.
            set_window_style(info.hwnd, WS_POPUP | WS_VISIBLE);
            return;
        }

        // Reapply the saved non-fullscreen placement and clear the fullscreen flag.
        const RECT fullscreen_area{ info.fullscreen_area };
        info.flags &= ~window_info::fullscreen;
        if (!SetWindowPlacement(info.hwnd, &info.windowed_placement))
        {
            // If placement can't be restored, keep fullscreen state and move back to the monitor.
            info.flags |= window_info::fullscreen;
            info.fullscreen_area = fullscreen_area;
            set_window_style(info.hwnd, WS_POPUP | WS_VISIBLE);
            SetWindowPos(info.hwnd, HWND_TOP,
                info.fullscreen_bounds.left, info.fullscreen_bounds.top,
                info.fullscreen_bounds.right - info.fullscreen_bounds.left,
                info.fullscreen_bounds.bottom - info.fullscreen_bounds.top,
                SWP_FRAMECHANGED | SWP_NOOWNERZORDER | SWP_NOACTIVATE);
        }
    }
}

bool
is_window_fullscreen(window_id id)
{
    return get_from_id(id).is_fullscreen();
}

window_handle
get_window_handle(window_id id)
{
    return get_from_id(id).hwnd;
}

void
set_window_caption(window_id id, const wchar_t* caption)
{
    window_info& info{ get_from_id(id) };
    SetWindowText(info.hwnd, caption);
}

math::u32v4
get_window_size(window_id id)
{
    window_info& info{ get_from_id(id) };
    RECT& area{ info.is_fullscreen() ? info.fullscreen_area : info.client_area };
    return { (u32)area.left, (u32)area.top, (u32)area.right, (u32)area.bottom };
}

bool
is_window_closed(window_id id)
{
    return get_from_id(id).is_closed();
}
} // anonymous namespace

window
create_window(const window_init_info* init_info /* = nullptr */)
{
    window_proc callback{ init_info ? init_info->callback : nullptr };
    window_handle parent{ init_info ? init_info->parent : nullptr };
    if (!register_window_class()) return {};

    window_info info{};
    info.client_area.right = (init_info && init_info->width) ? info.client_area.left + init_info->width : info.client_area.right;
    info.client_area.bottom = (init_info && init_info->height) ? info.client_area.top + init_info->height : info.client_area.bottom;
    info.style |= parent ? WS_CHILD : WS_OVERLAPPEDWINDOW;
    info.top_left = { init_info ? init_info->left : 0, init_info ? init_info->top : 0 };

    RECT rect{ info.client_area };

    UINT dpi{ parent ? GetDpiForWindow(parent) : GetDpiForSystem() };
    if (!dpi) dpi = USER_DEFAULT_SCREEN_DPI;
    if (!AdjustWindowRectExForDpi(&rect, info.style, FALSE, info.extended_style, dpi)) return {};

    const wchar_t* caption{ (init_info && init_info->caption) ? init_info->caption : L"Primal Game" };
    const s32 left{ init_info ? init_info->left : info.top_left.x };
    const s32 top{ init_info ? init_info->top : info.top_left.y };
    const s32 width{ rect.right - rect.left };
    const s32 height{ rect.bottom - rect.top };

    const window_id id{ windows.add(info) };

    // Create an instance of the window class
    HWND hwnd{ CreateWindowExW(
        0,                              // extended style
        window_class_name,              // window class name
        caption,                        // instance title
        info.style,                     // window style
        left, top,                      // initial window position
        width, height,                  // initial window dimensions
        parent,                         // handle to parent window
        NULL,                           // handle to menu
        GetModuleHandleW(nullptr),      // instance of this application
        (void*)(UINT_PTR)(id + 1)) };   // window id for the creation messages

    if (hwnd)
    {
        // Set in the "extra" bytes the pointer to the window callback function
        // which handles messages for the window
        SetLastError(0);
        if (callback)
        {
            if (!SetWindowLongPtr(hwnd, 0, (LONG_PTR)callback) && GetLastError())
            {
                windows.remove(id);
                return {};
            }
        }

        ShowWindow(hwnd, SW_SHOWNORMAL);
        UpdateWindow(hwnd);

        return window{ id };
    }

    windows.remove(id);
    return {};
}

void
remove_window(window_id id)
{
    window_info& info{ get_from_id(id) };
    if (IsWindow(info.hwnd) && !DestroyWindow(info.hwnd)) return;
    windows.remove(id);
}

window
window_from_handle(window_handle handle)
{
    if (!try_get_from_handle(handle)) return {};
    const id::id_type encoded_id{ (id::id_type)GetWindowLongPtr(handle, GWLP_USERDATA) };
    return window{ window_id{ encoded_id > 0 ? encoded_id - 1 : id::invalid_id } };
}

} // namespace primal::platform

#include "IncludeWindowCpp.h"

#endif // _WIND64