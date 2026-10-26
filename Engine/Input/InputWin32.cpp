// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#ifdef _WIN64

#include "InputWin32.h"
#include "Input.h"

#include <hidusage.h>
#ifndef QWORD
typedef u64 QWORD;
#endif

namespace primal::input {
namespace {

constexpr u32 vk_mapping[256]{
    /* 0x00 */ u32_invalid_id,
    /* 0x01 */ input_code::mouse_left,
    /* 0x02 */ input_code::mouse_right,
    /* 0x03 */ u32_invalid_id,
    /* 0x04 */ input_code::mouse_middle,
    /* 0x05 */ input_code::mouse_4,
    /* 0x06 */ input_code::mouse_5,
    /* 0x07 */ u32_invalid_id,
    /* 0x08 */ input_code::key_backspace,
    /* 0x09 */ input_code::key_tab,
    /* 0x0A */ u32_invalid_id,
    /* 0x0B */ u32_invalid_id,
    /* 0x0C */ u32_invalid_id,
    /* 0x0D */ input_code::key_return,
    /* 0x0E */ u32_invalid_id,
    /* 0x0F */ u32_invalid_id,

    /* 0x10 */ input_code::key_shift,
    /* 0x11 */ input_code::key_control,
    /* 0x12 */ input_code::key_alt,
    /* 0x13 */ input_code::key_pause,
    /* 0x14 */ input_code::key_capslock,
    /* 0x15 */ u32_invalid_id,
    /* 0x16 */ u32_invalid_id,
    /* 0x17 */ u32_invalid_id,
    /* 0x18 */ u32_invalid_id,
    /* 0x19 */ u32_invalid_id,
    /* 0x1A */ u32_invalid_id,
    /* 0x1B */ input_code::key_escape,
    /* 0x1C */ u32_invalid_id,
    /* 0x1D */ u32_invalid_id,
    /* 0x1E */ u32_invalid_id,
    /* 0x1F */ u32_invalid_id,

    /* 0x20 */ input_code::key_space,
    /* 0x21 */ input_code::key_page_up,
    /* 0x22 */ input_code::key_page_down,
    /* 0x23 */ input_code::key_end,
    /* 0x24 */ input_code::key_home,
    /* 0x25 */ input_code::key_left,
    /* 0x26 */ input_code::key_up,
    /* 0x27 */ input_code::key_right,
    /* 0x28 */ input_code::key_down,
    /* 0x29 */ u32_invalid_id,
    /* 0x2A */ u32_invalid_id,
    /* 0x2B */ u32_invalid_id,
    /* 0x2C */ input_code::key_print_screen,
    /* 0x2D */ input_code::key_insert,
    /* 0x2E */ input_code::key_delete,
    /* 0x2F */ u32_invalid_id,

    /* 0x30 */ input_code::key_0,
    /* 0x31 */ input_code::key_1,
    /* 0x32 */ input_code::key_2,
    /* 0x33 */ input_code::key_3,
    /* 0x34 */ input_code::key_4,
    /* 0x35 */ input_code::key_5,
    /* 0x36 */ input_code::key_6,
    /* 0x37 */ input_code::key_7,
    /* 0x38 */ input_code::key_8,
    /* 0x39 */ input_code::key_9,
    /* 0x3A */ u32_invalid_id,
    /* 0x3B */ u32_invalid_id,
    /* 0x3C */ u32_invalid_id,
    /* 0x3D */ u32_invalid_id,
    /* 0x3E */ u32_invalid_id,
    /* 0x3F */ u32_invalid_id,

    /* 0x40 */ u32_invalid_id,
    /* 0x41 */ input_code::key_a,
    /* 0x42 */ input_code::key_b,
    /* 0x43 */ input_code::key_c,
    /* 0x44 */ input_code::key_d,
    /* 0x45 */ input_code::key_e,
    /* 0x46 */ input_code::key_f,
    /* 0x47 */ input_code::key_g,
    /* 0x48 */ input_code::key_h,
    /* 0x49 */ input_code::key_i,
    /* 0x4A */ input_code::key_j,
    /* 0x4B */ input_code::key_k,
    /* 0x4C */ input_code::key_l,
    /* 0x4D */ input_code::key_m,
    /* 0x4E */ input_code::key_n,
    /* 0x4F */ input_code::key_o,

    /* 0x50 */ input_code::key_p,
    /* 0x51 */ input_code::key_q,
    /* 0x52 */ input_code::key_r,
    /* 0x53 */ input_code::key_s,
    /* 0x54 */ input_code::key_t,
    /* 0x55 */ input_code::key_u,
    /* 0x56 */ input_code::key_v,
    /* 0x57 */ input_code::key_w,
    /* 0x58 */ input_code::key_x,
    /* 0x59 */ input_code::key_y,
    /* 0x5A */ input_code::key_z,
    /* 0x5B */ u32_invalid_id,
    /* 0x5C */ u32_invalid_id,
    /* 0x5D */ u32_invalid_id,
    /* 0x5E */ u32_invalid_id,
    /* 0x5F */ u32_invalid_id,

    /* 0x60 */ input_code::key_numpad_0,
    /* 0x61 */ input_code::key_numpad_1,
    /* 0x62 */ input_code::key_numpad_2,
    /* 0x63 */ input_code::key_numpad_3,
    /* 0x64 */ input_code::key_numpad_4,
    /* 0x65 */ input_code::key_numpad_5,
    /* 0x66 */ input_code::key_numpad_6,
    /* 0x67 */ input_code::key_numpad_7,
    /* 0x68 */ input_code::key_numpad_8,
    /* 0x69 */ input_code::key_numpad_9,
    /* 0x6A */ input_code::key_multiply,
    /* 0x6B */ input_code::key_add,
    /* 0x6C */ u32_invalid_id,
    /* 0x6D */ input_code::key_subtract,
    /* 0x6E */ input_code::key_decimal,
    /* 0x6F */ input_code::key_divide,

    /* 0x70 */ input_code::key_f1,
    /* 0x71 */ input_code::key_f2,
    /* 0x72 */ input_code::key_f3,
    /* 0x73 */ input_code::key_f4,
    /* 0x74 */ input_code::key_f5,
    /* 0x75 */ input_code::key_f6,
    /* 0x76 */ input_code::key_f7,
    /* 0x77 */ input_code::key_f8,
    /* 0x78 */ input_code::key_f9,
    /* 0x79 */ input_code::key_f10,
    /* 0x7A */ input_code::key_f11,
    /* 0x7B */ input_code::key_f12,
    /* 0x7C */ u32_invalid_id,
    /* 0x7D */ u32_invalid_id,
    /* 0x7E */ u32_invalid_id,
    /* 0x7F */ u32_invalid_id,

    /* 0x80 */ u32_invalid_id,
    /* 0x81 */ u32_invalid_id,
    /* 0x82 */ u32_invalid_id,
    /* 0x83 */ u32_invalid_id,
    /* 0x84 */ u32_invalid_id,
    /* 0x85 */ u32_invalid_id,
    /* 0x86 */ u32_invalid_id,
    /* 0x87 */ u32_invalid_id,
    /* 0x88 */ u32_invalid_id,
    /* 0x89 */ u32_invalid_id,
    /* 0x8A */ u32_invalid_id,
    /* 0x8B */ u32_invalid_id,
    /* 0x8C */ u32_invalid_id,
    /* 0x8D */ u32_invalid_id,
    /* 0x8E */ u32_invalid_id,
    /* 0x8F */ u32_invalid_id,

    /* 0x90 */ input_code::key_numlock,
    /* 0x91 */ input_code::key_scrollock,
    /* 0x92 */ u32_invalid_id,
    /* 0x93 */ u32_invalid_id,
    /* 0x94 */ u32_invalid_id,
    /* 0x95 */ u32_invalid_id,
    /* 0x96 */ u32_invalid_id,
    /* 0x97 */ u32_invalid_id,
    /* 0x98 */ u32_invalid_id,
    /* 0x99 */ u32_invalid_id,
    /* 0x9A */ u32_invalid_id,
    /* 0x9B */ u32_invalid_id,
    /* 0x9C */ u32_invalid_id,
    /* 0x9D */ u32_invalid_id,
    /* 0x9E */ u32_invalid_id,
    /* 0x9F */ u32_invalid_id,

    /* 0xA0 */ u32_invalid_id,
    /* 0xA1 */ u32_invalid_id,
    /* 0xA2 */ u32_invalid_id,
    /* 0xA3 */ u32_invalid_id,
    /* 0xA4 */ u32_invalid_id,
    /* 0xA5 */ u32_invalid_id,
    /* 0xA6 */ u32_invalid_id,
    /* 0xA7 */ u32_invalid_id,
    /* 0xA8 */ u32_invalid_id,
    /* 0xA9 */ u32_invalid_id,
    /* 0xAA */ u32_invalid_id,
    /* 0xAB */ u32_invalid_id,
    /* 0xAC */ u32_invalid_id,
    /* 0xAD */ u32_invalid_id,
    /* 0xAE */ u32_invalid_id,
    /* 0xAF */ u32_invalid_id,

    /* 0xB0 */ u32_invalid_id,
    /* 0xB1 */ u32_invalid_id,
    /* 0xB2 */ u32_invalid_id,
    /* 0xB3 */ u32_invalid_id,
    /* 0xB4 */ u32_invalid_id,
    /* 0xB5 */ u32_invalid_id,
    /* 0xB6 */ u32_invalid_id,
    /* 0xB7 */ u32_invalid_id,
    /* 0xB8 */ u32_invalid_id,
    /* 0xB9 */ u32_invalid_id,
    /* 0xBA */ input_code::key_colon,
    /* 0xBB */ input_code::key_plus,
    /* 0xBC */ input_code::key_comma,
    /* 0xBD */ input_code::key_minus,
    /* 0xBE */ input_code::key_period,
    /* 0xBF */ input_code::key_question,

    /* 0xC0 */ input_code::key_tilde,
    /* 0xC1 */ u32_invalid_id,
    /* 0xC2 */ u32_invalid_id,
    /* 0xC3 */ u32_invalid_id,
    /* 0xC4 */ u32_invalid_id,
    /* 0xC5 */ u32_invalid_id,
    /* 0xC6 */ u32_invalid_id,
    /* 0xC7 */ u32_invalid_id,
    /* 0xC8 */ u32_invalid_id,
    /* 0xC9 */ u32_invalid_id,
    /* 0xCA */ u32_invalid_id,
    /* 0xCB */ u32_invalid_id,
    /* 0xCC */ u32_invalid_id,
    /* 0xCD */ u32_invalid_id,
    /* 0xCE */ u32_invalid_id,
    /* 0xCF */ u32_invalid_id,

    /* 0xD0 */ u32_invalid_id,
    /* 0xD1 */ u32_invalid_id,
    /* 0xD2 */ u32_invalid_id,
    /* 0xD3 */ u32_invalid_id,
    /* 0xD4 */ u32_invalid_id,
    /* 0xD5 */ u32_invalid_id,
    /* 0xD6 */ u32_invalid_id,
    /* 0xD7 */ u32_invalid_id,
    /* 0xD8 */ u32_invalid_id,
    /* 0xD9 */ u32_invalid_id,
    /* 0xDA */ u32_invalid_id,
    /* 0xDB */ input_code::key_bracket_open,
    /* 0xDC */ input_code::key_pipe,
    /* 0xDD */ input_code::key_bracket_close,
    /* 0xDE */ input_code::key_quote,
    /* 0xDF */ u32_invalid_id,

    /* 0xE0 */ u32_invalid_id,
    /* 0xE1 */ u32_invalid_id,
    /* 0xE2 */ u32_invalid_id,
    /* 0xE3 */ u32_invalid_id,
    /* 0xE4 */ u32_invalid_id,
    /* 0xE5 */ u32_invalid_id,
    /* 0xE6 */ u32_invalid_id,
    /* 0xE7 */ u32_invalid_id,
    /* 0xE8 */ u32_invalid_id,
    /* 0xE9 */ u32_invalid_id,
    /* 0xEA */ u32_invalid_id,
    /* 0xEB */ u32_invalid_id,
    /* 0xEC */ u32_invalid_id,
    /* 0xED */ u32_invalid_id,
    /* 0xEE */ u32_invalid_id,
    /* 0xEF */ u32_invalid_id,

    /* 0xF0 */ u32_invalid_id,
    /* 0xF1 */ u32_invalid_id,
    /* 0xF2 */ u32_invalid_id,
    /* 0xF3 */ u32_invalid_id,
    /* 0xF4 */ u32_invalid_id,
    /* 0xF5 */ u32_invalid_id,
    /* 0xF6 */ u32_invalid_id,
    /* 0xF7 */ u32_invalid_id,
    /* 0xF8 */ u32_invalid_id,
    /* 0xF9 */ u32_invalid_id,
    /* 0xFA */ u32_invalid_id,
    /* 0xFB */ u32_invalid_id,
    /* 0xFC */ u32_invalid_id,
    /* 0xFD */ u32_invalid_id,
    /* 0xFE */ u32_invalid_id,
    /* 0xFF */ u32_invalid_id,
};

struct modifier_flags {
    enum flags : u8 {
        left_shift = 0x10,
        left_control = 0x20,
        left_alt = 0x40,

        right_shift = 0x01,
        right_control = 0x02,
        right_alt = 0x04,
    };
};

u8                      modifier_keys_state{ 0 };
utl::vector<u8>         raw_input_buffer(4096);
HWND                    window_handle{ nullptr };
mouse_behavior::type    mouse_behavior_type{};
bool                    mouse_current_visibility{ true };
bool                    mouse_is_visible{ true };
bool                    window_has_focus{ false };

void
set_mouse_visibility(bool visible)
{
    if (mouse_is_visible != visible)
    {
        ShowCursor(visible ? TRUE : FALSE);
        mouse_is_visible = visible;
    }
}

void
clip_cursor_to_window()
{
    if (!window_handle || IsIconic(window_handle))
    {
        ClipCursor(nullptr);
        return;
    }

    RECT rect;
    if (!GetClientRect(window_handle, &rect)) return;

    POINT top_left{ rect.left, rect.top };
    POINT bottom_right{ rect.right, rect.bottom };
    if (!ClientToScreen(window_handle, &top_left) || !ClientToScreen(window_handle, &bottom_right)) return;

    rect = { top_left.x, top_left.y, bottom_right.x, bottom_right.y };
    ClipCursor(&rect);
}

void
apply_mouse_behavior()
{
    if (!window_has_focus)
    {
        ClipCursor(nullptr);
        set_mouse_visibility(true);
        return;
    }

    show_mouse(mouse_current_visibility);

    switch (mouse_behavior_type)
    {
    case mouse_behavior::free:
    case mouse_behavior::center:
        ClipCursor(nullptr);
        break;
    case mouse_behavior::clip:
        clip_cursor_to_window();
        break;
    }
}

void
set_modifier_input(u8 virtual_key, input_code::code code, modifier_flags::flags flags)
{
    if (GetKeyState(virtual_key) < 0)
    {
        set(input_source::keyboard, code, { 1.f, 0.f, 0.f });
        modifier_keys_state |= flags;
    }
    else if (modifier_keys_state & flags)
    {
        set(input_source::keyboard, code, { 0.f, 0.f, 0.f });
        modifier_keys_state &= ~flags;
    }
}

void
set_modifier_inputs(input_code::code code)
{
    if (code == input_code::key_shift)
    {
        set_modifier_input(VK_LSHIFT, input_code::key_left_shift, modifier_flags::left_shift);
        set_modifier_input(VK_RSHIFT, input_code::key_right_shift, modifier_flags::right_shift);
    }
    else if (code == input_code::key_control)
    {
        set_modifier_input(VK_LCONTROL, input_code::key_left_control, modifier_flags::left_control);
        set_modifier_input(VK_RCONTROL, input_code::key_right_control, modifier_flags::right_control);
    }
    else if (code == input_code::key_alt)
    {
        set_modifier_input(VK_LMENU, input_code::key_left_alt, modifier_flags::left_alt);
        set_modifier_input(VK_RMENU, input_code::key_right_alt, modifier_flags::right_alt);
    }
}

constexpr math::v2
get_mouse_position(LPARAM lparam)
{
    return { (f32)((s16)(lparam & 0x0000ffff)), (f32)((s16)(lparam >> 16)) };
}

void
process_input_buffer(u32 count)
{
    assert(count && count != u32_invalid_id);
    RAWINPUT* ri{ (RAWINPUT*)raw_input_buffer.data() };

    for (u32 i{ 0 }; i < count; ++i, ri = NEXTRAWINPUTBLOCK(ri))
    {
        const RAWINPUT& input{ *ri };

        if (input.header.dwType == RIM_TYPEMOUSE)
        {
            const RAWMOUSE& mouse{ input.data.mouse };

            // Mouse moved
            if (!mouse.usFlags && (mouse.lLastX || mouse.lLastY))
            {
                math::v2 pos{ (f32)mouse.lLastX, (f32)mouse.lLastY };
                set(input_source::mouse, input_code::mouse_position_x, { pos.x, 0.f, 0.f });
                set(input_source::mouse, input_code::mouse_position_y, { pos.y, 0.f, 0.f });
                set(input_source::mouse, input_code::mouse_position, { pos.x, pos.y, 0.f });
            }

            // Mouse buttons state changed
            if (mouse.usButtonFlags)
            {
                const math::u32v2 pos{ mouse_position() };
                const math::v3 pos_down{ (f32)pos.x, (f32)pos.y, 1.f };
                const math::v3 pos_up{ (f32)pos.x, (f32)pos.y, 0.f };

                if (mouse.usButtonFlags & RI_MOUSE_LEFT_BUTTON_DOWN) set(input_source::mouse, input_code::mouse_left, pos_down);
                if (mouse.usButtonFlags & RI_MOUSE_LEFT_BUTTON_UP) set(input_source::mouse, input_code::mouse_left, pos_up);
                if (mouse.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_DOWN) set(input_source::mouse, input_code::mouse_right, pos_down);
                if (mouse.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_UP) set(input_source::mouse, input_code::mouse_right, pos_up);
                if (mouse.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_DOWN) set(input_source::mouse, input_code::mouse_middle, pos_down);
                if (mouse.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_UP) set(input_source::mouse, input_code::mouse_middle, pos_up);
                if (mouse.usButtonFlags & RI_MOUSE_BUTTON_4_DOWN) set(input_source::mouse, input_code::mouse_4, pos_down);
                if (mouse.usButtonFlags & RI_MOUSE_BUTTON_4_UP) set(input_source::mouse, input_code::mouse_4, pos_up);
                if (mouse.usButtonFlags & RI_MOUSE_BUTTON_5_DOWN) set(input_source::mouse, input_code::mouse_5, pos_down);
                if (mouse.usButtonFlags & RI_MOUSE_BUTTON_5_UP) set(input_source::mouse, input_code::mouse_5, pos_up);
                if (mouse.usButtonFlags & RI_MOUSE_WHEEL) set(input_source::mouse, input_code::mouse_wheel, { (f32)((u16)mouse.usButtonData), 0.f, 0.f });
            }
        }
        else if (input.header.dwType == RIM_TYPEKEYBOARD)
        {
            const RAWKEYBOARD& keyboard{ input.data.keyboard };
            const f32 state{ keyboard.Flags & RI_KEY_BREAK ? 0.f : 1.f }; // 1.f means pressed
            const input_code::code code{ vk_mapping[keyboard.VKey & 0xff] };
            if (code != u32_invalid_id)
            {
                set(input_source::keyboard, code, { state, 0.f, 0.f });
                set_modifier_inputs(code);
            }
        }
        else if (input.header.dwType == RIM_TYPEHID)
        {
        }
    }
}

} // anonymous namespace

bool
initialize()
{
    RAWINPUTDEVICE rid[2]{};

    // Keyboard
    rid[0].usUsagePage = HID_USAGE_PAGE_GENERIC;
    rid[0].usUsage = HID_USAGE_GENERIC_KEYBOARD;
    //rid[0].dwFlags = RIDEV_NOLEGACY;

    // Mouse
    rid[1].usUsagePage = HID_USAGE_PAGE_GENERIC;
    rid[1].usUsage = HID_USAGE_GENERIC_MOUSE;
    //rid[1].dwFlags = RIDEV_NOLEGACY;

    return RegisterRawInputDevices(&rid[0], _countof(rid), sizeof(RAWINPUTDEVICE));
}

void
shutdown()
{
    ClipCursor(nullptr);
}

void
process()
{
    for (;;)
    {
        u32 buffer_size{ (u32)raw_input_buffer.size() };
        const u32 count{ GetRawInputBuffer((RAWINPUT*)raw_input_buffer.data(), &buffer_size, sizeof(RAWINPUTHEADER)) };

        if (!count) break;

        // Buffer too small. Double buffer size until it fits or exceeded a threshold.
        if (count == u32_invalid_id)
        {
            raw_input_buffer.resize(raw_input_buffer.size() * 2);
            buffer_size = (u32)raw_input_buffer.size();
            // Arbitrary buffer size fail threshold. Just give up when there's this much stale input data.
            if (buffer_size > 1000'000) break;

            continue;
        }

        process_input_buffer(count);
    }

    if (window_handle && window_has_focus && mouse_behavior_type == mouse_behavior::center)
    {
        RECT rect;
        if (GetClientRect(window_handle, &rect))
        {
            POINT center{ rect.left + ((rect.right - rect.left) >> 1), rect.top + ((rect.bottom - rect.top) >> 1) };
            if (ClientToScreen(window_handle, &center)) SetCursorPos(center.x, center.y);
        }
    }
}

#ifdef USE_WITH_EDITOR
// NOTE: We can't set a WM_INPUT filter for the editor like we did for the C++ message loop.
//       Therefore, when the game is running in the editor, we need to handle WM_INPUT
//       messages instead of reading the raw input buffer periodically.
void process_raw_input(u8* data, u32 size)
{
    assert(data && size && size != u32_invalid_id && raw_input_buffer.size() >= size);
    memcpy(raw_input_buffer.data(), data, size);
    process_input_buffer(1);
}
#endif // USE_WITH_EDITOR

math::u32v2
mouse_position()
{
    POINT pos{};
    return window_handle && GetCursorPos(&pos) && ScreenToClient(window_handle, &pos)
        ? math::u32v2{ (u32)pos.x, (u32)pos.y } : math::u32v2{};
}

void
show_mouse(bool show)
{
    mouse_current_visibility = show;
    set_mouse_visibility(show);
}

void
mouse_behavior(mouse_behavior::type type)
{
    mouse_behavior_type = type;
    apply_mouse_behavior();
}

void
update_mouse_behavior(HWND hwnd, bool is_focused)
{
    window_handle = hwnd;
    window_has_focus = is_focused;
    apply_mouse_behavior();
}
}

#endif // !_WIN64