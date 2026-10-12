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
        orthographic,

        count
    };

    constexpr explicit camera(camera_id id) : _id{ id } {}
    constexpr camera() = default;
    [[nodiscard]] constexpr camera_id get_id() const { return _id; }
    [[nodiscard]] constexpr bool is_valid() const { return id::is_valid(_id); }

    void up(math::v3 up) const;
    void field_of_view(f32 fov) const;
    void aspect_ratio(f32 aspect_ratio) const;
    void orthographic_size(f32 size) const;
    void range(f32 near_z, f32 far_z) const;

    [[nodiscard]] math::m4x4 view() const;
    [[nodiscard]] math::m4x4 projection() const;
    [[nodiscard]] math::m4x4 inverse_projection() const;
    [[nodiscard]] math::m4x4 view_projection() const;
    [[nodiscard]] math::m4x4 inverse_view_projection() const;
    [[nodiscard]] math::v3 up() const;
    [[nodiscard]] f32 near_z() const;
    [[nodiscard]] f32 far_z() const;
    [[nodiscard]] f32 field_of_view() const;
    [[nodiscard]] f32 aspect_ratio() const;
    [[nodiscard]] f32 orthographic_size() const;
    [[nodiscard]] type projection_type() const;
    [[nodiscard]] id::id_type entity_id() const;

private:
    camera_id _id{ id::invalid_id };
};
}