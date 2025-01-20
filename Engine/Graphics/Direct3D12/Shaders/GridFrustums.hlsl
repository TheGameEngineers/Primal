// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Common.hlsli"

ConstantBuffer<GlobalShaderData>                GlobalData      : register(b0, space0);
ConstantBuffer<LightCullingDispatchParameters>  ShaderParams    : register(b1, space0);
RWStructuredBuffer<Frustum>                     Frustums        : register(u0, space0);

// NOTE: TILE_SIZE is defined by the engine at compile-time.
[numthreads(TILE_SIZE, TILE_SIZE, 1)]
void ComputeGridFrustumsCS(uint3 DispatchThreadID : SV_DispatchThreadID)
{
    if (DispatchThreadID.x >= ShaderParams.NumThreads.x || DispatchThreadID.y >= ShaderParams.NumThreads.y) return;

    const float2 invViewDimensions = TILE_SIZE / float2(GlobalData.ViewWidth, GlobalData.ViewHeight);
    const float2 topLeft = DispatchThreadID.xy * invViewDimensions;
    const float2 center = topLeft + (invViewDimensions * 0.5f);

    float3 topLeftVS = UnprojectUV(topLeft, 0, GlobalData.InvProjection).xyz;
    float3 centerVS = UnprojectUV(center, 0, GlobalData.InvProjection).xyz;

    const float farClipRcp = -GlobalData.InvProjection._m33;
    Frustum frustum = { normalize(centerVS), distance(centerVS, topLeftVS) * farClipRcp };

    // Store the computed frustum in global memory for thread IDs that are in bounds of the grid.
    Frustums[DispatchThreadID.x + (DispatchThreadID.y * ShaderParams.NumThreads.x)] = frustum;
}