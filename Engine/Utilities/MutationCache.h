// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "CommonHeaders.h"
#include <concepts>

namespace primal::utl {

namespace detail {
template <bool use_map>
struct mutation_cache_map_storage {};

template <>
struct mutation_cache_map_storage<true>
{
protected:
    std::unordered_map<id::id_type, u32> _cache_map;
};

template <typename T, typename U>
concept mutation_cache_entry =
    std::default_initializable<T> &&
    std::move_constructible<T> &&
    std::convertible_to<U, id::id_type> &&
    requires(T& entry, const T& existing, U value)
    {
        { id::is_valid(value) } -> std::same_as<bool>;
        { existing.id == value } -> std::convertible_to<bool>;
        entry.id = value;
    };
} // namespace detail

template <typename T, typename U, bool use_map = false> requires detail::mutation_cache_entry<T, U>
struct mutation_cache : detail::mutation_cache_map_storage<use_map>
{
    T* get_cache_ptr(U id)
    {
        assert(id::is_valid(id));

        if constexpr (use_map)
        {
            auto& map{ this->_cache_map };
            u32 index{ u32_invalid_id };
            auto pair = map.try_emplace(id, id::invalid_id);

            // cache_map didn't have an entry for this id, new entry inserted
            if (pair.second)
            {
                index = (u32)_cache_array.size();
                _cache_array.emplace_back();
                _cache_array.back().id = id;
                pair.first->second = index;
            }
            else
            {
                index = pair.first->second;
            }

            assert(index < _cache_array.size());
            return &_cache_array[index];
        }
        else
        {
            for (auto& cache : _cache_array)
                if (cache.id == id)
                    return &cache;

            _cache_array.emplace_back();
            _cache_array.back().id = id;
            return &_cache_array.back();
        }
    }

    void clear()
    {
        if constexpr (use_map) this->_cache_map.clear();
        _cache_array.clear();
    }

    void get_cache_array(T** array, u32* count)
    {
        assert(array && count);
        *array = _cache_array.data();
        *count = (u32)_cache_array.size();
    }

private:
    utl::vector<T> _cache_array{};
};

} // namespace primal::utl