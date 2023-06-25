// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Common.hlsli"

ConstantBuffer<GlobalShaderData>                GlobalData      : register(b0, space0);
ConstantBuffer<LightCullingDispatchParameters>  ShaderParams    : register(b1, space0);
RWStructuredBuffer<Frustum>                     Frustums        : register(u0, space0);

#if USE_BOUNDING_SPHERES
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
#else
// Implementation of grid frustums shader is based on
// "Forward vs Deferred vs Forward+ Rendering with DirectX 11" (2015) by Jeremiah van Oosten.
// https://www.3dgep.com/forward-plus/#grid-frustums-compute-shader
// 
// NOTE: TILE_SIZE is defined by the engine at compile-time.
[numthreads(TILE_SIZE, TILE_SIZE, 1)]
void ComputeGridFrustumsCS(uint3 DispatchThreadID : SV_DispatchThreadID)
{
    const uint x = DispatchThreadID.x;
    const uint y = DispatchThreadID.y;

    // Return if our thread ID is not in bounds of the grid.
    if (x >= ShaderParams.NumThreads.x || y >= ShaderParams.NumThreads.y) return;

    // Compute the 4 corner points of the far clipping plane
    // to use as the frustum vertices.
    float4 screenSpace[4];
    screenSpace[0] = float4(float2(x, y) * TILE_SIZE, 0.f, 1.f);
    screenSpace[1] = float4(float2(x + 1, y) * TILE_SIZE, 0.f, 1.f);
    screenSpace[2] = float4(float2(x, y + 1) * TILE_SIZE, 0.f, 1.f);
    screenSpace[3] = float4(float2(x + 1, y + 1) * TILE_SIZE, 0.f, 1.f);

    const float2 invViewDimensions = 1.f / float2(GlobalData.ViewWidth, GlobalData.ViewHeight);
    float3 viewSpace[4];

    // Now convert the screen-space points to view space.
    viewSpace[0] = ScreenToView(screenSpace[0], invViewDimensions, GlobalData.InvProjection).xyz;
    viewSpace[1] = ScreenToView(screenSpace[1], invViewDimensions, GlobalData.InvProjection).xyz;
    viewSpace[2] = ScreenToView(screenSpace[2], invViewDimensions, GlobalData.InvProjection).xyz;
    viewSpace[3] = ScreenToView(screenSpace[3], invViewDimensions, GlobalData.InvProjection).xyz;

    // Build the frustum planes from the view space points.
    const float3 eyePos = (float3)0;
    Frustum frustum;
    // Left, right, top, bottom
    frustum.Planes[0] = ComputePlane(viewSpace[0], eyePos, viewSpace[2]);
    frustum.Planes[1] = ComputePlane(viewSpace[3], eyePos, viewSpace[1]);
    frustum.Planes[2] = ComputePlane(viewSpace[1], eyePos, viewSpace[0]);
    frustum.Planes[3] = ComputePlane(viewSpace[2], eyePos, viewSpace[3]);

    // Store the computed frustum in global memory for thread IDs that are in bounds of the grid.
    Frustums[x + (y * ShaderParams.NumThreads.x)] = frustum;
}
#endif