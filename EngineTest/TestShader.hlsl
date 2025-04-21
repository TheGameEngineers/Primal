// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Common.hlsli"
#include "BRDF.hlsli"

struct VertexOut
{
    float4 HomogeneousPosition  : SV_POSITION;
    float3 WorldPosition        : POSITION;
    float3 WorldNormal          : NORMAL;
    float4 WorldTangent         : TANGENT;
    float2 UV                   : TEXTURE;
};

struct PixelOut
{
    float4 Color                : SV_TARGET0;
};

struct Surface
{
    float3 BaseColor;
    float  Metallic;
    float3 Normal;
    float  PerceptualRoughness;
    float3 EmissiveColor;
    float  EmissiveIntensity;
    float3 V;                   // View direction
    float  AmbientOcclusion;
    float3 DiffuseColor;
    float  a2;                  // = Pow(PerceptualRoughness, 4)
    float3 SpecularColor;
    float  NoV;
    float  SpecularStrength;
};

#define ElementsTypePositionOnly                0x00
#define ElementsTypeStaticNormal                0x01
#define ElementsTypeStaticNormalTexture         0x03
#define ElementsTypeStaticColor                 0x04
#define ElementsTypeSkeletal                    0x08
#define ElementsTypeSkeletalColor               ElementsTypeSkeletal                | ElementsTypeStaticColor
#define ElementsTypeSkeletalNormal              ElementsTypeSkeletal                | ElementsTypeStaticNormal
#define ElementsTypeSkeletalNormalColor         ElementsTypeSkeletalNormal          | ElementsTypeStaticColor
#define ElementsTypeSkeletalNormalTexture       ElementsTypeSkeletal                | ElementsTypeStaticNormalTexture
#define ElementsTypeSkeletalNormalTextureColor  ElementsTypeSkeletalNormalTexture   | ElementsTypeStaticColor

struct VertexElement
{
#if ELEMENTS_TYPE == ElementsTypeStaticNormal
    uint        ColorTSign;
    uint16_t2   Normal;
#elif ELEMENTS_TYPE == ElementsTypeStaticNormalTexture
    uint        ColorTSign;
    uint16_t2   Normal;
    uint16_t2   Tangent;
    float2      UV;
#elif ELEMENTS_TYPE == ElementsTypeStaticColor
#elif ELEMENTS_TYPE == ElementsTypeSkeletal
#elif ELEMENTS_TYPE == ElementsTypeSkeletalColor
#elif ELEMENTS_TYPE == ElementsTypeSkeletalNormal
#elif ELEMENTS_TYPE == ElementsTypeSkeletalNormalColor
#elif ELEMENTS_TYPE == ElementsTypeSkeletalNormalTexture
#elif ELEMENTS_TYPE == ElementsTypeSkeletalNormalTextureColor
#endif
};

const static float InvIntervals = 2.f / ((1 << 16) - 1);

ConstantBuffer<GlobalShaderData>                GlobalData          : register(b0, space0);
ConstantBuffer<PerObjectData>                   PerObjectBuffer     : register(b1, space0);
StructuredBuffer<float3>                        VertexPositions     : register(t0, space0);
StructuredBuffer<VertexElement>                 Elements            : register(t1, space0);
StructuredBuffer<uint>                          SrvIndices          : register(t2, space0);
StructuredBuffer<DirectionalLightParameters>    DirectionalLights   : register(t3, space0);
StructuredBuffer<LightParameters>               CullableLights      : register(t4, space0);
StructuredBuffer<uint2>                         LightGrid           : register(t5, space0);
StructuredBuffer<uint>                          LightIndexList      : register(t6, space0);

SamplerState                                    PointSampler        : register(s0, space0);
SamplerState                                    LinearSampler       : register(s1, space0);
SamplerState                                    AnisotropicSampler  : register(s2, space0);

VertexOut TestShaderVS(in uint VertexIdx: SV_VertexID)
{
    VertexOut vsOut;

    float4 position = float4(VertexPositions[VertexIdx], 1.f);
    float4 worldPosition = mul(PerObjectBuffer.World, position);

#if ELEMENTS_TYPE == ElementsTypeStaticNormal

    VertexElement element = Elements[VertexIdx];
    float2 nXY = element.Normal * InvIntervals - 1.f;
    uint signs = element.ColorTSign >> 24;
    float nSign = float((signs & 0x04) >> 1) - 1.f;
    float3 normal = float3(nXY, sqrt(saturate(1.f - dot(nXY, nXY))) * nSign);

    vsOut.HomogeneousPosition = mul(PerObjectBuffer.WorldViewProjection, position);
    vsOut.WorldPosition = worldPosition.xyz;
    vsOut.WorldNormal = mul(float4(normal, 0.f), PerObjectBuffer.InvWorld).xyz;
    vsOut.WorldTangent = 0.f;
    vsOut.UV = 0.f;

#elif ELEMENTS_TYPE == ElementsTypeStaticNormalTexture

    VertexElement element = Elements[VertexIdx];
    uint signs = element.ColorTSign >> 24;
    float nSign = float((signs & 0x04) >> 1) - 1.f;
    float tSign = float(signs & 0x02) - 1.f;
    float hSign = float((signs & 0x01) << 1) - 1.f;


    float2 nXY = element.Normal * InvIntervals - 1.f;
    float3 normal = float3(nXY, sqrt(saturate(1.f - dot(nXY, nXY))) * nSign);

    float2 tXY = element.Tangent * InvIntervals - 1.f;
    float3 tangent = float3(tXY, sqrt(saturate(1.f - dot(tXY, tXY))) * tSign);
    tangent = tangent - normal * dot(normal, tangent);

    vsOut.HomogeneousPosition = mul(PerObjectBuffer.WorldViewProjection, position);
    vsOut.WorldPosition = worldPosition.xyz;
    vsOut.WorldNormal = normalize(mul(normal, (float3x3)PerObjectBuffer.InvWorld));
    vsOut.WorldTangent = float4(normalize(mul(tangent, (float3x3)PerObjectBuffer.InvWorld)), hSign);
    vsOut.UV = element.UV;
#else
#undef ELEMENTS_TYPE
    vsOut.HomogeneousPosition = mul(PerObjectBuffer.WorldViewProjection, position);
    vsOut.WorldPosition = worldPosition.xyz;
    vsOut.WorldNormal = 0.f;
    vsOut.WorldTangent = 0.f;
    vsOut.UV = 0.f;
#endif
    return vsOut;
}

#define TILE_SIZE 32
#define NO_LIGHT_ATTENUATION 0

float4 Sample(uint index, SamplerState sampler, float2 uv)
{
    return Texture2D(ResourceDescriptorHeap[index]).Sample(sampler, uv);
}

float4 Sample(uint index, SamplerState sampler, float2 uv, float mip)
{
    return Texture2D(ResourceDescriptorHeap[index]).SampleLevel(sampler, uv, mip);
}

float4 SampleCube(uint index, SamplerState sampler, float3 n)
{
    return TextureCube(ResourceDescriptorHeap[index]).Sample(sampler, n);
}

float4 SampleCube(uint index, SamplerState sampler, float3 n, float mip)
{
    return TextureCube(ResourceDescriptorHeap[index]).SampleLevel(sampler, n, mip);
}

float3 PhongBRDF(float3 N, float3 L, float3 V, float3 diffuseColor, float3 specularColor, float shininess)
{
    float3 color = diffuseColor;
    const float3 R = reflect(-L, N);
    const float VoR = max(dot(V, R), 0.f);
    color += pow(VoR, max(shininess, 1.f)) * specularColor;

    return color;
}

float3 CookTorranceBRDF(Surface S, float3 L)
{
    const float3 N = S.Normal;
    const float3 H = normalize(S.V + L);
    const float NoV = abs(S.NoV) + 1e-5;
    const float NoL = saturate(dot(N, L));
    const float NoH = saturate(dot(N, H));
    const float VoH = saturate(dot(S.V, H));

    const float D = D_GGX(NoH, S.a2);
    const float G = V_SmithGGXCorrelated(NoV, NoL, S.a2);
    const float3 F = F_Schlick(S.SpecularColor, VoH);

    float3 specularBRDF = (D * G) * F;
    float3 rho = 1.f - F;
    float3 diffuseBRDF = Diffuse_Lambert() * S.DiffuseColor * rho;
    //float3 diffuseBRDF = Diffuse_Burley(NoV, NoL, VoH, S.PerceptualRoughness * S.PerceptualRoughness) * S.DiffuseColor * rho;

    // NOTE: See "Practical multiple scattering compensation for microfacet models"
    //       https://blog.selfshadow.com/publications/turquin/ms_comp_final.pdf
    //       Eq. (16) with Ess == BrdfLut.x
    float2 BrdfLut = Sample(GlobalData.AmbientLight.BrdfLutSrvIndex, LinearSampler, float2(NoV, S.PerceptualRoughness), 0).rg;
    float3 energyCompensation = 1.f + S.SpecularColor * (rcp(BrdfLut.x) - 1.f);
    specularBRDF *= energyCompensation;

    return (diffuseBRDF + S.SpecularStrength * specularBRDF) * NoL;
}

float3 CalculateLighting(Surface S, float3 L, float3 lightColor)
{
#if 0 // PHONG
    const float3 N = S.Normal;
    const float NoL = saturate(dot(N, L));
    return PhongBRDF(N, L, S.V, S.BaseColor, 1.f, (1 - S.PerceptualRoughness) * 100.f) * (NoL / PI) * lightColor;
#else // PBR
    // We don't have light-units and therefore we don't know what intensity value of 1 corresponds to.
    // For now, let's multiply by PI to make a scene a bit lighter.
    return CookTorranceBRDF(S, L) * lightColor * PI;
#endif
}

float3 PointLight(Surface S, float3 worldPosition, LightParameters light)
{
    float3 L = light.Position - worldPosition;
    const float dSq = dot(L, L);
    float3 color = 0.f;
#if NO_LIGHT_ATTENUATION
    float3 N = S.Normal;
    if (dSq < light.Range * light.Range)
    {
        const float dRcp = rsqrt(dSq);
        L *= dRcp;
        color = saturate(dot(N, L)) * light.Color * light.Intensity * 0.05f;
    }
#else
    if (dSq < light.Range * light.Range)
    {
        const float dRcp = rsqrt(dSq);
        L *= dRcp;
        const float attenuation = 1.f - smoothstep(0.1f * light.Range, light.Range, rcp(dRcp));
        color = CalculateLighting(S, L, light.Color * light.Intensity * attenuation);
    }
#endif
    return color;
}

float3 Spotlight(Surface S, float3 worldPosition, LightParameters light)
{
    float3 L = light.Position - worldPosition;
    const float dSq = dot(L, L);
    float3 color = 0.f;
#if NO_LIGHT_ATTENUATION
    float3 N = S.Normal;
    if (dSq < light.Range * light.Range)
    {
        const float dRcp = rsqrt(dSq);
        L *= dRcp;
        const float CosAngleToLight = saturate(dot(-L, light.Direction));
        const float angularAttenuation = float(light.CosPenumbra < CosAngleToLight);
        color = saturate(dot(N, L)) * light.Color * light.Intensity * angularAttenuation * 0.05f;
    }
#else
    if (dSq < light.Range * light.Range)
    {
        const float dRcp = rsqrt(dSq);
        L *= dRcp;
        const float attenuation = 1.f - smoothstep(0.1f * light.Range, light.Range, rcp(dRcp));
        const float CosAngleToLight = saturate(dot(-L, light.Direction));
        const float angularAttenuation = smoothstep(light.CosPenumbra, light.CosUmbra, CosAngleToLight);
        color = CalculateLighting(S, L, light.Color * light.Intensity * attenuation * angularAttenuation);
    }
#endif
    return color;
}

// [Lagarde et al. 2014, Moving Frostbite to Physically Based Rendering ]
float3 GetSpecularDominantDir(float3 N, float3 R, float roughness)
{
    float smoothness = saturate(1- roughness);
    float lerpFactor = smoothness * (sqrt(smoothness) + roughness);
    // The result is not normalized as we fetch in a cubemap
    return lerp(N, R, lerpFactor);
}

float3 EvaluateIBL(Surface S)
{
    const float NoV = saturate(S.NoV);
    const float roughness = S.PerceptualRoughness * S.PerceptualRoughness;
    const float3 F0 = S.SpecularColor;
    const float3 F90 = max(1.f - S.PerceptualRoughness, F0);
    const float3 F = F_Schlick(NoV, F0, F90);

    AmbientLightParameters IBL = GlobalData.AmbientLight;
    float3 diffN = S.Normal;
    float3 diffuse = SampleCube(IBL.DiffuseSrvIndex, LinearSampler, diffN).rgb * S.DiffuseColor * (1.f - F);
    float3 specN = GetSpecularDominantDir(S.Normal, reflect(-S.V, S.Normal), roughness);
    float3 specularIBL = SampleCube(IBL.SpecularSrvIndex, LinearSampler, specN, S.PerceptualRoughness * 5.f).rgb;
    float2 BrdfLut = Sample(IBL.BrdfLutSrvIndex, LinearSampler, float2(NoV, S.PerceptualRoughness), 0).rg;
    float3 specular = specularIBL * (S.SpecularStrength * F0 * BrdfLut.x + F90 * BrdfLut.y);

    // NOTE: See "Practical multiple scattering compensation for microfacet models"
    //       https://blog.selfshadow.com/publications/turquin/ms_comp_final.pdf
    //       Eq. (16) with Ess == BrdfLut.x
    float3 energyCompensation = 1.f + F0 * (rcp(BrdfLut.x) - 1.f);
    specular *= energyCompensation;

    return  (diffuse + specular) * IBL.Intensity;
}

Surface GetSurface(VertexOut psIn, float3 V)
{
    Surface S;

    S.BaseColor = PerObjectBuffer.BaseColor.rgb;
    S.Metallic = PerObjectBuffer.Metallic;
    S.Normal = normalize(psIn.WorldNormal);
    S.PerceptualRoughness = PerObjectBuffer.Roughness;
    S.EmissiveColor = PerObjectBuffer.Emissive;
    S.EmissiveIntensity = PerObjectBuffer.EmissiveIntensity;
    S.AmbientOcclusion = 1.f;

#if TEXTURED_MTL
    float2 uv = psIn.UV;
    S.AmbientOcclusion = Sample(SrvIndices[0], LinearSampler, uv).r;
    S.BaseColor = Sample(SrvIndices[1], LinearSampler, uv).rgb;
    S.EmissiveColor = Sample(SrvIndices[2], LinearSampler, uv).rgb;
    float2 metalRough = Sample(SrvIndices[3], LinearSampler, uv).rg;
    S.Metallic = metalRough.r;
    S.PerceptualRoughness = metalRough.g;
    S.EmissiveIntensity = 1.f;
    float3 n = Sample(SrvIndices[4], LinearSampler, uv).rgb;
    n = n * 2.f - 1.f;
    n.z = sqrt(1.f - saturate(dot(n.xy, n.xy)));

    const float3 N = psIn.WorldNormal;
    const float3 T = psIn.WorldTangent.xyz;
    const float3 B = cross(N, T) * psIn.WorldTangent.w;
    const float3x3 TBN = float3x3(T, B, N);
    // Transform from tangent-space to world-space.
    S.Normal = normalize(mul(n, TBN));

#endif

    S.V = V;
    S.PerceptualRoughness = max(S.PerceptualRoughness , 0.045f);
    const float roughness = S.PerceptualRoughness * S.PerceptualRoughness;
    S.a2 = roughness * roughness;
    S.NoV = dot(V, S.Normal);
    S.DiffuseColor = S.BaseColor * (1.f - S.Metallic);
    S.SpecularColor = lerp(0.04f, S.BaseColor, S.Metallic); // AKA F0
    S.SpecularStrength = lerp(1 - min(S.PerceptualRoughness, 0.95f), 1.f, S.Metallic);

    return S;
}

uint GetGridIndex(float2 posXY, float viewWidth)
{
    const uint2 pos = uint2(posXY);
    const uint tilesX = ceil(viewWidth / TILE_SIZE);
    return (pos.x / TILE_SIZE) + (tilesX * (pos.y / TILE_SIZE));
}

[earlydepthstencil]
PixelOut TestShaderPS(in VertexOut psIn)
{
    float3 viewDir = normalize(GlobalData.CameraPosition - psIn.WorldPosition);
    Surface S = GetSurface(psIn, viewDir);

    float3 color = 0;
#if 0

    uint i = 0;
    for (i = 0; i < GlobalData.NumDirectionalLights; ++i)
    {
        DirectionalLightParameters light = DirectionalLights[i];
        color += CalculateLighting(S, -light.Direction, light.Color * light.Intensity);
    }

    const uint gridIndex = GetGridIndex(psIn.HomogeneousPosition.xy, GlobalData.ViewWidth);
    uint lightStartIndex = LightGrid[gridIndex].x;
    const uint lightCount = LightGrid[gridIndex].y;

    const uint numPointLights = lightStartIndex + (lightCount >> 16);
    const uint numSpotlights = numPointLights + (lightCount & 0xffff);

    for (i = lightStartIndex; i < numPointLights; ++i)
    {
        const uint lightIndex = LightIndexList[i];
        LightParameters light = CullableLights[lightIndex];
        color += PointLight(S, psIn.WorldPosition, light);
    }

    for (i = numPointLights; i < numSpotlights; ++i)
    {
        const uint lightIndex = LightIndexList[i];
        LightParameters light = CullableLights[lightIndex];
        color += Spotlight(S, psIn.WorldPosition, light);
    }
#else

    if (GlobalData.AmbientLight.Intensity > 0)
    {
      color += EvaluateIBL(S);
    }

#endif
    
#if TEXTURED_MTL
    float VoN = S.NoV * 1.3f;
    float VoN2 = VoN * VoN;
    float VoN4 = VoN2 * VoN2;
    float3 e = S.EmissiveColor;
    S.EmissiveColor = max(VoN4 * VoN4, 0.1f) * e * e;
#endif

    PixelOut psOut;
    psOut.Color = float4(color * S.AmbientOcclusion + S.EmissiveColor * S.EmissiveIntensity, 1.f);

    return psOut;
}