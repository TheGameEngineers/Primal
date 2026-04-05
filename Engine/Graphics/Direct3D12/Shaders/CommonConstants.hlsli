// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#if !defined(PRIMAL_COMMON_HLSLI) && !defined(__cplusplus)
#error Do not include this header directly in shader files. Only include this file via Common.hlsli.
#endif

static const float PI = 3.1415926535897932384626433832795f;

// Material constants
static const float MaxEmissiveIntensity = 10000.f;

// Light types
// NOTE: these to be the same as primal::graphics::light::type enumeration!
static const uint LIGHT_TYPE_DIRECTIONAL_LIGHT = 0;
static const uint LIGHT_TYPE_POINT_LIGHT = 1;
static const uint LIGHT_TYPE_SPOTLIGHT = 2;