// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Common.hlsli"

// NOTE: this constant is larger than max_lights_per_tile in light culling module (defined to be 256).
//       This is because 256 is the maximum for the *average* number of lights per tile, whereas
//       this constant is the maximum lights per tile.
static const uint       MaxLightsPerGroup = 1024;

groupshared uint        _minDepthVS;                        // tile's minimum depth in view-space.
groupshared uint        _maxDepthVS;                        // tile's maximum depth in view-space.
groupshared uint        _lightCount;                        // number of lights that affect pixels in this tile.
groupshared uint        _lightIndexStartOffset;             // offset in the global light index list where we copy _lightIndexList.
groupshared uint        _lightIndexList[MaxLightsPerGroup]; // indices of lights that affect this tile.

ConstantBuffer<GlobalShaderData>                GlobalData              : register(b0, space0);
ConstantBuffer<LightCullingDispatchParameters>  ShaderParams            : register(b1, space0);
StructuredBuffer<Frustum>                       Frustums                : register(t0, space0);
StructuredBuffer<LightCullingLightInfo>         Lights                  : register(t1, space0);

RWStructuredBuffer<uint>                        LightIndexCounter       : register(u0, space0);
RWStructuredBuffer<uint2>                       LightGrid_Opaque        : register(u1, space0);
RWStructuredBuffer<uint>                        LightIndexList_Opaque   : register(u3, space0);

// Implementation of light culling shader is based on
// "Forward vs Deferred vs Forward+ Rendering with DirectX 11" (2015) by Jeremiah van Oosten.
// https://www.3dgep.com/forward-plus/#light-culling
// 
// NOTE: TILE_SIZE is defined by the engine at compile-time.
[numthreads(TILE_SIZE, TILE_SIZE, 1)]
void CullLightsCS(ComputeShaderInput csIn)
{
    // INITIALIZATION SECTION
    if (csIn.GroupIndex == 0) // only the first thread in the group need to initialize groupshared memory
    {
        _minDepthVS = 0x7f7fffff; // FLT_MAX as uint
        _maxDepthVS = 0;
        _lightCount = 0;
    }

    uint i = 0, index = 0; // reusable index variables.

    // DEPTH MIN/MAX SECTION
    GroupMemoryBarrierWithGroupSync();

    const float depth = Texture2D(ResourceDescriptorHeap[ShaderParams.DepthBufferSrvIndex])[csIn.DispatchThreadID.xy].r;
    const float depthVS = ClipToView(float4(0.f, 0.f, depth, 1.f), GlobalData.InvProjection).z;
    // Negate depth because of right-handed coorinates (negative z-axis)
    // This make the comparisons easier to understand.
    const uint z = asuint(-depthVS);

    if (depth != 0) // Don't include far plane
    {
        InterlockedMin(_minDepthVS, z);
        InterlockedMax(_maxDepthVS, z);
    }

    // LIGHT CUILLING SECTION
    GroupMemoryBarrierWithGroupSync();

    const uint gridIndex = csIn.GroupID.x + (csIn.GroupID.y * ShaderParams.NumThreadGroups.x);
    const Frustum frustum = Frustums[gridIndex];
    // Negate view-space min/max again.
    const float minDepthVS = -asfloat(_minDepthVS);
    const float maxDepthVS = -asfloat(_maxDepthVS);

    for (i = csIn.GroupIndex; i < ShaderParams.NumLights; i += TILE_SIZE * TILE_SIZE)
    {
        const LightCullingLightInfo light = Lights[i];
        const float3 lightPositionVS = mul(GlobalData.View, float4(light.Position, 1.f)).xyz;

        if (light.Type == LIGHT_TYPE_POINT_LIGHT)
        {
            const Sphere sphere = { lightPositionVS, light.Range };
            if (SphereInsideFrustum(sphere, frustum, minDepthVS, maxDepthVS))
            {
                InterlockedAdd(_lightCount, 1, index);
                if (index < MaxLightsPerGroup) _lightIndexList[index] = i;
            }
        }
        else if (light.Type == LIGHT_TYPE_SPOTLIGHT)
        {
            const float3 lightDirectionVS = mul(GlobalData.View, float4(light.Direction, 0.f)).xyz;
            const Cone cone = { lightPositionVS, light.Range, lightDirectionVS, light.ConeRadius };
            if (ConeInsideFrustum(cone, frustum, minDepthVS, maxDepthVS))
            {
                InterlockedAdd(_lightCount, 1, index);
                if (index < MaxLightsPerGroup) _lightIndexList[index] = i;
            }
        }
    }

    // UPDATE LIGHT GRID SECTION
    GroupMemoryBarrierWithGroupSync();

    const uint lightCount = min(_lightCount, MaxLightsPerGroup - 1);

    if (csIn.GroupIndex == 0)
    {
        InterlockedAdd(LightIndexCounter[0], lightCount, _lightIndexStartOffset);
        LightGrid_Opaque[gridIndex] = uint2(_lightIndexStartOffset, lightCount);
    }

    // UPDATE LIGHT INDEX LIST SECTION
    GroupMemoryBarrierWithGroupSync();

    for (i = csIn.GroupIndex; i < lightCount; i += TILE_SIZE * TILE_SIZE)
    {
        LightIndexList_Opaque[_lightIndexStartOffset + i] = _lightIndexList[i];
    }
}