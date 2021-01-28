// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "ToolsCommon.h"

namespace primal::tools {
enum primitive_mesh_type : u32
{
    plane,
    cube,
    uv_sphere,
    ico_sphere,
    cylinder,
    capsule,

    count
};

struct primitive_init_info
{
    primitive_mesh_type type;
    u32                 segments[3]{ 1, 1, 1 };
    math::v3            size{ 1, 1, 1 };
    u32                 lod{ 0 };
};

}