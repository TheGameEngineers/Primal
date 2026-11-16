// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "D3D12CommonHeaders.h"

namespace primal::graphics::d3d12::camera {

// NOTE: We're mapping to XMM registers which need a 16-byte alignment. X64 memory allocations have a default 16-byte alignment.
//       If we ever map to YMM or ZMM register, we'll need 32-byte and 64-byte alignments, respectively.
class d3d12_camera
{
public:
    explicit d3d12_camera(camera_init_info info);

    void update();
    void up(math::v3 up);
    constexpr void field_of_view(f32 fov);
    constexpr void aspect_ratio(f32 aspect_ratio);
    constexpr void orthographic_size(f32 size);
    constexpr void near_z(f32 near_z);
    constexpr void far_z(f32 far_z);

    [[nodiscard]] constexpr DirectX::XMMATRIX view() const { return _view; }
    [[nodiscard]] constexpr DirectX::XMMATRIX projection() const { return _projection; }
    [[nodiscard]] constexpr DirectX::XMMATRIX inverse_projection() const { return _inverse_projection; }
    [[nodiscard]] constexpr DirectX::XMMATRIX view_projection() const { return _view_projection; }
    [[nodiscard]] constexpr DirectX::XMMATRIX inverse_view_projection() const { return _inverse_view_projection; }
    [[nodiscard]] constexpr DirectX::XMVECTOR position() const { return _position; }
    [[nodiscard]] constexpr DirectX::XMVECTOR direction() const { return _direction; }
    [[nodiscard]] constexpr DirectX::XMVECTOR up() const { return _up; }
    [[nodiscard]] constexpr f32 near_z() const { return _near_z; }
    [[nodiscard]] constexpr f32 far_z() const { return _far_z; }
    [[nodiscard]] constexpr f32 field_of_view() const { return _field_of_view; }
    [[nodiscard]] constexpr f32 aspect_ratio() const { return _aspect_ratio; }
    [[nodiscard]] constexpr f32 orthographic_size() const { return _orthographic_size; }
    [[nodiscard]] constexpr graphics::camera::type projection_type() const { return _projection_type; }
    [[nodiscard]] constexpr id::id_type entity_id() const { return _entity_id; }
private:
    DirectX::XMMATRIX       _view;
    DirectX::XMMATRIX       _projection;
    DirectX::XMMATRIX       _inverse_projection;
    DirectX::XMMATRIX       _view_projection;
    DirectX::XMMATRIX       _inverse_view_projection;
    DirectX::XMVECTOR       _position{};
    DirectX::XMVECTOR       _direction{};
    DirectX::XMVECTOR       _up;
    f32                     _near_z;
    f32                     _far_z;
    union {
        f32                 _field_of_view;
        f32                 _orthographic_size;
    };
    f32                     _aspect_ratio;      
    graphics::camera::type  _projection_type;
    id::id_type             _entity_id;
    bool                    _is_dirty;
};

[[nodiscard]]graphics::camera create(camera_init_info info);
void remove(camera_id id);
void set_parameter(camera_id id, camera_parameter::parameter parameter, const void *const data, u32 data_size);
void get_parameter(camera_id id, camera_parameter::parameter parameter, void *const data, u32 data_size);
[[nodiscard]] d3d12_camera& get(camera_id id);
}