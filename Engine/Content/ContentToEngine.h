// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "CommonHeaders.h"

namespace primal::content {

struct primitve_topology{
    enum type: u32 {
        point_list = 1,
        line_list,
        line_strip,
        triangle_list,
        triangle_strip,

        count
    };
};

}