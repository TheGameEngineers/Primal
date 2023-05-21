// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once

#include "D3D12CommonHeaders.h"

namespace primal::graphics::d3d12 {
struct d3d12_frame_info;
}
namespace primal::graphics::d3d12::gpass {

constexpr DXGI_FORMAT           main_buffer_format{ DXGI_FORMAT_R16G16B16A16_FLOAT };
constexpr DXGI_FORMAT           depth_buffer_format{ DXGI_FORMAT_D32_FLOAT };

struct opaque_root_parameter {
    enum parameter : u32 {
        global_shader_data,
        per_object_data,
        position_buffer,
        element_buffer,
        srv_indices,
        directional_lights,
        cullable_lights,
        light_grid,
        light_index_list,

        count
    };
};

bool initialize();
void shutdown();

[[nodiscard]] const d3d12_render_texture& main_buffer();
[[nodiscard]] const d3d12_depth_buffer& depth_buffer();

// NOTE: call this every frame befor rendering anything in gpass.
void set_size(math::u32v2 size);
void depth_prepass(id3d12_graphics_command_list* cmd_list, const d3d12_frame_info& d3d12_info);
void render(id3d12_graphics_command_list* cmd_list, const d3d12_frame_info& d3d12_info);

void add_transitions_for_depth_prepass(d3dx::d3d12_resource_barrier& barriers);
void add_transitions_for_gpass(d3dx::d3d12_resource_barrier& barriers);
void add_transitions_for_post_process(d3dx::d3d12_resource_barrier& barriers);

void set_render_targets_for_depth_prepass(id3d12_graphics_command_list* cmd_list);
void set_render_targets_for_gpass(id3d12_graphics_command_list* cmd_list);
}