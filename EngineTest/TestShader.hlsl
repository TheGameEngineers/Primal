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

struct ElementStaticNormalTexture
{
    uint        ColorTSign;
    uint16_t2   Normal;
    uint16_t2   Tangent;
    float2      UV;
};

struct PixelOut
{
    float4 Color                : SV_TARGET0;
};

const static float InvIntervals = 2.f / ((1 << 16) - 1);

ConstantBuffer<GlobalShaderData>                GlobalData              : register(b0, space0);
ConstantBuffer<PerObjectData>                   PerObjectBuffer         : register(b1, space0);
StructuredBuffer<float3>                        VertexPositions         : register(t0, space0);
StructuredBuffer<ElementStaticNormalTexture>    Elements                : register(t1, space0);

VertexOut TestShaderVS(in uint VertexIdx: SV_VertexID)
{
    VertexOut vsOut;

    // if ELEMENT_TYPE == 3
    float4 position = float4(VertexPositions[VertexIdx], 1.f);
    float4 worldPosition = mul(PerObjectBuffer.World, position);

    uint signs = 0;
    uint16_t2 packedNormal = 0;
    ElementStaticNormalTexture element = Elements[VertexIdx];
    signs = (element.ColorTSign >> 24) & 0xff;
    packedNormal = element.Normal;

    float nSign = float(signs & 0x02) - 1;
    float3 normal;
    normal.x = packedNormal.x * InvIntervals - 1.f;
    normal.y = packedNormal.y * InvIntervals - 1.f;
    normal.z = sqrt(saturate(1.f - dot(normal.xy, normal.xy))) * nSign;

    vsOut.HomogeneousPosition = mul(PerObjectBuffer.WorldViewProjection, position);
    vsOut.WorldPosition = worldPosition.xyz;
    vsOut.WorldNormal = mul(float4(normal, 0.f), PerObjectBuffer.InvWorld).xyz;
    vsOut.WorldTangent = 0.f;
    vsOut.UV = 0.f;

    return vsOut;
}

[earlydepthstencil]
PixelOut TestShaderPS(in VertexOut psIn)
{
    PixelOut psOut;

    psOut.Color = float4(psIn.WorldNormal, 1.f);

    return psOut;
}