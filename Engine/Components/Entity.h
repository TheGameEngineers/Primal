// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "ComponentsCommon.h"

namespace primal {

struct component_type
{
    enum type : u32 {
        transform,
        script,
        geometry,

        count
    };
};

#define INIT_INFO(component) namespace component { struct init_info; }

INIT_INFO(transform);
INIT_INFO(script);
INIT_INFO(geometry);

#undef INIT_INFO

namespace game_entity {
struct entity_info
{
    transform::init_info* transform{ nullptr };
    script::init_info* script{ nullptr };
    geometry::init_info* geometry{ nullptr };
};

entity create(entity_info info);
void remove(entity_id id);
bool update_component(entity_id id, entity_info info, component_type::type type);
bool is_alive(entity_id id);
}
}