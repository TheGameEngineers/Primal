// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Fractals.hlsli"

struct ShaderConstants
{
    float Width;
    float Height;
    uint  Frame;
};

ConstantBuffer<ShaderConstants>         ShaderParams                    : register(b1);

#define SAMPLES 4

float4 FillColorPS(in noperspective float4 Position : SV_Position,
                   in noperspective float2 UV : TEXCOORD) : SV_Target0
{
    const float offset = 0.2f;
    const float2 offsets[4] =
    {
        float2(-offset, offset),
        float2(offset, offset),
        float2(offset, -offset),
        float2(-offset, -offset)
    };

    const float2 invDim = float2(1.f / ShaderParams.Width, 1.f / ShaderParams.Height);
    float3 color = 0.f;
    for (int i = 0; i < SAMPLES; i++)
    {
        const float2 uv = (Position.xy + offsets[i]) * invDim;
        //color += DrawMandelbrot(uv);
        color += DrawJuliaSet(uv, ShaderParams.Frame);
    }

    return float4(float3(color.z, color.x, 1.f) * color.x / SAMPLES, 1.f);
    //return float4(color / SAMPLES, 1.f);
}