// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "CommonHeaders.h"

namespace primal::utl {

// A vector class similar to std::vector with basic functionality.
// The user can specify in the template argument whether they want
// elements' destructor to be called when being removed or while
// clearing/destructing the vector.
template<typename T, bool destruct = true>
class vector
{
public:
    // Default constructor. Doesn't allocate memory.
    constexpr vector() = default;

    // Constructor resizes the vector and initializes 'count' items.
    constexpr explicit vector(u64 count)
    {
        resize(count);
    }

    // Constructor resizes the vector and initializes 'count' items using 'value'.
    constexpr explicit vector(u64 count, const T& value)
    {
        resize(count, value);
    }

    // Copy-constructor. Constructs by copying another vector. The items
    // in the copied vector must be copyable.
    constexpr vector(const vector& o) requires(destruct)
    {
        *this = o;
    }

    // Move-constructor. Constructs by moving another vector.
    // The original vector will be empty after move.
    constexpr vector(vector&& o)
        : _capacity{ o._capacity }, _size{ o._size }, _data{ o._data }
    {
        o.reset();
    }

    // Copy-assignment operator. Clears this vector and copies items
    // from another vector. The items must be copyable.
    constexpr vector& operator=(const vector& o) requires(destruct)
    {
        assert(this != std::addressof(o));
        if (this != std::addressof(o))
        {
            clear();
            reserve(o._size);
            for (const auto& item : o)
            {
                emplace_back(item);
            }
            assert(_size == o._size);
        }

        return *this;
    }

    // Move-assignment operator. Frees all resources in this vector and
    // moves the other vector into this one.
    constexpr vector& operator=(vector&& o)
    {
        assert(this != std::addressof(o));
        if (this != std::addressof(o))
        {
            destroy();
            move(o);
        }

        return *this;
    }

    // Destructs the vector and its items as specified in template argument
    ~vector() { destroy(); }

    // Inserts an item at the end of the vector by copying 'value'.
    constexpr void push_back(const T& value)
    {
        emplace_back(value);
    }

    // Inserts an item at the end of the vector by moving 'value'.
    constexpr void push_back(T&& value)
    {
        emplace_back(std::move(value));
    }

    // Copy- or move-constructs an item at the end of the vector.
    template<typename... params>
    constexpr decltype(auto) emplace_back(params&&... p)
    {
        if (_size == _capacity)
        {
            const u64 new_capacity{ ((_capacity + 1) * 3) >> 1 }; // reserve 50% more
            // This is to make sure that we can add an element that is already in the vector,
            // effectively appending it to the vector by copying/moving it.
            if (_size)
            {
                T value(std::forward<params>(p)...);
                reserve(new_capacity);
                T *const item{ new (std::addressof(_data[_size])) T(std::move(value)) };
                ++_size;
                return *item;
            }

            reserve(new_capacity);
        }
        assert(_size < _capacity);

        T *const item{ new (std::addressof(_data[_size])) T(std::forward<params>(p)...) };
        ++_size;
        return *item;
    }

    // Resizes the vector and initializes new items with their default value.
    constexpr void resize(u64 new_size)
    {
        static_assert(std::is_default_constructible_v<T>,
            "Type must be default-constructible.");

        if (new_size > _size)
        {
            reserve(new_size);
            while (_size < new_size)
            {
                emplace_back();
            }
        }
        else if (new_size < _size)
        {
            if constexpr (destruct)
            {
                destruct_range(new_size, _size);
            }

            _size = new_size;
        }

        // Do nothing if new_size == _size.
        assert(new_size == _size);
    }

    // Resizes the vector and initializes new items by copying 'value'.
    constexpr void resize(u64 new_size, const T& value)
    {
        static_assert(std::is_copy_constructible_v<T>,
            "Type must be copy-constructible.");

        if (new_size > _size)
        {
            if (new_size > _capacity)
            {
                T value_copy(value);
                reserve(new_size);
                while (_size < new_size)
                {
                    emplace_back(value_copy);
                }
            }
            else
            {
                while (_size < new_size)
                {
                    emplace_back(value);
                }
            }
        }
        else if (new_size < _size)
        {
            if constexpr (destruct)
            {
                destruct_range(new_size, _size);
            }

            _size = new_size;
        }

        // Do nothing if new_size == _size.
        assert(new_size == _size);
    }

    // Allocates memory to contain the specified number of items.
    constexpr void reserve(u64 new_capacity)
    {
        if (new_capacity > _capacity)
        {
            if constexpr (std::is_trivially_copyable_v<T>)
            {
                // NOTE: realoc() will automatically copy the data in the buffer
                //       if a new region of memory is allocated.
                void* new_buffer{ realloc(_data, new_capacity * sizeof(T)) };
                assert(new_buffer);
                if (new_buffer)
                {
                    _data = static_cast<T*>(new_buffer);
                    _capacity = new_capacity;
                }
            }
            else
            {
                void* new_buffer{ malloc(new_capacity * sizeof(T)) };
                assert(new_buffer);
                if (new_buffer)
                {
                    T* const new_data{ static_cast<T*>(new_buffer) };
                    for (u64 i{0}; i < _size; ++i)
                    {
                        new (std::addressof(new_data[i])) T(std::move(_data[i]));
                    }

                    if constexpr (destruct)
                    {
                        destruct_range(0, _size);
                    }

                    free(_data);
                    _data = new_data;
                    _capacity = new_capacity;
                }
            }
        }
    }

    // Removes the item at specified index.
    constexpr T *const erase(u64 index)
    {
        assert(_data && index < _size);
        return erase(std::addressof(_data[index]));
    }

    // Removes the item at specified location.
    constexpr T *const erase(T *const item)
    {
        assert(_data && item >= std::addressof(_data[0]) &&
            item < std::addressof(_data[_size]));
        T* const last{ std::addressof(_data[_size - 1]) };
        for (T* current{ item }; current < last; ++current)
        {
            *current = std::move(*(current + 1));
        }
        if constexpr (destruct) last->~T();
        --_size;

        return item;
    }

    // Same as erase() but faster because it just copies the last item.
    constexpr T *const erase_unordered(u64 index)
    {
        assert(_data && index < _size);
        return erase_unordered(std::addressof(_data[index]));
    }

    // Same as erase() but faster because it just copies the last item.
    constexpr T *const erase_unordered(T *const item)
    {
        assert(_data && item >= std::addressof(_data[0]) &&
            item < std::addressof(_data[_size]));
        T* const last{ std::addressof(_data[_size - 1]) };
        if (item != last)
        {
            *item = std::move(*last);
        }
        if constexpr (destruct) last->~T();
        --_size;

        return item;
    }

    // Clears the vector and destructs items as specified in template argument.
    constexpr void clear()
    {
        if constexpr (destruct)
        {
            destruct_range(0, _size);
        }
        _size = 0;
    }

    // Swaps two vectors
    constexpr void swap(vector& o)
    {
        if (this != std::addressof(o))
        {
            auto temp(std::move(o));
            o.move(*this);
            move(temp);
        }
    }

    // Pointer to the start of data. Might be null.
    [[nodiscard]] constexpr T* data()
    {
        return _data;
    }

    // Pointer to the start of data. Might be null.
    [[nodiscard]] constexpr const T* data() const
    {
        return _data;
    }

    // Returns true if vector is empty.
    [[nodiscard]] constexpr bool empty() const
    {
        return _size == 0;
    }

    // Return the number of items in the vector.
    [[nodiscard]] constexpr u64 size() const
    {
        return _size;
    }

    // Returns the current capacity of the vector.
    [[nodiscard]] constexpr u64 capacity() const
    {
        return _capacity;
    }

    // Indexing operator. Returns a reference to the item at specified index.
    [[nodiscard]] constexpr T& operator[](u64 index)
    {
        assert(_data && index < _size);
        return _data[index];
    }


    // Indexing operator. Returns a constant reference to the item at specified index.
    [[nodiscard]] constexpr const T& operator[](u64 index) const
    {
        assert(_data && index < _size);
        return _data[index];
    }

    // Returns a reference to the first item. Will fault the application if called
    // when the vector is empty.
    [[nodiscard]] constexpr T& front()
    {
        assert(_data && _size);
        return _data[0];
    }

    // Returns a constant reference to the first item. Will fault the application
    //  if called when the vector is empty.
    [[nodiscard]] constexpr const T& front() const
    {
        assert(_data && _size);
        return _data[0];
    }

    // Returns a reference to the last item. Will fault the application if called
    // when the vector is empty.
    [[nodiscard]] constexpr T& back()
    {
        assert(_data && _size);
        return _data[_size - 1];
    }

    // Returns a constant reference to the last item. Will fault the application
    //  if called when the vector is empty.
    [[nodiscard]] constexpr const T& back() const
    {
        assert(_data && _size);
        return _data[_size - 1];
    }

    // Returns a pointer to the first item. Returns null when vector is empty.
    [[nodiscard]] constexpr T* begin()
    {
        return _data;
    }

    // Returns a constant pointer to the first item. Returns null when vector is empty.
    [[nodiscard]] constexpr const T* begin() const
    {
        return _data;
    }

    // Returns a pointer to the last item. Returns null when vector is empty.
    [[nodiscard]] constexpr T* end()
    {
        assert(!(_data == nullptr && _size > 0));
        return _data ? std::addressof(_data[_size]) : nullptr;
    }

    // Returns a constant pointer to the last item. Returns null when vector is empty.
    [[nodiscard]] constexpr const T* end() const
    {
        assert(!(_data == nullptr && _size > 0));
        return _data ? std::addressof(_data[_size]) : nullptr;
    }

private:
    constexpr void move(vector& o)
    {
        _capacity = o._capacity;
        _size = o._size;
        _data = o._data;
        o.reset();
    }

    constexpr void reset()
    {
        _capacity = 0;
        _size = 0;
        _data = nullptr;
    }

    constexpr void destruct_range(u64 first, u64 last)
    {
        assert(destruct);
        assert(first <= _size && last <= _size && first <= last);
        if (_data)
        {
            for (; first != last; ++first)
            {
                _data[first].~T();
            }
        }
    }

    constexpr void destroy()
    {
        assert([&] {return _capacity ? _data != nullptr : _data == nullptr; }());
        clear();
        _capacity = 0;
        if (_data) free(_data);
        _data = nullptr;
    }

    u64 _capacity{ 0 };
    u64 _size{ 0 };
    T*  _data{ nullptr };
};
}
