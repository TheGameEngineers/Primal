// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "ComponentsCommon.h"

namespace primal {

#define INIT_INFO(component) namespace component { struct init_info; }

INIT_INFO(transform);

#undef INIT_INFO

namespace game_entity {
struct entity_info
{
    transform::init_info* transform{ nullptr };
};

entity create_game_entity(const entity_info& info);
void remove_game_entity(entity e);
bool is_alive(entity e);
}
}