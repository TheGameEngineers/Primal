// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "D3D12CommonHeaders.h"

namespace primal::graphics::d3d12 {
struct d3d12_frame_info;
}

namespace primal::graphics::d3d12::delight {

constexpr u32 light_culling_tile_size{ 32 };

bool initialize();
void shutdown();

[[nodiscard]] id::id_type add_culler();
void remove_culler(id::id_type id);

void cull_lights(id3d12_graphics_command_list *const cmd_list,
                 const d3d12_frame_info& d3d12_info,
                 d3dx::d3d12_resource_barrier& barriers);

// TODO: temporary for visualizing light culling. Remove later.
D3D12_GPU_VIRTUAL_ADDRESS frustums(id::id_type light_culling_id, u32 frame_index);
D3D12_GPU_VIRTUAL_ADDRESS light_grid_opaque(id::id_type light_culling_id, u32 frame_index);
D3D12_GPU_VIRTUAL_ADDRESS light_index_list_opaque(id::id_type light_culling_id, u32 frame_index);
}