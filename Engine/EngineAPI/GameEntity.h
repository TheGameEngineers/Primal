// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once

#include "..\Components\ComponentsCommon.h"
#include "TransfromComponent.h"
#include "ScriptComponent.h"

namespace primal {
namespace game_entity {

DEFINE_TYPED_ID(entity_id);

class entity {
public:
    constexpr explicit entity(entity_id id) : _id{ id } {}
    constexpr entity() : _id{ id::invalid_id } {}
    constexpr entity_id get_id() const { return _id; }
    constexpr bool is_valid() const { return id::is_valid(_id); }

    transform::component transform() const;
    script::component script() const;
private:
    entity_id _id;
};
} // namespace game_entity

namespace script
{
class entity_script : public game_entity::entity
{
public:
    virtual ~entity_script() = default;
    virtual void begin_play() {}
    virtual void update(float) {}
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
}