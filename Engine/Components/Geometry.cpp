// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Geometry.h"
#include "Entity.h"
#include "Graphics/Renderer.h"

namespace primal::geometry {
namespace {

utl::vector<id::id_type>            render_item_ids;
utl::vector<game_entity::entity_id> owning_entity_ids;
utl::vector<geometry_id>            owner_ids;
utl::vector<id::id_type>            id_mapping;

utl::vector<id::generation_type>    generations;
utl::deque<geometry_id>             free_ids;

#if _DEBUG
bool
exists(geometry_id id)
{
    assert(id::is_valid(id));
    const id::id_type index{ id::index(id) };
    assert(index < generations.size() && !(id::is_valid(id_mapping[index]) && id_mapping[index] >= render_item_ids.size()));
    assert(generations[index] == id::generation(id));
    return (generations[index] == id::generation(id)) &&
        id::is_valid(id_mapping[index]) &&
        id::is_valid(render_item_ids[id_mapping[index]]);
}
#endif
} // anonymous namespace

component
create(init_info info, game_entity::entity entity)
{
    assert(entity.is_valid());
    assert(id::is_valid(info.geometry_content_id) && info.material_count && info.material_ids);

    geometry_id id{};
    if (free_ids.size() > id::min_deleted_elements)
    {
        id = free_ids.front();
        assert(!exists(id));
        free_ids.pop_front();
        id = geometry_id{ id::new_generation(id) };
        ++generations[id::index(id)];
    }
    else
    {
        id = geometry_id{ (id::id_type)id_mapping.size() };
        id_mapping.emplace_back();
        generations.push_back(0);
    }

    assert(id::is_valid(id));
    const id::id_type index{ (id::id_type)render_item_ids.size() };
    render_item_ids.emplace_back(graphics::add_render_item(entity.get_id(), info.geometry_content_id, info.material_count, info.material_ids));
    owner_ids.emplace_back(id::index(id));
    owning_entity_ids.emplace_back(entity.get_id());
    id_mapping[id::index(id)] = index;
    return component{ id };
}

void
remove(component c)
{
    assert(c.is_valid() && exists(c.get_id()));
    const geometry_id id{ c.get_id() };
    const id::id_type index{ id_mapping[id::index(id)] };
    const geometry_id last_id{ owner_ids.back() };
    graphics::remove_render_item(render_item_ids[index]);
    utl::erase_unordered(render_item_ids, index);
    utl::erase_unordered(owning_entity_ids, index);
    utl::erase_unordered(owner_ids, index);
    id_mapping[id::index(last_id)] = index;
    id_mapping[id::index(id)] = id::invalid_id;

    if (generations[id::index(id)] < id::max_generation)
    {
        free_ids.push_back(id);
    }
}

void
get_render_item_ids(id::id_type *const item_ids, u32 count)
{
    assert(item_ids && render_item_ids.size() >= count);
    memcpy(item_ids, render_item_ids.data(), count * sizeof(id::id_type));
}

void
get_render_item_ids(const id::id_type *const geometry_ids, id::id_type *const item_ids, u32 count)
{
    assert(geometry_ids && item_ids && count);
    assert(render_item_ids.size() >= count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        const geometry_id id{ geometry_ids[i] };
        assert(id::is_valid(id) && exists(id));
        const id::id_type index{ id_mapping[id::index(id)] };
        assert(index < render_item_ids.size() && id::is_valid(render_item_ids[index]));
        item_ids[i] = render_item_ids[index];
    }
}

void
get_entity_ids(const id::id_type *const geometry_ids, game_entity::entity_id *const entity_ids, u32 count)
{
    assert(geometry_ids && entity_ids && count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        const geometry_id id{ geometry_ids[i] };
        assert(id::is_valid(id) && exists(id));
        const id::id_type index{ id_mapping[id::index(id)] };
        assert(index < owning_entity_ids.size() && id::is_valid(owning_entity_ids[index]));
        entity_ids[i] = owning_entity_ids[index];
    }
}

}