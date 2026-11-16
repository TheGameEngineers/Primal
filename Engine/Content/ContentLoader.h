// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "CommonHeaders.h"
#if !defined(SHIPPING) && defined(_WIN64)
namespace primal::content {
bool load_game();
void unload_game();

u64 get_current_light_set_key();

id::id_type get_camera();
bool load_engine_shaders(std::unique_ptr<u8[]>& shaders, u64& size);
}
#endif // !defined(SHIPPING)