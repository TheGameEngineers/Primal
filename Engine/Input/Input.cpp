// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Input.h"

namespace primal::input {
namespace {

struct input_binding
{
    utl::vector<input_source>   sources;
    input_value                 value{};
    bool                        is_dirty{ true };
};

std::unordered_map<u64, input_value>    input_values;
std::unordered_map<u64, input_binding>  input_bindings;
std::unordered_map<u64, u64>            source_binding_map;
utl::vector<detail::input_system_base*> input_callbacks;

constexpr u64
get_key(input_source::type type, u32 code)
{
    return ((u64)type << 32) | (u64)code;
}

} // anonymous namespace

void
bind(input_source source)
{
    assert(source.source_type < input_source::count);
    const u64 key{ get_key(source.source_type, source.code) };
    unbind(source.source_type, (input_code::code)source.code);

    input_bindings[source.binding].sources.emplace_back(source);
    source_binding_map[key] = source.binding;
}

void
unbind(input_source::type type, input_code::code code)
{
    assert(type < input_source::count);
    const u64 key{ get_key(type, code) };
    if (!source_binding_map.count(key))
    {
        return;
    }

    const u64 binding_key{ source_binding_map[key] };
    assert(input_bindings.count(binding_key));
    input_binding& binding{ input_bindings[binding_key] };
    utl::vector<input_source>& sources{ binding.sources };
    for (u32 i{ 0 }; i < sources.size(); ++i)
    {
        if (sources[i].source_type == type && sources[i].code == code)
        {
            assert(sources[i].binding == source_binding_map[key]);
            utl::erase_unordered(sources, i);
            source_binding_map.erase(key);
            break;
        }
    }

    if (!sources.size())
    {
        assert(!source_binding_map.count(key));
        input_bindings.erase(binding_key);
    }
}

void
unbind(u64 binding)
{
    if (!input_bindings.count(binding))
    {
        return;
    }

    utl::vector<input_source>& sources{ input_bindings[binding].sources };
    for (const auto& source : sources)
    {
        assert(source.binding == binding);
        const u64 key{ get_key(source.source_type, source.code) };
        assert(source_binding_map.count(key) && source_binding_map[key] == binding);
        source_binding_map.erase(key);
    }

    input_bindings.erase(binding);
}

void
set(input_source::type type, input_code::code code, math::v3 value)
{
    assert(type < input_source::count);
    const u64 key{ get_key(type, code) };
    input_value& input{ input_values[key] };
    input.previous = input.current;
    input.current = value;

    if (source_binding_map.count(key))
    {
        const u64 binding_key{ source_binding_map[key] };
        assert(input_bindings.count(binding_key));
        input_binding& binding{ input_bindings[binding_key] };
        binding.is_dirty = true;

        input_value binding_value;
        get(binding_key, binding_value);

        // TODO: these callbacks could cause data-races in scripts when not run on the same thread as game scripts
        for (const auto& c : input_callbacks)
        {
            c->on_event(binding_key, binding_value);
        }
    }

    // TODO: these callbacks could cause data-races in scripts when not run on the same thread as game scripts
    for (const auto& c : input_callbacks)
    {
        c->on_event(type, code, input);
    }
}

void
get(input_source::type type, input_code::code code, input_value& value)
{
    assert(type < input_source::count);
    const u64 key{ get_key(type, code) };
    value = input_values[key];
}

void
get(u64 binding, input_value& value)
{
    if (!input_bindings.count(binding))
    {
        return;
    }

    input_binding& input_binding{ input_bindings[binding] };

    if (!input_binding.is_dirty)
    {
        value = input_binding.value;
        return;
    }

    utl::vector<input_source>& sources{ input_binding.sources };
    input_value sub_value{};
    input_value result{};

    for (const auto& source : sources)
    {
        assert(source.binding == binding);
        get(source.source_type, (input_code::code)source.code, sub_value);
        assert(source.axis <= axis::z);
        if (source.source_type == input_source::mouse)
        {
            const f32 current{ (&sub_value.current.x)[source.source_axis] };
            const f32 previous{ (&sub_value.previous.x)[source.source_axis] };
            (&result.current.x)[source.axis] += (current - previous) * source.multiplier;
        }
        else
        {
            (&result.previous.x)[source.axis] += (&sub_value.previous.x)[source.source_axis] * source.multiplier;
            (&result.current.x)[source.axis] += (&sub_value.current.x)[source.source_axis] * source.multiplier;
        }
    }

    input_binding.value = result;
    input_binding.is_dirty = false;
    value = result;
}

detail::input_system_base::input_system_base()
{
    input_callbacks.emplace_back(this);
}

detail::input_system_base::~input_system_base()
{
    for (u32 i{ 0 }; i < input_callbacks.size(); ++i)
    {
        if (input_callbacks[i] == this)
        {
            utl::erase_unordered(input_callbacks, i);
            break;
        }
    }
}
}