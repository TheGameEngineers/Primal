// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Common.hlsli"

struct VertexOut
{
    float4 HomogeneousPosition  : SV_POSITION;
    float3 WorldPosition        : POSITION;
    float3 WorldNormal          : NORMAL;
    float3 WorldTangent         : TANGENT;
    float2 UV                   : TEXTURE;
};

struct PixelOut
{
    float4 Color                : SV_TARGET0;
};

#define ElementsTypeStaticNormal                0x01
#define ElementsTypeStaticNormalTexture         0x03
#define ElementsTypeStaticColor                 0x04
#define ElementsTypeSkeletal                    0x08
#define ElementsTypeSkeletalColor               ElementsTypeSkeletal | ElementsTypeStaticColor
#define ElementsTypeSkeletalNormal              ElementsTypeSkeletal | ElementsTypeStaticNormal
#define ElementsTypeSkeletalNormalColor         ElementsTypeSkeletalNormal | ElementsTypestaticColor
#define ElementsTypeSkeletalNormalTexture       ElementsTypeSkeletal | ElementsTypeStaticNormalTexture
#define ElementsTypeSkeletalNormalTextureColor  ElementsTypeSkeletalNormalTexture | ElementsTypeStaticColor

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

StructuredBuffer<DirectionalLightParameters>    DirectionalLights   : register(t3, space0);
StructuredBuffer<LightParameters>               CullableLights      : register(t4, space0);
StructuredBuffer<uint2>                         LightGrid           : register(t5, space0);
StructuredBuffer<uint>                          LightIndexList      : register(t6, space0);

VertexOut TestShaderVS(in uint VertexIdx: SV_VertexID)
{
    VertexOut vsOut;

    float4 position = float4(VertexPositions[VertexIdx], 1.f);
    float4 worldPosition = mul(PerObjectBuffer.World, position);

#if ELEMENTS_TYPE == ElementsTypeStaticNormal

    VertexElement element = Elements[VertexIdx];
    float2 nXY = element.Normal * InvIntervals - 1.f;
    uint signs = (element.ColorTSign >> 24) & 0xff;
    float nSign = float(signs & 0x02) - 1;
    float3 normal = float3(nXY.x, nXY.y, sqrt(saturate(1.f - dot(nXY, nXY))) * nSign);

    vsOut.HomogeneousPosition = mul(PerObjectBuffer.WorldViewProjection, position);
    vsOut.WorldPosition = worldPosition.xyz;
    vsOut.WorldNormal = mul(float4(normal, 0.f), PerObjectBuffer.InvWorld).xyz;
    vsOut.WorldTangent = 0.f;
    vsOut.UV = 0.f;

#elif ELEMENTS_TYPE == ElementsTypeStaticNormalTexture

    VertexElement element = Elements[VertexIdx];
    float2 nXY = element.Normal * InvIntervals - 1.f;
    uint signs = (element.ColorTSign >> 24) & 0xff;
    float nSign = float(signs & 0x02) - 1;
    float3 normal = float3(nXY.x, nXY.y, sqrt(saturate(1.f - dot(nXY, nXY))) * nSign);

    vsOut.HomogeneousPosition = mul(PerObjectBuffer.WorldViewProjection, position);
    vsOut.WorldPosition = worldPosition.xyz;
    vsOut.WorldNormal = mul(float4(normal, 0.f), PerObjectBuffer.InvWorld).xyz;
    vsOut.WorldTangent = 0.f;
    vsOut.UV = 0.f;
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

float3 CalculateLighting(float3 N, float3 L, float3 V, float3 lightColor)
{
    const float NoL = dot(N, L);
    float specular = 0;

    if (NoL > 0.f)
    {
        const float3 R = reflect(-L, N);
        const float VoR = max(dot(V, R), 0.f);
        specular = saturate(NoL * pow(VoR, 4.f) * 0.5f);
    }

    return (max(0.f, NoL) + specular) * lightColor;
}

float3 PointLight(float3 N, float3 worldPosition, float3 V, LightParameters light)
{
    float3 L = light.Position - worldPosition;
    const float dSq = dot(L, L);
    float3 color = 0.f;
#if NO_LIGHT_ATTENUATION
    if (dSq < light.Range * light.Range)
    {
        const float dRcp = rsqrt(dSq);
        L *= dRcp;
        color = saturate(dot(N, L)) * light.Color * light.Intensity * 0.01f;
    }
#else
    if (dSq < light.Range * light.Range)
    {
        const float dRcp = rsqrt(dSq);
        L *= dRcp;
        const float attenuation = 1.f - smoothstep(-light.Range, light.Range, rcp(dRcp));
        color = CalculateLighting(N, L, V, light.Color * light.Intensity * attenuation * 0.2f);
    }
#endif
    return color;
}

float3 Spotlight(float3 N, float3 worldPosition, float3 V, LightParameters light)
{
    float3 L = light.Position - worldPosition;
    const float dSq = dot(L, L);
    float3 color = 0.f;
#if NO_LIGHT_ATTENUATION
    if (dSq < light.Range * light.Range)
    {
        const float dRcp = rsqrt(dSq);
        L *= dRcp;
        const float CosAngleToLight = saturate(dot(-L, light.Direction));
        const float angularAttenuation = float(light.CosPenumbra < CosAngleToLight);
        color = saturate(dot(N, L)) * light.Color * light.Intensity * angularAttenuation * 0.01f;
    }
#else
    if (dSq < light.Range * light.Range)
    {
        const float dRcp = rsqrt(dSq);
        L *= dRcp;
        const float attenuation = 1.f - smoothstep(-light.Range, light.Range, rcp(dRcp));
        const float CosAngleToLight = saturate(dot(-L, light.Direction));
        const float angularAttenuation = smoothstep(light.CosPenumbra, light.CosUmbra, CosAngleToLight);
        color = CalculateLighting(N, L, V, light.Color * light.Intensity * attenuation * angularAttenuation * 0.2f);
    }
#endif
    return color;
}

uint GetGridIndex(float2 posXY, float viewWidth)
{
    const uint2 pos = uint2(posXY);
    const uint tileX = ceil(viewWidth / TILE_SIZE);
    return (pos.x / TILE_SIZE) + (tileX * (pos.y / TILE_SIZE));
}

[earlydepthstencil]
PixelOut TestShaderPS(in VertexOut psIn)
{
    PixelOut psOut;

    float3 normal = normalize(psIn.WorldNormal);
    float3 viewDir = normalize(GlobalData.CameraPosition - psIn.WorldPosition);

    float3 color = 0;
    uint i = 0;
    for (i = 0; i < GlobalData.NumDirectionalLights; ++i)
    {
        DirectionalLightParameters light = DirectionalLights[i];

        float3 lightDirection = light.Direction;
        if (abs(lightDirection.z - 1.f) < 0.001f)
        {
            lightDirection = GlobalData.CameraDirection;
        }

        color += 0.02f * CalculateLighting(normal, -lightDirection, viewDir, light.Color * light.Intensity);
    }

    const uint gridIndex = GetGridIndex(psIn.HomogeneousPosition.xy, GlobalData.ViewWidth);
    uint lightStartIndex = LightGrid[gridIndex].x;
    const uint lightCount = LightGrid[gridIndex].y;

#if USE_BOUNDING_SPHERES
    const uint numPointLights = lightStartIndex + (lightCount >> 16);
    const uint numSpotlights = numPointLights + (lightCount & 0xffff);

    for (i = lightStartIndex; i < numPointLights; ++i)
    {
        const uint lightIndex = LightIndexList[i];
        LightParameters light = CullableLights[lightIndex];
        color += PointLight(normal, psIn.WorldPosition, viewDir, light);
    }

    for (i = numPointLights; i < numSpotlights; ++i)
    {
        const uint lightIndex = LightIndexList[i];
        LightParameters light = CullableLights[lightIndex];
        color += Spotlight(normal, psIn.WorldPosition, viewDir, light);
    }

#else
    for (i = 0; i < lightCount; ++i)
    {
        const uint lightIndex = LightIndexList[lightStartIndex + i];
        LightParameters light = CullableLights[lightIndex];

        if (light.Type == LIGHT_TYPE_POINT_LIGHT)
        {
            color += PointLight(normal, psIn.WorldPosition, viewDir, light);
        }
        else if (light.Type == LIGHT_TYPE_SPOTLIGHT)
        {
            color += Spotlight(normal, psIn.WorldPosition, viewDir, light);
        }
    }
#endif

    float3 ambient = 0 / 255.f;
    psOut.Color = saturate(float4(color + ambient, 1.f));

    return psOut;
}