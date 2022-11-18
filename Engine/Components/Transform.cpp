// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Transform.h"
#include "Entity.h"

namespace primal::transform
{
namespace {

utl::vector<math::m4x4> to_world;
utl::vector<math::m4x4> inv_world;
utl::vector<math::v4>   rotations;
utl::vector<math::v3>   orientations;
utl::vector<math::v3>   positions;
utl::vector<math::v3>   scales;
utl::vector<u8>         has_transform;
utl::vector<u8>         changes_from_previous_frame;
u8                      read_write_flag;

void
calculate_transform_matrices(id::id_type index)
{
    assert(rotations.size() >= index);
    assert(positions.size() >= index);
    assert(scales.size() >= index);

    using namespace DirectX;
    XMVECTOR r{ XMLoadFloat4(&rotations[index]) };
    XMVECTOR t{ XMLoadFloat3(&positions[index]) };
    XMVECTOR s{ XMLoadFloat3(&scales[index]) };

    XMMATRIX world{ XMMatrixAffineTransformation(s, XMQuaternionIdentity(), r, t) };
    XMStoreFloat4x4(&to_world[index], world);

    // NOTE: (F. Luna) Intro to DirectX 12, section 8.2.2
    world.r[3] = XMVectorSet(0.f, 0.f, 0.f, 1.f);
    XMMATRIX inverse_world{ XMMatrixInverse(nullptr, world) };
    XMStoreFloat4x4(&inv_world[index], inverse_world);

    has_transform[index] = 1;
}

math::v3
calculate_orientation(math::v4 rotation)
{
    using namespace DirectX;
    XMVECTOR rotation_quat{ XMLoadFloat4(&rotation) };
    XMVECTOR front{ XMVectorSet(0.f, 0.f, 1.f, 0.f) };
    math::v3 orientation;
    XMStoreFloat3(&orientation, XMVector3Rotate(front, rotation_quat));
    return orientation;
}

void
set_rotation(transform_id id, const math::v4& rotation_quaternion)
{
    const u32 index{ id::index(id) };
    rotations[index] = rotation_quaternion;
    orientations[index] = calculate_orientation(rotation_quaternion);
    has_transform[index] = 0;
    changes_from_previous_frame[index] |= component_flags::rotation;
}

void
set_orientation(transform_id, const math::v3&)
{
}

void
set_position(transform_id id, const math::v3& position)
{
    const u32 index{ id::index(id) };
    positions[index] = position;
    has_transform[index] = 0;
    changes_from_previous_frame[index] |= component_flags::position;
}

void
set_scale(transform_id id, const math::v3& scale)
{
    const u32 index{ id::index(id) };
    scales[index] = scale;
    has_transform[index] = 0;
    changes_from_previous_frame[index] |= component_flags::scale;
}

} // anonymous namespace

component
create(init_info info, game_entity::entity entity)
{
    assert(entity.is_valid());
    const id::id_type entity_index{ id::index(entity.get_id()) };

    if (positions.size() > entity_index)
    {
        math::v4 rotation{ info.rotation };
        rotations[entity_index] = rotation;
        orientations[entity_index] = calculate_orientation(rotation);
        positions[entity_index] = math::v3{ info.position };
        scales[entity_index] = math::v3{ info.scale };
        has_transform[entity_index] = 0;
        changes_from_previous_frame[entity_index] = (u8)component_flags::all;
    }
    else
    {
        assert(positions.size() == entity_index);
        to_world.emplace_back();
        inv_world.emplace_back();
        rotations.emplace_back(info.rotation);
        orientations.emplace_back(calculate_orientation(math::v4{ info.rotation }));
        positions.emplace_back(info.position);
        scales.emplace_back(info.scale);
        has_transform.emplace_back((u8)0);
        changes_from_previous_frame.emplace_back((u8)component_flags::all);
    }

    // NOTE: each entity has a transform component. Therefor, id's for transform components
    //       are exactly the same as entity ids.
    return component{ transform_id{ entity.get_id() } };
}

void
remove([[maybe_unused]] component c)
{
    assert(c.is_valid());
}

void
get_transform_matrices(const game_entity::entity_id id, math::m4x4& world, math::m4x4& inverse_world)
{
    assert(game_entity::entity{ id }.is_valid());

    const id::id_type entity_index{ id::index(id) };
    if (!has_transform[entity_index])
    {
        calculate_transform_matrices(entity_index);
    }

    world = to_world[entity_index];
    inverse_world = inv_world[entity_index];
}

void
get_updated_components_flags(const game_entity::entity_id *const ids, u32 count, u8 *const flags)
{
    assert(ids && count && flags);
    read_write_flag = 1;

    for (u32 i{ 0 }; i < count; ++i)
    {
        assert(game_entity::entity{ ids[i] }.is_valid());
        flags[i] = changes_from_previous_frame[id::index(ids[i])];
    }
}

void
update(const component_cache *const cache, u32 count)
{
    assert(cache && count);

    // NOTE: clearing "changes_from_previous_frame" happens once every frame when there will be no reads and the caches are
    //       about to be applied by calling this function (i.e. the rest of the current frame will only have writes).
    if (read_write_flag)
    {
        memset(changes_from_previous_frame.data(), 0, changes_from_previous_frame.size());
        read_write_flag = 0;
    }

    for (u32 i{ 0 }; i < count; ++i)
    {
        const component_cache& c{ cache[i] };
        assert(component{ c.id }.is_valid());

        if (c.flags & component_flags::rotation)
        {
            set_rotation(c.id, c.rotation);
        }

        if (c.flags & component_flags::orientation)
        {
            set_orientation(c.id, c.orientation);
        }

        if (c.flags & component_flags::position)
        {
            set_position(c.id, c.position);
        }

        if (c.flags & component_flags::scale)
        {
            set_scale(c.id, c.scale);
        }
    }
}

math::v4
component::rotation() const
{
    assert(is_valid());
    return rotations[id::index(_id)];
}

math::v3
component::orientation() const
{
    assert(is_valid());
    return orientations[id::index(_id)];
}

math::v3
component::position() const
{
    assert(is_valid());
    return positions[id::index(_id)];
}

math::v3
component::scale() const
{
    assert(is_valid());
    return scales[id::index(_id)];
}


}