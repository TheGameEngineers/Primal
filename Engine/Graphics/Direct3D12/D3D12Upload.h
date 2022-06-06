// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "D3D12CommonHeaders.h"

namespace primal::graphics::d3d12::upload {

class d3d12_upload_context {
public:
    d3d12_upload_context(u32 aligned_size);
    DISABLE_COPY_AND_MOVE(d3d12_upload_context);
    ~d3d12_upload_context() { assert(_frame_index == u32_invalid_id); }

    void end_upload();

    [[nodiscard]] constexpr id3d12_graphics_command_list *const command_list() const { return _cmd_list; }
    [[nodiscard]] constexpr ID3D12Resource *const upload_buffer() const { return _upload_buffer; }
    [[nodiscard]] constexpr void *const cpu_address() const { return _cpu_address; }

private:
    DEBUG_OP(d3d12_upload_context() = default);
    id3d12_graphics_command_list*   _cmd_list{ nullptr };
    ID3D12Resource*                 _upload_buffer{ nullptr };
    void*                           _cpu_address{ nullptr };
    u32                             _frame_index{ u32_invalid_id };
};

bool initialize();
void shutdown();
}