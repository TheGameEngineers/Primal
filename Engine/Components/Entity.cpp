// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Entity.h"
#include "Transform.h"
#include "Script.h"
#include "Geometry.h"

namespace primal::game_entity {
namespace {

utl::vector<transform::component>       transforms;
utl::vector<script::component>          scripts;
utl::vector<geometry::component>        geometries;

utl::vector<id::generation_type>        generations;
utl::deque<entity_id>                   free_ids;

} // anonymous namespace

entity
create(entity_info info)
{
    assert(info.transform); // All game entities must have a transform component
    if (!info.transform) return {};

    entity_id id{};

    if (free_ids.size() > id::min_deleted_elements)
    {
        id = free_ids.front();
        assert(!is_alive(id));
        free_ids.pop_front();
        id = entity_id{ id::new_generation(id) };
        ++generations[id::index(id)];
    }
    else
    {
        id = entity_id{ (id::id_type)generations.size() };
        generations.push_back(0);

        // Resize components
        // NOTE: we don't call resize(), so the number of memory allocations stays low
        transforms.emplace_back();
        scripts.emplace_back();
        geometries.emplace_back();
    }

    const entity new_entity{ id };
    const id::id_type index{ id::index(id) };

    // Create transform component
    assert(!transforms[index].is_valid());
    transforms[index] = transform::create(*info.transform, new_entity);
    assert(transforms[index].get_id() == id);
    if (!transforms[index].is_valid()) return {};

    // Create script component
    if (info.script && info.script->script_creator)
    {
        assert(!scripts[index].is_valid());
        scripts[index] = script::create(*info.script, new_entity);
        assert(scripts[index].is_valid());
    }

    // Create geometry component
    if (info.geometry)
    {
        assert(!geometries[index].is_valid());
        geometries[index] = geometry::create(*info.geometry, new_entity);
        assert(geometries[index].is_valid());
    }

    return new_entity;
}

void
remove(entity_id id)
{
    const id::id_type index{ id::index(id) };
    assert(is_alive(id));

    if (geometries[index].is_valid())
    {
        geometry::remove(geometries[index]);
        geometries[index] = {};
    }

    if (scripts[index].is_valid())
    {
        script::remove(scripts[index]);
        scripts[index] = {};
    }

    transform::remove(transforms[index]);
    transforms[index] = {};

    if (generations[index] < id::max_generation)
    {
        free_ids.push_back(id);
    }
}

bool
update_component(entity_id id, entity_info info, component_type::type type)
{
    assert(is_alive(id) && type != component_type::transform);
    if (type == component_type::transform) return false;
    entity entity{ id };
    const id::id_type index{ id::index(id) };

    if (type == component_type::script)
    {
        if (scripts[index].is_valid())
        {
            script::remove(scripts[index]);
            scripts[index] = {};
        }

        if (info.script && info.script->script_creator)
        {
            script::component new_script{ script::create(*info.script, entity) };
            assert(new_script.is_valid());

            if (new_script.is_valid())
            {
                scripts[index] = new_script;
                return true;
            }
        }
    }
    else if (type == component_type::geometry)
    {
        if (geometries[index].is_valid())
        {
            geometry::remove(geometries[index]);
            geometries[index] = {};
        }

        if (info.geometry)
        {
            geometry::component new_geometry{ geometry::create(*info.geometry, entity) };
            assert(new_geometry.is_valid());

            if (new_geometry.is_valid())
            {
                geometries[index] = new_geometry;
                return true;
            }
        }
    }

    return false;
}

bool
is_alive(entity_id id)
{
    assert(id::is_valid(id));
    const id::id_type index{ id::index(id) };
    assert(index < generations.size());
    return generations[index] == id::generation(id) && transforms[index].is_valid();
}

transform::component
entity::transform() const
{
    assert(is_alive(_id));
    return transforms[id::index(_id)];
}

script::component
entity::script() const
{
    assert(is_alive(_id));
    return scripts[id::index(_id)];
}

geometry::component
entity::geometry() const
{
    assert(is_alive(_id));
    return geometries[id::index(_id)];
}

}