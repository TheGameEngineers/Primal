// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "../Components/ComponentsCommon.h"

namespace primal::transform {

DEFINE_TYPED_ID(transform_id);

class component final
{
public:
    constexpr explicit component(transform_id id) : _id{ id } {}
    constexpr component() : _id{ id::invalid_id } {}
    constexpr transform_id get_id() const { return _id; }
    constexpr bool is_valid() const { return id::is_valid(_id); }

    math::v4 rotation() const;
    math::v3 orientation() const;
    math::v3 position() const;
    math::v3 scale() const;
private:
    transform_id _id;
};

}