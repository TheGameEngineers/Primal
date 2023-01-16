// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "D3D12Light.h"
#include "D3D12Core.h"
#include "Shaders/SharedTypes.h"
#include "EngineAPI/GameEntity.h"

namespace primal::graphics::d3d12::light {
namespace {

struct light_owner
{
    game_entity::entity_id  entity_id{ id::invalid_id };
    u32                     data_index;
    graphics::light::type   type;
    bool                    is_enabled;
};

// NOTE (to myself): don't forget to #undef CONSTEXPR when you copy/paste this block of code!
#if USE_STL_VECTOR
#define CONSTEXPR
#else
#define CONSTEXPR constexpr
#endif

class light_set
{
public:
    constexpr graphics::light add(const light_init_info& info)
    {
        if (info.type == graphics::light::directional)
        {
            u32 index{ u32_invalid_id };
            // Find an available slot in the array if any.
            for (u32 i{ 0 }; i < _non_cullable_owners.size(); ++i)
            {
                if (!id::is_valid(_non_cullable_owners[i]))
                {
                    index = i;
                    break;
                }
            }

            if (index == u32_invalid_id)
            {
                index = (u32)_non_cullable_owners.size();
                _non_cullable_owners.emplace_back();
                _non_cullable_lights.emplace_back();
            }

            hlsl::DirectionalLightParameters& params{ _non_cullable_lights[index] };
            params.Color = info.color;
            params.Intensity = info.intensity;

            light_owner owner{ game_entity::entity_id{info.entity_id}, index, info.type, info.is_enabled };
            const light_id id{ _owners.add(owner) };
            _non_cullable_owners[index] = id;

            return graphics::light{ id, info.light_set_key };
        }
        else
        {
            return {};
        }
    }

    constexpr void remove(light_id id)
    {
        enable(id, false);

        const light_owner& owner{ _owners[id] };

        if (owner.type == graphics::light::directional)
        {
            _non_cullable_owners[owner.data_index] = light_id{ id::invalid_id };
        }
        else
        {
            // TODO: cullable lights
        }

        _owners.remove(id);
    }

    void update_transforms()
    {
        // Update direction for non-cullable lights
        for (const auto& id : _non_cullable_owners)
        {
            if (!id::is_valid(id)) continue;

            const light_owner& owner{ _owners[id] };
            if (owner.is_enabled)
            {
                const game_entity::entity entity{ game_entity::entity_id{owner.entity_id} };
                hlsl::DirectionalLightParameters& params{ _non_cullable_lights[owner.data_index] };
                params.Direction = entity.orientation();
            }
        }

        // TODO: cullable lights
    }

    constexpr void enable(light_id id, bool is_enabled)
    {
        _owners[id].is_enabled = is_enabled;

        if (_owners[id].type == graphics::light::directional)
        {
            return;
        }

        // TODO: cullable lights
    }

    constexpr void intensity(light_id id, f32 intensity)
    {
        if (intensity < 0.f) intensity = 0.f;

        const light_owner& owner{ _owners[id] };
        const u32 index{ owner.data_index };

        if (owner.type == graphics::light::directional)
        {
            assert(index < _non_cullable_lights.size());
            _non_cullable_lights[index].Intensity = intensity;
        }
        else
        {
            // TODO: cullable lights
        }
    }

    constexpr void color(light_id id, math::v3 color)
    {
        assert(color.x <= 1.f && color.y <= 1.f && color.z <= 1.f);
        assert(color.x >= 0.f && color.y >= 0.f && color.z >= 0.f);

        const light_owner& owner{ _owners[id] };
        const u32 index{ owner.data_index };

        if (owner.type == graphics::light::directional)
        {
            assert(index < _non_cullable_lights.size());
            _non_cullable_lights[index].Color = color;
        }
        else
        {
            // TODO: cullable lights
        }
    }

    constexpr bool is_enabled(light_id id) const
    {
        return _owners[id].is_enabled;
    }

    constexpr f32 intensity(light_id id) const
    {
        const light_owner& owner{ _owners[id] };
        const u32 index{ owner.data_index };
        if (owner.type == graphics::light::directional)
        {
            assert(index < _non_cullable_lights.size());
            return _non_cullable_lights[index].Intensity;
        }

        // TODO: cullable lights
        return 0.f;
    }

    constexpr math::v3 color(light_id id)
    {
        const light_owner& owner{ _owners[id] };
        const u32 index{ owner.data_index };
        if (owner.type == graphics::light::directional)
        {
            assert(index < _non_cullable_lights.size());
            return _non_cullable_lights[index].Color;
        }

        // TODO: cullable lights
        return {};
    }

    constexpr graphics::light::type type(light_id id) const
    {
        return _owners[id].type;
    }

    constexpr id::id_type entity_id(light_id id) const
    {
        return _owners[id].entity_id;
    }

    // Return the number of enabled directional lights
    CONSTEXPR u32 non_cullable_light_count() const
    {
        u32 count{ 0 };
        for (const auto& id : _non_cullable_owners)
        {
            if (id::is_valid(id) && _owners[id].is_enabled) ++count;
        }

        return count;
    }

    CONSTEXPR void non_cullable_lights(hlsl::DirectionalLightParameters *const lights, [[maybe_unused]] u32 buffer_size)
    {
        assert(buffer_size == math::align_size_up<D3D12_CONSTANT_BUFFER_DATA_PLACEMENT_ALIGNMENT>(non_cullable_light_count() * sizeof(hlsl::DirectionalLightParameters)));
        const u32 count{ (u32)_non_cullable_owners.size() };
        u32 index{ 0 };
        for (u32 i{ 0 }; i < count; ++i)
        {
            if (!id::is_valid(_non_cullable_owners[i])) continue;

            const light_owner& owner{ _owners[_non_cullable_owners[i]] };
            if (owner.is_enabled)
            {
                assert(_owners[_non_cullable_owners[i]].data_index == i);
                lights[index] = _non_cullable_lights[i];
                ++index;
            }
        }
    }

    constexpr bool has_lights() const
    {
        return _owners.size() > 0;
    }
private:

    // NOTE: these are NOT tightly packed
    utl::free_list<light_owner>                     _owners;
    utl::vector<hlsl::DirectionalLightParameters>   _non_cullable_lights;
    utl::vector<light_id>                           _non_cullable_owners;

};

class d3d12_light_buffer
{
public:
    d3d12_light_buffer() = default;
    CONSTEXPR void update_light_buffers(light_set& set, u64 light_set_key, u32 frame_index)
    {
        u32 sizes[light_buffer::count]{};
        sizes[light_buffer::non_cullable_light] = set.non_cullable_light_count() * sizeof(hlsl::DirectionalLightParameters);

        u32 current_sizes[light_buffer::count]{};
        current_sizes[light_buffer::non_cullable_light] = _buffers[light_buffer::non_cullable_light].buffer.size();

        if (current_sizes[light_buffer::non_cullable_light] < sizes[light_buffer::non_cullable_light])
        {
            resize_buffer(light_buffer::non_cullable_light, sizes[light_buffer::non_cullable_light], frame_index);
        }

        set.non_cullable_lights((hlsl::DirectionalLightParameters *const)_buffers[light_buffer::non_cullable_light].cpu_address,
                                _buffers[light_buffer::non_cullable_light].buffer.size());

        // TODO: cullable lights
    }

    constexpr void release()
    {
        for (u32 i{ 0 }; i < light_buffer::count; ++i)
        {
            _buffers[i].buffer.release();
            _buffers[i].cpu_address = nullptr;
        }
    }

    constexpr D3D12_GPU_VIRTUAL_ADDRESS non_cullable_lights() const { return _buffers[light_buffer::non_cullable_light].buffer.gpu_address(); }
private:
    struct light_buffer
    {
        enum type : u32 {
            non_cullable_light,
            cullable_light,
            culling_info,

            count
        };

        d3d12_buffer    buffer{};
        u8*             cpu_address{ nullptr };
    };

    void resize_buffer(light_buffer::type type, u32 size, [[maybe_unused]] u32 frame_index)
    {
        assert(type < light_buffer::count);
        if (!size) return;

        _buffers[type].buffer.release();
        _buffers[type].buffer = d3d12_buffer{ constant_buffer::get_default_init_info(size), true };
        NAME_D3D12_OBJECT_INDEXED(_buffers[type].buffer.buffer(), frame_index,
                                  type == light_buffer::non_cullable_light ? L"Non-cullable Light Buffer" :
                                  type == light_buffer::cullable_light ? L"Cullable Light Buffer" : L"Light Culling Info Buffer");

        D3D12_RANGE range{};
        DXCall(_buffers[type].buffer.buffer()->Map(0, &range, (void**)(&_buffers[type].cpu_address)));
        assert(_buffers[type].cpu_address);
    }

    light_buffer    _buffers[light_buffer::count];
    u64             _current_light_set_key{ 0 };
};
// Good boy!
#undef CONSTEXPR

std::unordered_map<u64, light_set>  light_sets;
d3d12_light_buffer                  light_buffers[frame_buffer_count];

constexpr void
set_is_enabled(light_set& set, light_id id, const void *const data, [[maybe_unused]] u32 size)
{
    bool is_enabled{ *(bool*)data };
    assert(sizeof(is_enabled) == size);
    set.enable(id, is_enabled);
}

constexpr void
set_intensity(light_set& set, light_id id, const void *const data, [[maybe_unused]] u32 size)
{
    f32 intensity{ *(f32*)data };
    assert(sizeof(intensity) == size);
    set.intensity(id, intensity);
}

constexpr void
set_color(light_set& set, light_id id, const void *const data, [[maybe_unused]] u32 size)
{
    math::v3 color{ *(math::v3*)data };
    assert(sizeof(color) == size);
    set.color(id, color);
}

constexpr void
get_is_enabled(light_set& set, light_id id, void *const data, [[maybe_unused]] u32 size)
{
    bool *const is_enabled{ (bool *const)data };
    assert(sizeof(bool) == size);
    *is_enabled = set.is_enabled(id);
}

constexpr void
get_intensity(light_set& set, light_id id, void *const data, [[maybe_unused]] u32 size)
{
    f32 *const intensity{ (f32 *const)data };
    assert(sizeof(f32) == size);
    *intensity = set.intensity(id);
}

constexpr void
get_color(light_set& set, light_id id, void *const data, [[maybe_unused]] u32 size)
{
    math::v3 *const color{ (math::v3 *const)data };
    assert(sizeof(math::v3) == size);
    *color = set.color(id);
}

constexpr void
get_type(light_set& set, light_id id, void *const data, [[maybe_unused]] u32 size)
{
    graphics::light::type *const type{ (graphics::light::type *const)data };
    assert(sizeof(graphics::light::type) == size);
    *type = set.type(id);
}

constexpr void
get_entity_id(light_set& set, light_id id, void *const data, [[maybe_unused]] u32 size)
{
    id::id_type *const entity_id{ (id::id_type *const)data };
    assert(sizeof(id::id_type) == size);
    *entity_id = set.entity_id(id);
}

constexpr void
dummy_set(light_set&, light_id, const void *const, u32)
{}

using set_function = void(*)(light_set&, light_id, const void *const, u32);
using get_function = void(*)(light_set&, light_id, void *const, u32);
constexpr set_function set_functions[]
{
    set_is_enabled,
    set_intensity,
    set_color,
    dummy_set,
    dummy_set,
};

static_assert(_countof(set_functions) == light_parameter::count);

constexpr get_function get_functions[]
{
    get_is_enabled,
    get_intensity,
    get_color,
    get_type,
    get_entity_id,
};

static_assert(_countof(get_functions) == light_parameter::count);

} // anonymous namespace

bool
initialize()
{
    return true;
}

void
shutdown()
{
    // make sure to remove all light before shutting down graphics.
    assert([] {
        bool has_lights{ false };
        for (const auto& it : light_sets)
        {
            has_lights |= it.second.has_lights();
        }
        return !has_lights;
           }());

    for (u32 i{ 0 }; i < frame_buffer_count; ++i)
    {
        light_buffers[i].release();
    }
}


graphics::light
create(light_init_info info)
{
    assert(id::is_valid(info.entity_id));
    return light_sets[info.light_set_key].add(info);
}

void
remove(light_id id, u64 light_set_key)
{
    assert(light_sets.count(light_set_key));
    light_sets[light_set_key].remove(id);
}

void
set_parameter(light_id id, u64 light_set_key, light_parameter::parameter parameter, const void *const data, u32 data_size)
{
    assert(data && data_size);
    assert(light_sets.count(light_set_key));
    assert(parameter < light_parameter::count && set_functions[parameter] != dummy_set);
    set_functions[parameter](light_sets[light_set_key], id, data, data_size);
}

void
get_parameter(light_id id, u64 light_set_key, light_parameter::parameter parameter, void *const data, u32 data_size)
{
    assert(data && data_size);
    assert(light_sets.count(light_set_key));
    assert(parameter < light_parameter::count);
    get_functions[parameter](light_sets[light_set_key], id, data, data_size);

}

void
update_light_buffers(const d3d12_frame_info& d3d12_info)
{
    const u64 light_set_key{ d3d12_info.info->light_set_key };
    assert(light_sets.count(light_set_key));
    light_set& set{ light_sets[light_set_key] };
    if (!set.has_lights()) return;

    set.update_transforms();
    const u32 frame_index{ d3d12_info.frame_index };
    d3d12_light_buffer& light_buffer{ light_buffers[frame_index] };
    light_buffer.update_light_buffers(set, light_set_key, frame_index);
}

D3D12_GPU_VIRTUAL_ADDRESS
non_cullable_light_buffer(u32 frame_index)
{
    const d3d12_light_buffer& light_buffer{ light_buffers[frame_index] };
    return light_buffer.non_cullable_lights();
}

u32
non_cullable_light_count(u64 light_set_key)
{
    assert(light_sets.count(light_set_key));
    return light_sets[light_set_key].non_cullable_light_count();
}

}