// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "D3D12CommonHeaders.h"

namespace primal::graphics::d3d12::content {
namespace submesh {

id::id_type add(const u8*& data);
void remove(id::id_type id);

} // namespace submesh
}