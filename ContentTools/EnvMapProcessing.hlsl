// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
const static float PI = 3.1415926535897932f;
const static float SAMPLE_OFFSET = 0.5f;


cbuffer Constants : register(b0)
{
    uint  g_CubeMapInSize;
    uint  g_CubeMapOutSize;
    uint  g_SampleCount;
    float g_Roughness;
};

Texture2D<float4>               EnvMap                  : register(t0);
TextureCube<float4>             CubeMapIn               : register(t0);

RWTexture2DArray<float4>        Output                  : register(u0);

SamplerState                    LinearSampler           : register(s0);

float RadicalInverse_VdC(uint bits)
{
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    return float(bits) * 2.3283064365386963e-10; // / 0x100000000
}
// ----------------------------------------------------------------------------
float2 Hammersley(uint i, uint N)
{
    return float2(float(i) / float(N), RadicalInverse_VdC(i));
}

float Pow4(float x)
{
    float xx = x * x;
    return xx * xx;
}

float3 GetSampleDirectionEquirectangular(uint face, float x, float y)
{
    float3 direction[6] = {
        {-x,    1.f, -y},   // X+ Left
        { x,   -1.f, -y},   // X- Right
        { y,    x,    1.f}, // Y+ Bottom
        {-y,    x,   -1.f}, // Y- Top
        { 1.f,  x,   -y},   // Z+ Front
        {-1.f, -x,   -y},   // Z- Back
    };

    return normalize(direction[face]);
}

float3 GetSampleDirectionCubemap(uint face, float x, float y)
{
    float3 direction[6] = {
        { 1.f, -y,   -x},   // X+ Left
        {-1.f, -y,    x},   // X- Right
        { x,    1.f,  y},   // Y+ Bottom
        { x,   -1.f, -y},   // Y- Top
        { x,   -y,    1.f}, // Z+ Front
        {-x,   -y,   -1.f}, // Z- Back
    };

    return normalize(direction[face]);
}

float2 DirectionToEquirectangularUV(float3 dir)
{
    float Phi = atan2(dir.y, dir.x);
    float Theta = acos(dir.z);
    float u = Phi * (0.5f / PI) + 0.5f;
    float v = Theta * (1.f / PI);
    return float2(u, v);
}

// GGX / Trowbridge-Reitz
// [Walter et al. 2007, "Microfacet models for refraction through rough surfaces"]
float D_GGX(float NoH, float a)
{
    float d = (NoH * a - NoH) * NoH + 1.f;
    return a / (PI * d * d);
}

float3x3 GetTangentFrame(float3 normal)
{
    float3 up = abs(normal.z) < 0.999f ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 tangentX = normalize(cross(up, normal));
    float3 tangentY = cross(normal, tangentX);

    return float3x3(tangentX, tangentY, normal);
}

float3 ImportanceSampleGGX(float2 E, float a)
{
    float Phi = 2.f * PI * E.x;
    float CosTheta = sqrt((1.f - E.y) / (1.f + (a - 1.f) * E.y));
    float SinTheta = sqrt(1.f - CosTheta * CosTheta);

    float3 H;
    H.x = SinTheta * cos(Phi);
    H.y = SinTheta * sin(Phi);
    H.z = CosTheta;

    return H;
}

float3 PrefilterEnvMap(float roughness, float3 R)
{
    float a4 = Pow4(roughness);
    float3 N = R;
    float3 V = R;

    float3 PrefilteredColor = 0;
    float TotalWeight = 0;
    uint NumSamples = g_SampleCount;
    float resolution = g_CubeMapInSize; // resolution of source cubemap (per face)
    float invSaTexel = (6.f * resolution * resolution) * (1.f / 4.f * PI);
    float mipLevel = 0;
    float3x3 tangentFrame = GetTangentFrame(N);

    for (uint i = 0; i < NumSamples; i++)
    {
        float2 Xi = Hammersley(i, NumSamples);
        float3 H = mul(ImportanceSampleGGX(Xi, a4), tangentFrame);
        float3 L = 2 * dot(V, H) * H - V;
        float NoL = saturate(dot(N, L));

        if (NoL > 0)
        {
#if 1       
            float NoH = max(dot(N, H), 0.f);
            float HoV = max(dot(H, V), 0.f);
            float D = D_GGX(NoH, a4);
            float pdf = D * NoH / (4.f * HoV + 0.0001f);
            float saSample = 1.f / (float(NumSamples) * pdf + 0.0001f);

            mipLevel = roughness == 0.f ? 0.f : 0.5f * log2(saSample * invSaTexel);
#endif

            PrefilteredColor += CubeMapIn.SampleLevel(LinearSampler, L, mipLevel).rgb * NoL;
            TotalWeight += NoL;
        }
    }
    return PrefilteredColor / TotalWeight;
}

float3 SampleHemisphereDiscrete(float3 normal)
{
    float3 N = normal;
    float3 irradiance = 0.f;
    float3x3 tangentFrame = GetTangentFrame(N);

    float delta = 0.02f;
    uint sampleCount = 0;

    for (float phi = 0; phi < 2 * PI; phi += delta)
    {
        float sinPhi = sin(phi);
        float cosPhi = cos(phi);

        for (float theta = 0; theta < 0.5 * PI; theta += delta)
        {
            float sinTheta = sin(theta);
            float cosTheta = cos(theta);
            float3 transform = float3(sinTheta * cosPhi, sinTheta * sinPhi, cosTheta);
            float3 sampleDir = mul(transform, tangentFrame);

            irradiance += CubeMapIn.SampleLevel(LinearSampler, sampleDir, 0).rgb * cosTheta * sinTheta;

            ++sampleCount;
        }
    }

    irradiance *= PI / float(sampleCount);
    return irradiance;
}

float3 SampleHemisphereRandom(float3 normal)
{
    float3 irradiance = 0.f;
    float3x3 tangentFrame = GetTangentFrame(normal);
    uint sampleCount = g_SampleCount;

    for (uint i = 0; i < sampleCount; ++i)
    {
        float2 Xi = Hammersley(i, sampleCount);
        float phi = 2.f * PI * Xi.x;
        float sinTheta = sqrt(Xi.y);
        float cosTheta = sqrt(1.f - Xi.y);
        float sinPhi = sin(phi);
        float cosPhi = cos(phi);

        float3 transform = float3(sinTheta * cosPhi, sinTheta * sinPhi, cosTheta);
        float3 sampleDir = mul(transform, tangentFrame);

        irradiance += CubeMapIn.SampleLevel(LinearSampler, sampleDir, 0).rgb;
    }

    irradiance *= (1.f / float(sampleCount));
    return irradiance;
}

float3 SampleHemisphereBrute(float3 normal)
{
    float3 irradiance = 0.f;
    float sampleCount = 0.f;
    float invDim = 1.f / g_CubeMapInSize;

    for (uint face = 0; face < 6; ++face)
        for (uint y = 0; y < g_CubeMapInSize; ++y)
            for (uint x = 0; x < g_CubeMapInSize; ++x)
            {
                float2 uv = (float2(x, y) + SAMPLE_OFFSET) * invDim;
                float2 pos = 2.f * uv - 1.f;

                float3 sampleDir = GetSampleDirectionCubemap(face, pos.x, pos.y);
                float cosTheta = dot(sampleDir, normal);
                if (cosTheta > 0.f)
                {
                    float tmp = 1.f + pos.x * pos.x + pos.y * pos.y;
                    float weight = 4.f * cosTheta / (sqrt(tmp) * tmp);
                    irradiance += CubeMapIn.SampleLevel(LinearSampler, sampleDir, 0).rgb * weight;
                    sampleCount += weight;
                }
            }

    irradiance *= 1.f / sampleCount;
    return irradiance;
}

[numthreads(16, 16, 1)]
void EquirectangularToCubeMapCS(uint3 DispatchThreadID : SV_DispatchThreadID, uint3 GroupID : SV_GroupID)
{
    uint face = GroupID.z;
    uint size = g_CubeMapOutSize;

    if (DispatchThreadID.x >= size || DispatchThreadID.y >= size || face >= 6) return;

    float2 uv = (float2(DispatchThreadID.xy) + SAMPLE_OFFSET) / size;
    float2 pos = 2.f * uv - 1.f;
    float3 sampleDirection = GetSampleDirectionEquirectangular(face, pos.x, pos.y);
    float2 dir = DirectionToEquirectangularUV(sampleDirection);
    // Misusing sampleCount as a toggle for mirroring the cubemap.
    if(g_SampleCount == 1) dir.x = 1.f - dir.x;
    float4 envMapSample = EnvMap.SampleLevel(LinearSampler, dir, 0);

    Output[uint3(DispatchThreadID.x, DispatchThreadID.y, face)] = envMapSample;
}

[numthreads(16, 16, 1)]
void PrefilterDiffuseEnvMapCS(uint3 DispatchThreadID : SV_DispatchThreadID, uint3 GroupID : SV_GroupID)
{
    uint face = GroupID.z;
    uint size = g_CubeMapOutSize;

    if (DispatchThreadID.x >= size || DispatchThreadID.y >= size || face >= 6) return;

    float2 uv = (float2(DispatchThreadID.xy) + SAMPLE_OFFSET) / size;
    float2 pos = 2.f * uv - 1.f;
    float3 sampleDirection = GetSampleDirectionCubemap(face, pos.x, pos.y);
    float3 irradiance = SampleHemisphereBrute(sampleDirection);
    //float3 irradiance = SampleHemisphereRandom(sampleDirection);
    //float3 irradiance = SampleHemisphereDiscrete(sampleDirection);

    Output[uint3(DispatchThreadID.x, DispatchThreadID.y, face)] = float4(irradiance, 1.f);
}

[numthreads(16, 16, 1)]
void PrefilterSpecularEnvMapCS(uint3 DispatchThreadID : SV_DispatchThreadID, uint3 GroupID : SV_GroupID)
{
    uint face = GroupID.z;
    uint size = g_CubeMapOutSize;

    if (DispatchThreadID.x >= size || DispatchThreadID.y >= size || face >= 6) return;

    float2 uv = (float2(DispatchThreadID.xy) + SAMPLE_OFFSET) / size;
    float2 pos = 2.f * uv - 1.f;
    float3 sampleDirection = GetSampleDirectionCubemap(face, pos.x, pos.y);
    float3 irradiance = PrefilterEnvMap(g_Roughness, sampleDirection);

    Output[uint3(DispatchThreadID.x, DispatchThreadID.y, face)] = float4(irradiance, 1.f);
}