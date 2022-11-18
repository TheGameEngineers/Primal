// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "ComponentsCommon.h"

namespace primal::transform {

struct init_info
{
    f32 position[3]{};
    f32 rotation[4]{};
    f32 scale[3]{1.f, 1.f, 1.f};
};

struct component_flags {
    enum flags : u32 {
        rotation = 0x01,
        orientation = 0x02,
        position = 0x04,
        scale = 0x08,

        all = rotation | orientation | position | scale
    };
};

struct component_cache
{
    math::v4        rotation;
    math::v3        orientation;
    math::v3        position;
    math::v3        scale;
    transform_id    id;
    u32             flags;
};

component create(init_info info, game_entity::entity entity);
void remove(component c);
void get_transform_matrices(const game_entity::entity_id id, math::m4x4& world, math::m4x4& inverse_world);
void get_updated_components_flags(const game_entity::entity_id *const ids, u32 count, u8 *const flags);
void update(const component_cache *const cache, u32 count);
}