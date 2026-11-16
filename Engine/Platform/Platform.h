// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "CommonHeaders.h"
#include "Window.h"
#include "PlatformTypes.h"

namespace primal::platform {

struct window_init_info;

window create_window(const window_init_info* const init_info = nullptr);
void remove_window(window_id id);
window window_from_handle(window_handle handle);
}