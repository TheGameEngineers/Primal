// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#if INCLUDE_WINDOW_CPP
#include "Window.h"

namespace primal::platform {

void
window::set_fullscreen(bool is_fullscreen) const
{
    assert(is_valid());
    set_window_fullscreen(_id, is_fullscreen);
}

bool
window::is_fullscreen() const
{
    assert(is_valid());
    return is_window_fullscreen(_id);
}

void*
window::handle() const
{
    assert(is_valid());
    return get_window_handle(_id);
}

void
window::set_caption(const wchar_t* caption) const
{
    assert(is_valid());
    set_window_caption(_id, caption);
}

math::u32v4
window::size() const
{
    assert(is_valid());
    return get_window_size(_id);
}

void
window::resize(u32 width, u32 height) const
{
    assert(is_valid());
    resize_window(_id, width, height);
}

u32
window::width() const
{
    math::u32v4 s{ size() };
    return s.z - s.x;
}

u32
window::height() const
{
    math::u32v4 s{ size() };
    return s.w - s.y;
}

bool
window::is_closed() const
{
    assert(is_valid());
    return is_window_closed(_id);
}
}
#endif // INCLUDE_WINDOW_CPP