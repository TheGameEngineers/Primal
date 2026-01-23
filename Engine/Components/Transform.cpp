// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Transform.h"

namespace primal::transform {
namespace {

#ifdef _MSC_VER
#pragma warning(push)
#pragma warning(disable:4201)
#endif
// C4201: nonstandard extension used: nameless struct/union
struct local_frame
{
    union {
        struct {
            math::v3 right;
            math::v3 up;
            math::v3 front;
        };
        math::m3x3 frame;
    };

    local_frame()
        : right{ 1.f, 0.f, 0.f }, up{ 0.f, 1.f, 0.f }, front{ 0.f, 0.f, 1.f }
    {}
};

#ifdef _MSC_VER
#pragma warning(pop)
#endif

utl::vector<math::m4x4>     to_world;
utl::vector<math::m4x4>     inv_world;
utl::vector<math::v4>       rotations;
utl::vector<local_frame>    local_frames;
utl::vector<math::v3>       positions;
utl::vector<math::v3>       scales;
utl::vector<u8>             has_transform;
utl::vector<u8>             changes_from_previous_frame;
u8                          read_write_flag;

void
calculate_transform_matrices(id::id_type index)
{
    assert(rotations.size() > index);
    assert(positions.size() > index);
    assert(scales.size() > index);

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

void
calculate_local_frame(const math::v4& rotation, local_frame& result)
{
    using namespace DirectX;

    local_frame frame{};

    XMVECTOR right{ XMLoadFloat3(&frame.right) };
    XMVECTOR up{ XMLoadFloat3(&frame.up) };
    XMVECTOR front{ XMLoadFloat3(&frame.front) };
    XMVECTOR rotation_quat{ XMLoadFloat4(&rotation) };

    right = XMVector3Normalize(XMVector3Rotate(right, rotation_quat));
    up = XMVector3Normalize(XMVector3Rotate(up, rotation_quat));
    front = XMVector3Normalize(XMVector3Cross(right, up));

    XMStoreFloat3(&result.right, right);
    XMStoreFloat3(&result.up, up);
    XMStoreFloat3(&result.front, front);
}

void
set_rotation(transform_id id, const math::v4& rotation_quaternion)
{
    const u32 index{ id::index(id) };
    rotations[index] = rotation_quaternion;
    calculate_local_frame(rotation_quaternion, local_frames[index]);
    has_transform[index] = 0;
    changes_from_previous_frame[index] |= component_flags::rotation;
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
        calculate_local_frame(rotation, local_frames[entity_index]);
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
        local_frames.emplace_back();
        calculate_local_frame(rotations.back(), local_frames.back());
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

math::v3
component::right() const
{
    assert(is_valid());
    return local_frames[id::index(_id)].right;
}

math::v3
component::up() const
{
    assert(is_valid());
    return local_frames[id::index(_id)].up;
}

math::v3
component::front() const
{
    assert(is_valid());
    return local_frames[id::index(_id)].front;
}

math::m3x3
component::local_frame() const
{
    assert(is_valid());
    return local_frames[id::index(_id)].frame;
}

DirectX::XMVECTOR
component::calculate_local_position(math::v3 delta) const
{
    assert(is_valid());
    const id::id_type index{ id::index(_id) };
    using namespace DirectX;
    XMMATRIX frame{ XMLoadFloat3x3(&local_frames[index].frame) };
    XMVECTOR l_pos{ XMLoadFloat3(&delta) };
    l_pos = XMVector3Transform(l_pos, frame);
    XMVECTOR w_pos{ XMLoadFloat3(&positions[index]) };

    return w_pos + l_pos;
}

DirectX::XMVECTOR
component::calculate_absolute_rotation(math::v3 rotation) const
{
    assert(is_valid());
    return DirectX::XMQuaternionRotationRollPitchYawFromVector(XMLoadFloat3(&rotation));
}

DirectX::XMVECTOR
component::calculate_local_rotation(math::v3 delta) const
{
    assert(is_valid());
    const id::id_type index{ id::index(_id) };

    using namespace DirectX;
    XMVECTOR d{ XMQuaternionRotationRollPitchYawFromVector(XMLoadFloat3(&delta)) };
    XMVECTOR q{ XMLoadFloat4(&rotations[index]) };

    return XMQuaternionMultiply(d, q);
}

DirectX::XMVECTOR
component::calculate_world_rotation(math::v3 delta) const
{
    assert(is_valid());
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

    const id::id_type index{ id::index(_id) };

    using namespace DirectX;
    XMVECTOR a{ XMLoadFloat3(&axis) };
    XMVECTOR d{ XMQuaternionRotationNormal(a, angle) };
    XMVECTOR q{ XMLoadFloat4(&rotations[index]) };

    return XMQuaternionMultiply(q, d);
}

}