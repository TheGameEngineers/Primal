// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#if !defined(PRIMAL_COMMON_HLSLI) && !defined(__cplusplus)
#error Do not include this header directly in shader files. Only include this file via Common.hlsli.
#endif

// Compute a plane from 3 noncollinear points that form a triangle.
// This equation assumes a right-handed (counter-clockwise winding order) 
// coordinate system to determine the direction of the plane normal.
Plane ComputePlane(float3 p0, float3 p1, float3 p2)
{
    Plane plane;

    const float3 v0 = p1 - p0;
    const float3 v2 = p2 - p0;

    plane.Normal = normalize(cross(v0, v2));
    plane.Distance = dot(plane.Normal, p0);

    return plane;
}

// Check to see if a point is fully behind (inside the negative halfspace of) a plane.
bool PointInsidePlane(float3 p, Plane plane)
{
    return dot(plane.Normal, p) - plane.Distance < 0;
}

// Check to see if a sphere is fully behind (inside the negative halfspace of) a plane.
// Source: Real-time collision detection, Christer Ericson (2005)
bool SphereInsidePlane(Sphere sphere, Plane plane)
{
    return dot(plane.Normal, sphere.Center) - plane.Distance < -sphere.Radius;
}

// Check to see if a cone if fully behind (inside the negative halfspace of) a plane.
// Source: Real-time collision detection, Christer Ericson (2005)
bool ConeInsidePlane(Cone cone, Plane plane)
{
    // Compute the farthest point on the end of the cone to the positive space of the plane.
    float3 m = cross(cross(plane.Normal, cone.Direction), cone.Direction);
    float3 Q = cone.Tip + cone.Direction * cone.Height - m * cone.Radius;

    // The cone is in the negative halfspace of the plane if both
    // the tip of the cone and the farthest point on the end of the cone to the 
    // positive halfspace of the plane are both inside the negative halfspace 
    // of the plane.
    return PointInsidePlane(cone.Tip, plane) && PointInsidePlane(Q, plane);
}

// Converts a normalized screen-space position to a 3D coordinate depending on "inverse" parameter.
// uv is screen-space uv coordinate of the pixel
// depth is z-buffer depth
// inverse parameter can be
//      - inverse projection            -> screen to view-space
//      - inverse view_projection       -> screen to world-space
//      - inverse world-view_projection -> screen to object-space

float4 UnprojectUV(float2 uv, float depth, float4x4 inverse)
{
    // Convert to clip space
    float4 clip = float4(float2(uv.x, 1.f - uv.y) * 2.f - 1.f, depth, 1.f);

    // 3D-space position (before perspective divide).
    float4 position = mul(inverse, clip);

    // Divide by w to get the 3D-space position.
    return position / position.w;
}

float4 ClipToView(float4 clip, float4x4 inverseProjection)
{
    // View space position
    float4 view = mul(inverseProjection, clip);
    // Perspective (un)projection
    return view / view.w;
}

float4 ScreenToView(float4 screen, float2 invViewDimensions, float4x4 inverseProjection)
{
    // Convert to normalized texture coordinates
    float2 texCoord = screen.xy * invViewDimensions;

    // Convert to clip space
    float4 clip = float4(float2(texCoord.x, 1.f - texCoord.y) * 2.f - 1.f, screen.z, screen.w);

    return ClipToView(clip, inverseProjection);
}