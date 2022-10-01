// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "CommonHeaders.h"

namespace primal::content {

struct asset_type {
    enum type : u32 {
        unknown = 0,
        animation,
        audio,
        material,
        mesh,
        skeleton,
        texture,

        count
    };
};

typedef struct compiled_shader
{
    static constexpr u32 hash_length{ 16 };
    constexpr u64 byte_code_size() const { return _byte_code_size; }
    constexpr const u8 *const hash() const { return &_hash[0]; }
    constexpr const u8 *const byte_code() const { return &_byte_code; }
private:
    u64         _byte_code_size;
    u8          _hash[hash_length];
    u8          _byte_code;
} const *compiled_shader_ptr;

struct lod_offset
{
    u16 offset;
    u16 count;
};

id::id_type create_resource(const void *const data, asset_type::type type);
void destroy_resource(id::id_type id, asset_type::type type);

id::id_type add_shader(const u8* data);
void remove_shader(id::id_type id);
compiled_shader_ptr get_shader(id::id_type id);

void get_submesh_gpu_ids(id::id_type geometry_content_id, u32 id_count, id::id_type *const gpu_ids);
void get_lod_offsets(const id::id_type *const geometry_ids, const f32 *const thresholds, u32 id_count, utl::vector<lod_offset>& offsets);
}