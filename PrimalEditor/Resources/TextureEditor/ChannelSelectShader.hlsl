// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.

// Compile with: fxc ChannelSelectShader.hlsl /E main /T ps_3_0 /O3 /Fo ChannelSelectShader.cso
sampler2D   MipImage : register(s0);
float4      Channels : register(c0);
float4      Info     : register(c1);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    int stride = Info.r;
    float4 c = tex2D(MipImage, uv);

    if(all(Channels) || stride == 1)
    {
        return c;
    }
    else if(!any(Channels.rgb) && any(Channels.a))
    {
        return float4(c.aaa, 1.f);
    }
    else if(stride == 16 || stride == 4 || stride == 3)
    {
        const float3 inv_mask = 1.f - Channels.rgb;
        const float3 mask = c.rgb * Channels.rgb;
        const float3 color = mask.rgb + (mask.gbr * inv_mask.brg + mask.brg * inv_mask.gbr) * inv_mask.rgb;
        return float4(color, (stride == 4 && any(Channels.a)) ? c.a : 1.f);
    }

    return float4(1.f, 0.f, 1.f, 1.f);
}