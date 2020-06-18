// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once

#include "..\Components\ComponentsCommon.h"
#include "TransfromComponent.h"

namespace primal::game_entity {

DEFINE_TYPED_ID(entity_id);

class entity {
public:
    constexpr explicit entity(entity_id id) : _id{ id } {}
    constexpr entity() : _id{ id::invalid_id } {}
    constexpr entity_id get_id() const { return _id; }
    constexpr bool is_valid() const { return id::is_valid(_id); }

    transform::component transform() const;
private:
    entity_id _id;
};
}