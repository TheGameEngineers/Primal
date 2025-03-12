// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "CommonHeaders.h"
#include "Graphics/Renderer.h"

struct shader_file_info
{
    const char*         file_name;
    const char*         function;
    primal::graphics::shader_type::type   type;
};

std::unique_ptr<u8[]> compile_shader(shader_file_info info, u8* code, u32 code_size, primal::utl::vector<std::wstring>& extra_args,
                                     bool include_errors_and_disassembly = false);
std::unique_ptr<u8[]> compile_shader(shader_file_info info, const char* file_path, primal::utl::vector<std::wstring>& extra_args,
                                     bool include_errors_and_disassembly = false);
bool compile_shaders();