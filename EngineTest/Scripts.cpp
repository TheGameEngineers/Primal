// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Components/Entity.h"
#include "Components/Transform.h"
#include "Components/Script.h"

using namespace primal;

class rotator_script;
REGISTER_SCRIPT(rotator_script);
class rotator_script : public script::entity_script
{
public:
    constexpr explicit rotator_script(game_entity::entity entity)
        : script::entity_script{ entity } {}

    void begin_play() override {}
    void update(float dt) override
    {
        _angle += 0.25f * dt * math::two_pi;
        if (_angle > math::two_pi) _angle -= math::two_pi;
        math::v3a rot{ 0.f, _angle, 0.f };
        DirectX::XMVECTOR quat{ DirectX::XMQuaternionRotationRollPitchYawFromVector(DirectX::XMLoadFloat3A(&rot)) };
        math::v4 rot_quat{};
        DirectX::XMStoreFloat4(&rot_quat, quat);
        set_rotation(rot_quat);
    }

private:
    f32 _angle{ 0.f };
};

class fan_script;
REGISTER_SCRIPT(fan_script);
class fan_script : public script::entity_script
{
public:
    constexpr explicit fan_script(game_entity::entity entity)
        : script::entity_script{ entity } {}

    void begin_play() override {}
    void update(float dt) override
    {
        _angle -= 1.f * dt * math::two_pi;
        if (_angle > math::two_pi) _angle += math::two_pi;
        math::v3a rot{ _angle, 0.f, 0.f };
        DirectX::XMVECTOR quat{ DirectX::XMQuaternionRotationRollPitchYawFromVector(DirectX::XMLoadFloat3A(&rot)) };
        math::v4 rot_quat{};
        DirectX::XMStoreFloat4(&rot_quat, quat);
        set_rotation(rot_quat);
    }

private:
    f32 _angle{ 0.f };
};

class wibbly_wobbly_script;
REGISTER_SCRIPT(wibbly_wobbly_script);
class wibbly_wobbly_script : public script::entity_script
{
public:
    constexpr explicit wibbly_wobbly_script(game_entity::entity entity)
        : script::entity_script{ entity } {}

    void begin_play() override {}
    void update(float dt) override
    {
        _angle -= 0.01f * dt * math::two_pi;
        if (_angle > math::two_pi) _angle += math::two_pi;
        f32 x{ _angle * 2.f - math::pi };
        const f32 s1{ 0.05f * std::sin(x) * std::sin(std::sin(x / 1.62f) + std::sin(1.62f * x) + std::sin(3.24f * x)) };
        x = _angle;
        const f32 s2{ 0.05f * std::sin(x) * std::sin(std::sin(x / 1.62f) + std::sin(1.62f * x) + std::sin(3.24f * x)) };

        math::v3a rot{ s1, 0.f, s2 };
        DirectX::XMVECTOR quat{ DirectX::XMQuaternionRotationRollPitchYawFromVector(DirectX::XMLoadFloat3A(&rot)) };
        math::v4 rot_quat{};
        DirectX::XMStoreFloat4(&rot_quat, quat);
        set_rotation(rot_quat);
        math::v3 pos{ position() };
        pos.y = 1.3f + 0.2f * std::sin(x) * std::sin(std::sin(x / 1.62f) + std::sin(1.62f * x) + std::sin(3.24f * x));
        set_position(pos);
    }

private:
    f32 _angle{ 0.f };
};