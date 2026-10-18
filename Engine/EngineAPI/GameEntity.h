// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "CommonHeaders.h"
#include "Engine.h"
#include "TransformComponent.h"
#include "ScriptComponent.h"
#include "GeometryComponent.h"

namespace primal {

struct component_type
{
    enum type : u32 {
        transform,
        script,
        geometry,

        count
    };
};

namespace game_entity {

DEFINE_TYPED_ID(entity_id);

class entity
{
public:
    constexpr explicit entity(entity_id id) : _id{ id } {}
    constexpr entity() : _id{ id::invalid_id } {}
    [[nodiscard]] constexpr entity_id get_id() const { return _id; }
    [[nodiscard]] constexpr bool is_valid() const { return id::is_valid(_id); }

    [[nodiscard]] transform::component transform() const;
    [[nodiscard]] script::component script() const;
    [[nodiscard]] geometry::component geometry() const;

    [[nodiscard]] math::v4 rotation() const { return transform().rotation(); }
    [[nodiscard]] math::v3 position() const { return transform().position(); }
    [[nodiscard]] math::v3 scale() const { return transform().scale(); }
    [[nodiscard]] math::v3 right() const { return transform().right(); }
    [[nodiscard]] math::v3 up() const { return transform().up(); }
    [[nodiscard]] math::v3 front() const { return transform().front(); }
    [[nodiscard]] math::m3x3 local_frame() const { return transform().local_frame(); }

    void rotation(math::v4 rotation_quaternion) const { transform().rotation(rotation_quaternion); }
    void position(math::v3 position) const { transform().position(position); }
    void scale(math::v3 scale) const { transform().scale(scale); }

private:
    entity_id _id;
};
} // namespace game_entity
} // namespace primal

namespace primal::script {
class entity_script : public game_entity::entity
{
public:
    virtual ~entity_script() = default;
    virtual void begin_play() {}
    virtual void update(f32) {}
protected:
    constexpr explicit entity_script(game_entity::entity entity)
        : game_entity::entity{ entity.get_id() } {}
};

namespace detail {
using script_ptr = std::unique_ptr<entity_script>;
using script_creator = script_ptr(*)(game_entity::entity entity);
using string_hash = std::hash<std::string>;

u8 register_script(size_t, script_creator);
#ifdef USE_WITH_EDITOR
extern "C" __declspec(dllexport)
#endif //USE_WITH_EDITOR
script_creator get_script_creator(size_t tag);

template<class script_class>
script_ptr create_script(game_entity::entity entity)
{
    assert(entity.is_valid());
    return std::make_unique<script_class>(entity);
}

#ifdef USE_WITH_EDITOR
u8 add_script_name(const char* name);

#define REGISTER_SCRIPT(TYPE)                                           \
        namespace {                                                     \
        const u8 _reg_##TYPE                                            \
        { primal::script::detail::register_script(                      \
              primal::script::detail::string_hash()(#TYPE),             \
              &primal::script::detail::create_script<TYPE>) };          \
        const u8 _name_##TYPE                                           \
        { primal::script::detail::add_script_name(#TYPE) };             \
        }                                                               

#else
#define REGISTER_SCRIPT(TYPE)                                           \
        namespace {                                                     \
        const u8 _reg_##TYPE                                            \
        { primal::script::detail::register_script(                      \
              primal::script::detail::string_hash()(#TYPE),             \
              &primal::script::detail::create_script<TYPE>) };          \
        }

#endif // USE_WITH_EDITOR
} // namespace detail
} // namespace script
