// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Common.h"
#include "CommonHeaders.h"
#include "Id.h"
#include "Components/Entity.h"
#include "Components/Transform.h"
#include "Components/Script.h"
#include "Components/Geometry.h"

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

game_entity::entity entity_from_id(id::id_type id)
{
    return game_entity::entity{ game_entity::entity_id{id} };
}

} // anonymous namespace

std::mutex mutex{};

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

EDITOR_INTERFACE u32
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
    return game_entity::update_component(game_entity::entity_id{ entity_id }, entity_info, type);
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
