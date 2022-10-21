// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once

#include "Graphics/Direct3D12/D3D12CommonHeaders.h"

namespace primal::graphics::d3d12::hlsl {

using float4x4 = math::m4x4a;
using float4 = math::v4;
using float3 = math::v3;
using float2 = math::v2;
using uint4 = math::u32v4;
using uint3 = math::u32v3;
using uint2 = math::u32v2;
using uint = u32;

#include "CommonTypes.hlsli"
}