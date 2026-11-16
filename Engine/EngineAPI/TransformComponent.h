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

    [[nodiscard]] static DirectX::XMVECTOR XM_CALLCONV calculate_local_position(DirectX::FXMMATRIX frame, DirectX::FXMVECTOR world_position, math::v3 delta)
    {
        using namespace DirectX;
        return world_position + XMVector3Transform(XMLoadFloat3(&delta), frame);
    }

    [[nodiscard]] static DirectX::XMVECTOR XM_CALLCONV calculate_absolute_rotation(math::v3 rotation)
    {
        return DirectX::XMQuaternionRotationRollPitchYawFromVector(XMLoadFloat3(&rotation));
    }

    [[nodiscard]] static DirectX::XMVECTOR XM_CALLCONV calculate_local_rotation(DirectX::FXMVECTOR rotation, math::v3 delta)
    {
        using namespace DirectX;
        XMVECTOR d{ XMQuaternionRotationRollPitchYawFromVector(XMLoadFloat3(&delta)) };
        return XMQuaternionMultiply(d, rotation);
    }

    [[nodiscard]] static DirectX::XMVECTOR XM_CALLCONV calculate_world_rotation(DirectX::FXMVECTOR rotation, math::v3 delta)
    {
        math::v3 axis{ 1.f, 0.f, 0.f };
        f32 angle{ delta.x };

        if (abs(delta.x) < math::epsilon)
        {
            if (abs(delta.z) < math::epsilon)
            {
                axis = { 0.f, 1.f, 0.f };
                angle = delta.y;
            }
            else if (abs(delta.y) < math::epsilon)
            {
                axis = { 0.f, 0.f, 1.f };
                angle = delta.z;
            }
        }

        using namespace DirectX;
        XMVECTOR a{ XMLoadFloat3(&axis) };
        XMVECTOR d{ XMQuaternionRotationNormal(a, angle) };
        return XMQuaternionMultiply(rotation, d);
    }

    [[nodiscard]] static math::v3 get_euler_angles_from_local_frame(const math::m3x3& m)
    {
        float pitch{ 0.f }, yaw{ 0.f }, roll{ 0.f };
        // Assuming front is Z, up is Y and right is X
        if (m._32 < 1.f)
        {
            if (m._32 > -1.f)
            {
                pitch = asinf(-m._32);
                yaw = atan2f(m._31, m._33);
                roll = atan2f(m._12, m._22);
            }
            else
            {
                // m._32 == -1
                pitch = math::half_pi;
                yaw = -atan2f(-m._13, m._11);
                roll = 0.f;
            }
        }
        else
        {
            // m._32 == +1
            pitch = -math::half_pi;
            yaw = atan2f(-m._13, m._11);
            roll = 0.f;
        }

        return { pitch, yaw, roll };
    }

private:
    transform_id _id;
};

} // namespace primal::transform