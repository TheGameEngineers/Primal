// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#ifdef _WIN64
#include "CommonHeaders.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif // !WIN32_LEAN_AND_MEAN
// Skip definition of min/max macros in windows.h
#ifndef NOMINMAX
#define NOMINMAX
#endif // !NOMINMAX

#include <Windows.h>

namespace primal::input {

void update_mouse_behavior(HWND hwnd, bool is_focused);

#ifdef USE_WITH_EDITOR
void process_raw_input(u8* data, u32 size);
#endif // USE_WITH_EDITOR
}

#endif // !_WIN64