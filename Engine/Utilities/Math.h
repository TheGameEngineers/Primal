// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once

#include "CommonHeaders.h"

// platform specific headers
#if defined(_WIN64)
#include <DirectXMath.h>
#endif

#include "MathTypes.h"

namespace primal::math {

[[nodiscard]] constexpr bool
is_equal(f32 a, f32 b, f32 eps = epsilon)
{
    f32 diff{ a - b };
    if (diff < 0.f) diff = -diff;
    return diff < eps;
}

template<typename T>
[[nodiscard]] constexpr T
clamp(T value, T min, T max)
{
    assert(min <= max);
    return (value < min) ? min : (value > max) ? max : value;
}

template<u32 bits>
[[nodiscard]] constexpr u32
pack_unit_float(f32 f)
{
    static_assert(bits && bits <= sizeof(u32) * 8);
    assert(f >= 0.f && f <= 1.f);
    constexpr f32 intervals{ (f32)(((u32)1 << bits) - 1) };
    return (u32)(intervals * f + 0.5f);
}

template<u32 bits>
[[nodiscard]] constexpr f32
unpack_to_unit_float(u32 i)
{
    static_assert(bits && bits <= sizeof(u32) * 8);
    assert(i < ((u32)1 << bits));
    constexpr f32 intervals{ (f32)(((u32)1 << bits) - 1) };
    return (f32)i / intervals;
}

template<u32 bits>
[[nodiscard]] constexpr u32
pack_float(f32 f, f32 min, f32 max)
{
    assert(min < max);
    assert(f <= max && f >= min);
    const f32 distance{ (f - min) / (max - min) };
    return pack_unit_float<bits>(distance);
}

template<u32 bits>
[[nodiscard]] constexpr f32
unpack_to_float(u32 i, f32 min, f32 max)
{
    assert(min < max);
    return unpack_to_unit_float<bits>(i) * (max - min) + min;
}

// Align by rounding up. Will result in a multiple of 'alignment' that is greater than or equal to 'size'.
template<u64 alignment>
[[nodiscard]] constexpr u64
align_size_up(u64 size)
{
    static_assert(alignment, "Alignment must be non-zero.");
    constexpr u64 mask{ alignment - 1 };
    static_assert(!(alignment & mask), "Alignment should be a power of 2.");
    return ((size + mask) & ~mask);
}

// Align by rounding down. Will result in a multiple of 'alignment' that is less than or equal to 'size'.
template<u64 alignment>
[[nodiscard]] constexpr u64
align_size_down(u64 size)
{
    static_assert(alignment, "Alignment must be non-zero.");
    constexpr u64 mask{ alignment - 1 };
    static_assert(!(alignment & mask), "Alignment should be a power of 2.");
    return (size & ~mask);
}

// Align by rounding up. Will result in a multiple of 'alignment' that is greater than or equal to 'size'.
[[nodiscard]] constexpr u64
align_size_up(u64 size, u64 alignment)
{
    assert(alignment && "Alignment must be non-zero.");
    const u64 mask{ alignment - 1 };
    assert(!(alignment & mask) && "Alignment should be a power of 2.");
    return ((size + mask) & ~mask);
}

// Align by rounding down. Will result in a multiple of 'alignment' that is less than or equal to 'size'.
[[nodiscard]] constexpr u64
align_size_down(u64 size, u64 alignment)
{
    assert(alignment && "Alignment must be non-zero.");
    const u64 mask{ alignment - 1 };
    assert(!(alignment & mask) && "Alignment should be a power of 2.");
    return (size & ~mask);
}

namespace detail {

struct crc64_table {    
    u64 table[256]{};
    constexpr u64 operator[](u32 i) const { return table[i]; }
    constexpr u32 size() const { return 256; }
};

inline constexpr u64 crc64_ecma_polynomial{ 0x42F0E1EBA9EA3693 };

inline constexpr auto crc64_ecma_table{ []
{
    crc64_table table{};
    for (u32 i{ 0 }; i < table.size(); ++i)
    {
        u64 crc{ u64(i) << 56 };
        for (u32 bit{ 0 }; bit < 8; ++bit)
            crc = (crc & (u64(1) << 63)) ? (crc << 1) ^ crc64_ecma_polynomial : crc << 1;
        table.table[i] = crc;
    }
    return table;
}() };

}

[[nodiscard]] inline u64
crc64_ecma182(const u8 *const data, u64 size)
{
    assert(data || !size);

    u64 crc{ 0 };
    for (u64 i{ 0 }; i < size; ++i)
    {
        const u8 index{ u8((crc >> 56) ^ data[i]) };
        crc = detail::crc64_ecma_table[index] ^ (crc << 8);
    }

    return crc;
}

// TODO: _BitScan*() functions are Microsoft specific. 
[[nodiscard]] inline u8
log2(u64 value)
{
    unsigned long mssb; // most significant set bit
    unsigned long lssb; // least significant set bit

    // If perfect power of two (only one set bit), return index of bit. Otherwise round up
    // fractional log by adding 1 to most significant set bit's index.
    if (_BitScanReverse64(&mssb, value) > 0 && _BitScanForward64(&lssb, value) > 0)
        return u8(mssb + (mssb == lssb ? 0 : 1));
    else
        return 0;
}

constexpr math::v3 to_radians(math::v3 degrees)
{
    return { degrees.x * to_rad, degrees.y * to_rad, degrees.z * to_rad };
}

constexpr math::v3 to_degrees(math::v3 radians)
{
    return { radians.x * to_deg, radians.y * to_deg, radians.z * to_deg };
}

}