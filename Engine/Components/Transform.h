// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "ComponentsCommon.h"

namespace primal::transform {

DEFINE_TYPED_ID(transform_id);

struct init_info
{
    f32 positon[3]{};
    f32 rotation[4]{};
    f32 scale[3]{1.f, 1.f, 1.f};
};

transform_id create_transform(const init_info& info, game_entity::entity_id entity_id);
void remove_transform(transform_id id);
}