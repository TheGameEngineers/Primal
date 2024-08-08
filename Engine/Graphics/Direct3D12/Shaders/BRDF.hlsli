// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.

float Pow5(float x)
{
    float xx = x * x;
    return xx * xx * x;
}

// GGX / Trowbridge-Reitz
// [Walter et al. 2007, "Microfacet models for refraction through rough surfaces"]
float D_GGX(float NoH, float a)
{
    float d = (NoH * a - NoH) * NoH + 1;
    return a / (PI * d * d);
}

// [Heitz 2014, "Understanding the Masking-Shadowing Function in Microfacet-Based BRDFs"]
float V_SmithGGXCorrelated(float NoV, float NoL, float a)
{
    float GGXL = NoV * sqrt((-NoL * a + NoL) * NoL + a);
    float GGXV = NoL * sqrt((-NoV * a + NoV) * NoV + a);
    return 0.5f / (GGXV + GGXL);
}

// Appoximation of joint Smith term for GGX
// [Heitz 2014, "Understanding the Masking-Shadowing Function in Microfacet-Based BRDFs"]
float V_SmithGGXCorrelatedApprox(float NoV, float NoL, float a) {
    float GGXV = NoL * ((-NoV * a + NoV) + a);
    float GGXL = NoV * ((-NoL * a + NoL) + a);
    return 0.5f / (GGXV + GGXL);
}

// [Schlick 1994, "An Inexpensive BRDF Model for Physically-Based Rendering"]
float3 F_Schlick(float3 F0, float VoH)
{
    float u = Pow5(1.f - VoH);
    // Anything less than 2% is physically impossible and is instead considered to be shadowing
    float3 F90Approx = saturate(50.f * F0.g);
    return  F90Approx * u + (1 - u) * F0;
}

float3 F_Schlick(float u, float3 f0, float3 f90) {
    return f0 + (f90 - f0) * Pow5(1.f - u);
}

float3 Diffuse_Lambert()
{
    return 1 / PI;
}

// [Burley 2012, "Physically-Based Shading at Disney"]
float3 Diffuse_Burley(float NoV, float NoL, float VoH, float roughness)
{
    float u = Pow5(1.f - NoV);
    float v = Pow5(1.f - NoL);

    float FD90 = 0.5f + 2.f * VoH * VoH * roughness;
    float FdV = 1.f + (u * FD90 - u);
    float FdL = 1.f + (v * FD90 - v);
    return (1.f / PI) * FdV * FdL;
}