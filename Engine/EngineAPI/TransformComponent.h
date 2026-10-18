// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "CommonHeaders.h"

namespace primal::transform {

struct space {
    enum frame : u32 {
        absolute,
        local,
        world,
    };
};

DEFINE_TYPED_ID(transform_id);

class component final
{
public:
    constexpr explicit component(transform_id id) : _id{ id } {}
    constexpr component() : _id{ id::invalid_id } {}
    [[nodiscard]] constexpr transform_id get_id() const { return _id; }
    [[nodiscard]] constexpr bool is_valid() const { return id::is_valid(_id); }

    // TODO: we might want to add a parameter to rotation and position
    //       to get them with respect to the local frame or world space.
    [[nodiscard]] math::v4 rotation() const;
    [[nodiscard]] math::v3 position() const;
    [[nodiscard]] math::v3 scale() const;
    [[nodiscard]] math::v3 right() const;
    [[nodiscard]] math::v3 up() const;
    [[nodiscard]] math::v3 front() const;
    [[nodiscard]] math::m3x3 local_frame() const;

    void rotation(math::v4 rotation_quaternion) const;
    void position(math::v3 position) const;
    void scale(math::v3 scale) const;

    [[nodiscard]] DirectX::XMVECTOR calculate_local_position(math::v3 delta) const;
    [[nodiscard]] DirectX::XMVECTOR calculate_absolute_rotation(math::v3 rotation) const;
    [[nodiscard]] DirectX::XMVECTOR calculate_local_rotation(math::v3 delta) const;
    [[nodiscard]] DirectX::XMVECTOR calculate_world_rotation(math::v3 delta) const;

private:
    transform_id _id;
};

}