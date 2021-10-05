// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.

float4 FillColorPS(in noperspective float4 Position : SV_Position,
                   in noperspective float2 UV : TEXCOORD) : SV_Target0
{
    return float4(1.f, 0.f, 1.f, 1.f);
}