// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "CommonHeaders.h"

namespace primal::graphics {

DEFINE_TYPED_ID(camera_id);

class camera
{
public:
    enum type : u32
    {
        perspective,
        orthographic
    };

    constexpr explicit camera(camera_id id) : _id{ id } {}
    constexpr camera() = default;
    constexpr camera_id get_id() const { return _id; }
    constexpr bool is_valid() const { return id::is_valid(_id); }

    void up(math::v3 up) const;
    void field_of_view(f32 fov) const;
    void aspect_ratio(f32 aspect_ratio) const;
    void view_width(f32 width) const;
    void view_height(f32 height) const;
    void range(f32 near_z, f32 far_z) const;

    math::m4x4 view() const;
    math::m4x4 projection() const;
    math::m4x4 inverse_projection() const;
    math::m4x4 view_projection() const;
    math::m4x4 inverse_view_projection() const;
    math::v3 up() const;
    f32 near_z() const;
    f32 far_z() const;
    f32 field_of_view() const;
    f32 aspect_ratio() const;
    f32 view_width() const;
    f32 view_height() const;
    type projection_type() const;
    id::id_type entity_id() const;

private:
    camera_id _id{ id::invalid_id };
};
}