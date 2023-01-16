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

[earlydepthstencil]
PixelOut TestShaderPS(in VertexOut psIn)
{
    PixelOut psOut;

    float3 normal = normalize(psIn.WorldNormal);
    float3 viewDir = normalize(GlobalData.CameraPosition - psIn.WorldPosition);

    float3 color = 0;

    for (uint i = 0; i < GlobalData.NumDirectionalLights; ++i)
    {
        DirectionalLightParameters light = DirectionalLights[i];

        float3 lightDirection = light.Direction;
        if (abs(lightDirection.z - 1.f) < 0.001f)
        {
            lightDirection = GlobalData.CameraDirection;
        }
        float diffuse = max(dot(normal, -lightDirection), 0.f);
        float3 reflection = reflect(lightDirection, normal);
        float specular = pow(max(dot(viewDir, reflection), 0.f), 16) * 0.5f;

        float3 lightColor = light.Color * light.Intensity;
        color += (diffuse + specular) * lightColor;
    }

    float3 ambient = 10 / 255.f;
    psOut.Color = saturate(float4(color + ambient, 1.f));

    return psOut;
}