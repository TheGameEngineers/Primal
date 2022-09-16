// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "D3D12CommonHeaders.h"

namespace primal::graphics::d3d12::content {

bool initialize();
void shutdown();

namespace submesh {

id::id_type add(const u8*& data);
void remove(id::id_type id);

} // namespace submesh

namespace texture {
id::id_type add(const u8* const);
void remove(id::id_type);
void get_descriptor_indices(const id::id_type *const texture_ids, u32 id_count, u32 *const indices);
} // namespace texture

namespace material {

id::id_type add(material_init_info info);
void remove(id::id_type id);

} // namespace material
}