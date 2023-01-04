// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "D3D12CommonHeaders.h"

namespace primal::graphics::d3d12::light {

graphics::light create(light_init_info info);
void remove(light_id id, u64 light_set_key);
void set_parameter(light_id id, u64 light_set_key, light_parameter::parameter parameter, const void *const data, u32 data_size);
void get_parameter(light_id id, u64 light_set_key, light_parameter::parameter parameter, void *const data, u32 data_size);
}