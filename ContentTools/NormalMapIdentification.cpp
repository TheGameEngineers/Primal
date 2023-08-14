// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "ToolsCommon.h"
#include <DirectXTex.h>

using namespace DirectX;

namespace primal::tools {
namespace {

constexpr f32 inv255{ 1.f / 255.f };
constexpr f32 min_avg_length_threshold{ 0.7f };
constexpr f32 max_avg_length_threshold{ 1.1f };
constexpr f32 min_avg_z_threshold{ 0.8f };
constexpr f32 vector_length_sq_rejection_threshold{ min_avg_length_threshold * min_avg_length_threshold };
constexpr f32 rejection_ratio_threshold{ 0.33f };

struct color
{
    f32 r, g, b, a;
    bool is_transparent() const { return a < 0.001f; }
    bool is_black() const { return r < 0.001f && g < 0.001f && b < 0.001f; }

    color operator+(color c)
    {
        r += c.r; g += c.g; b += c.b; a += c.a;
        return *this;
    }

    color operator+=(color c) { return (*this) + c; }

    color operator*(f32 s)
    {
        r *= s; g *= s; b *= s; a *= s;
        return *this;
    }

    color operator*=(f32 s) { return (*this) * s; }
    color operator/=(f32 s) { return (*this) * (1.f / s); }
};

using sampler = color(*)(const u8 *const);

color
sample_pixel_rgb(const u8 *const pixel)
{
    color c{ (f32)pixel[0], (f32)pixel[1], (f32)pixel[2], (f32)pixel[3] };
    return c * inv255;
}

color
sample_pixel_bgr(const u8 *const pixel)
{
    color c{ (f32)pixel[2], (f32)pixel[1], (f32)pixel[0], (f32)pixel[3] };
    return c * inv255;
}

s32
evaluate_color(color c)
{
    if (c.is_black() || c.is_transparent()) return 0;

    math::v3 v{ c.r * 2.f - 1.f, c.g * 2.f - 1.f, c.b * 2.f - 1.f };
    const f32 v_length_sq{ v.x * v.x + v.y * v.y + v.z * v.z };

    return (v.z < 0.f || v_length_sq < vector_length_sq_rejection_threshold) ? -1 : 1;
}

bool
evaluate_image(const Image *const image, sampler sample)
{
    constexpr u32 sample_count{ 4096 };
    const size_t image_size{ image->slicePitch };
    const size_t sample_interval{ std::max(image_size / sample_count, (size_t)4) };
    const u32 min_sample_count{ std::max((u32)(image_size / sample_interval) >> 2, (u32)1) };
    const u8 *const pixels{ image->pixels };

    u32 accepted_samples{ 0 };
    u32 rejected_samples{ 0 };
    color average_color{};

    size_t offset{ sample_interval };
    while (offset < image_size)
    {
        const color c{ sample(&pixels[offset]) };
        const s32 result{ evaluate_color(c) };
        if (result < 0)
        {
            ++rejected_samples;
        }
        else if (result > 0)
        {
            ++accepted_samples;
            average_color += c;
        }

        offset += sample_interval;
    }

    if (accepted_samples >= min_sample_count)
    {
        const f32 rejection_ratio{ (f32)rejected_samples / (f32)accepted_samples };
        if (rejection_ratio > rejection_ratio_threshold) return false;

        average_color /= (f32)accepted_samples;
        math::v3 v{ average_color.r * 2.f - 1.f, average_color.g * 2.f - 1.f, average_color.b * 2.f - 1.f };
        const f32 avg_length{ sqrt(v.x * v.x + v.y * v.y + v.z * v.z) };
        const f32 avg_normalized_z{ v.z / avg_length };

        return
            avg_length >= min_avg_length_threshold &&
            avg_length <= max_avg_length_threshold &&
            avg_normalized_z >= min_avg_z_threshold;
    }

    return false;
}

} // anonymous namespace

bool
is_normal_map(const Image *const image)
{
    const DXGI_FORMAT image_format{ image->format };
    if (BitsPerPixel(image_format) != 32 || BitsPerColor(image_format) != 8) return false;

    return evaluate_image(image, IsBGR(image_format) ? sample_pixel_bgr : sample_pixel_rgb);
}
}