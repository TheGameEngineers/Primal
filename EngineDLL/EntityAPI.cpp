// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Common.h"
#include "CommonHeaders.h"
#include "Id.h"
#include "Components/Entity.h"
#include "Components/Transform.h"
#include "Components/Script.h"
#include "Components/Geometry.h"
#include "Utilities/Threading.h"

using namespace primal;

math::v4 to_quat(math::v3 angles, bool is_degrees)
{
    using namespace DirectX;
    if (is_degrees) angles = math::to_radians(angles);

    math::v4 quat_result{};
    XMVECTOR quat{ XMQuaternionRotationRollPitchYawFromVector(XMLoadFloat3(&angles)) };
    XMStoreFloat4(&quat_result, quat);
    return quat_result;
}

namespace {

struct transform_component
{
    f32 position[3];
    f32 rotation[3];
    f32 scale[3];

    transform::init_info to_init_info() const
    {
        using namespace DirectX;
        transform::init_info info{};
        memcpy(&info.position[0], &position[0], sizeof(position));
        memcpy(&info.scale[0], &scale[0], sizeof(scale));
        math::v3 rot{ &rotation[0] };
        math::v4 rot_quat{ to_quat(rot, true) };
        memcpy(&info.rotation[0], &rot_quat.x, sizeof(info.rotation));
        return info;
    }
};

struct script_component
{
    script::detail::script_creator script_creator;

    script::init_info to_init_info() const
    {
        script::init_info info{};
        info.script_creator = script_creator;
        return info;
    }
};

struct geometry_component
{
    id::id_type     geometry_content_id;
    u32             material_count;
    id::id_type*    material_ids;

    geometry::init_info to_init_info() const
    {
        geometry::init_info info{};
        info.geometry_content_id = geometry_content_id;
        info.material_count = material_count;
        info.material_ids = material_ids;
        return info;
    }
};

struct game_entity_descriptor
{
    transform_component transform;
    script_component    script;
    geometry_component  geometry;
};

utl::vector <transform::component_cache> transform_cache{};

math::v3
get_euler_angles_from_local_frame(const math::m3x3& m)
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

    return { pitch * math::to_deg, yaw * math::to_deg, roll * math::to_deg };
}

math::v3
get_local_pos(math::v3 pos, u32 id)
{
    transform::component xform{ transform::transform_id{id} };
    DirectX::XMVECTOR position{ xform.calculate_local_position(pos) };
    math::v3 result{};
    DirectX::XMStoreFloat3(&result, position);
    return result;
}

math::v4
get_world_quat(math::v3 w, u32 id)
{
    w = math::to_radians(w);
    transform::component xform{ transform::transform_id{id} };
    DirectX::XMVECTOR q{ xform.calculate_world_rotation(w) };
    math::v4 result{};
    DirectX::XMStoreFloat4(&result, q);
    return result;
}

math::v4
get_local_quat(math::v3 w, u32 id)
{
    w = math::to_radians(w);
    transform::component xform{ transform::transform_id{id} };
    DirectX::XMVECTOR q{ xform.calculate_local_rotation(w) };
    math::v4 result{};
    DirectX::XMStoreFloat4(&result, q);
    return result;
}

math::v4
get_absolute_quat(math::v3 w, u32 id)
{
    w = math::to_radians(w);
    transform::component xform{ transform::transform_id{id} };
    DirectX::XMVECTOR q{ xform.calculate_absolute_rotation(w) };
    math::v4 result{};
    DirectX::XMStoreFloat4(&result, q);
    return result;
}
} // anonymous namespace

utl::ticket_mutex mutex{};

EDITOR_INTERFACE id::id_type
CreateGameEntity(game_entity_descriptor* e)
{
    std::lock_guard lock{ mutex };
    assert(e);
    game_entity_descriptor& desc{ *e };
    transform::init_info transform_info{ desc.transform.to_init_info() };
    script::init_info script_info{ desc.script.to_init_info() };
    geometry::init_info geometry_info{ desc.geometry.to_init_info() };
    game_entity::entity_info entity_info
    {
        &transform_info,
        &script_info,
        id::is_valid(desc.geometry.geometry_content_id) ? &geometry_info : nullptr,
    };
    return game_entity::create(entity_info).get_id();
}

EDITOR_INTERFACE void
RemoveGameEntity(id::id_type id)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(id));
    game_entity::remove(game_entity::entity_id{ id });
}

EDITOR_INTERFACE b32
UpdateComponent(id::id_type entity_id, game_entity_descriptor* e, component_type::type type)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(entity_id) && e && type != component_type::transform);
    game_entity_descriptor& desc{ *e };
    script::init_info script_info{ desc.script.to_init_info() };
    geometry::init_info geometry_info{ desc.geometry.to_init_info() };
    game_entity::entity_info entity_info
    {
        nullptr,
        &script_info,
        id::is_valid(desc.geometry.geometry_content_id) ? &geometry_info : nullptr,
    };
    return game_entity::update_component(game_entity::entity_id{ entity_id }, entity_info, type) ? 1 : 0;
}

EDITOR_INTERFACE id::id_type
GetComponentId(id::id_type entity_id, component_type::type type)
{
    std::lock_guard lock{ mutex };
    assert(id::is_valid(entity_id));
    game_entity::entity entity{ game_entity::entity_id{ entity_id } };

    switch (type)
    {
    case component_type::transform: return entity.transform().get_id();
    case component_type::script:    return entity.script().get_id();
    case component_type::geometry:  return entity.geometry().get_id();
    default: return id::invalid_id;
    }
}

EDITOR_INTERFACE void
GetPosition(id::id_type* ids, f32* x, f32* y, f32* z, u32 count)
{
    assert(ids && count);
    std::lock_guard lock{ mutex };

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(transform::transform_id{ ids[i] } == game_entity::entity{ game_entity::entity_id{ids[i]} }.transform().get_id());
        // NOTE: transform id is the same as entity id
        const transform::transform_id id{ ids[i] };
        transform::component t{ id };
        math::v3 pos{ t.position() };
        x[i] = pos.x; y[i] = pos.y; z[i] = pos.z;
    }
}

EDITOR_INTERFACE void
GetRotation(id::id_type* ids, f32* x, f32* y, f32* z, u32 count)
{
    assert(ids && count);
    std::lock_guard lock{ mutex };
    using namespace DirectX;

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(transform::transform_id{ ids[i] } == game_entity::entity{ game_entity::entity_id{ids[i]} }.transform().get_id());
        // NOTE: transform id is the same as entity id
        const transform::transform_id id{ ids[i] };
        transform::component t{ id };
        math::v3 euler{ get_euler_angles_from_local_frame(t.local_frame()) };
        x[i] = euler.x; y[i] = euler.y; z[i] = euler.z;
    }
}

EDITOR_INTERFACE void
GetScale(id::id_type* ids, f32* x, f32* y, f32* z, u32 count)
{
    assert(ids && count);
    std::lock_guard lock{ mutex };

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(transform::transform_id{ ids[i] } == game_entity::entity{ game_entity::entity_id{ids[i]} }.transform().get_id());
        // NOTE: transform id is the same as entity id
        const transform::transform_id id{ ids[i] };
        transform::component t{ id };
        math::v3 scale{ t.scale() };
        x[i] = scale.x; y[i] = scale.y; z[i] = scale.z;
    }
}

EDITOR_INTERFACE void
SetPosition(id::id_type* ids, f32* x, f32* y, f32* z, u32 count, b32 is_local)
{
    assert(ids && count);
    std::lock_guard lock{ mutex };
    
    transform_cache.resize(count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        math::v3 v{ x[i], y[i], z[i] };
        transform_cache[i].position = !is_local ? v : get_local_pos(v, ids[i]);
        transform_cache[i].flags = transform::component_flags::position;
        assert(transform::transform_id{ ids[i] } == game_entity::entity{ game_entity::entity_id{ids[i]} }.transform().get_id());
        // NOTE: transform id is the same as entity id
        transform_cache[i].id = transform::transform_id {ids[i]};
    }

    transform::update(transform_cache.data(), count);
}

EDITOR_INTERFACE void
SetRotation(id::id_type* ids, f32* x, f32* y, f32* z, u32 count, u32 frame)
{
    assert(ids && count);
    std::lock_guard lock{ mutex };

    transform_cache.resize(count);

    using frame_fn_ptr = math::v4(*)(math::v3, u32);
    frame_fn_ptr frame_fn{ &get_absolute_quat };
    
    if (frame == transform::space::local)
    {
        frame_fn = &get_local_quat;
    }
    else if (frame == transform::space::world)
    { 
        frame_fn = &get_world_quat;
    }


    for (u32 i{ 0 }; i < count; ++i)
    {
        math::v3 v{ x[i], y[i], z[i] };
        transform_cache[i].rotation = frame_fn(v, ids[i]);
        transform_cache[i].flags = transform::component_flags::rotation;
        assert(transform::transform_id{ ids[i] } == game_entity::entity{ game_entity::entity_id{ids[i]} }.transform().get_id());
        // NOTE: transform id is the same as entity id
        transform_cache[i].id = transform::transform_id{ ids[i] };
    }

    transform::update(transform_cache.data(), count);
}

EDITOR_INTERFACE void
SetScale(id::id_type* ids, f32* x, f32* y, f32* z, u32 count, b32)
{
    assert(ids && count);
    std::lock_guard lock{ mutex };

    transform_cache.resize(count);

    for (u32 i{ 0 }; i < count; ++i)
    {
        transform_cache[i].scale = { x[i], y[i], z[i] };
        transform_cache[i].flags = transform::component_flags::scale;
        assert(transform::transform_id{ ids[i] } == game_entity::entity{ game_entity::entity_id{ids[i]} }.transform().get_id());
        // NOTE: transform id is the same as entity id
        transform_cache[i].id = transform::transform_id{ ids[i] };
    }

    transform::update(transform_cache.data(), count);
}