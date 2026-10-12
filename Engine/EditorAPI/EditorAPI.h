// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#ifdef USE_WITH_EDITOR

#pragma once
#include "CommonHeaders.h"

namespace primal::editor {

struct editor_api final
{
    void (*GetEngineVersion)(int*, int*, int*) { nullptr };
    // CAMERA
    void (*GetCameraId)(id::id_type, id::id_type*) { nullptr };
    void (*GetCameraUp)(id::id_type*, f32*, f32*, f32*, u32) { nullptr };
    void (*GetCameraFieldOfView)(id::id_type*, f32*, u32) { nullptr };
    void (*GetCameraAspectRatio)(id::id_type*, f32*, u32) { nullptr };
    void (*GetCameraOrthographicSize)(id::id_type*, f32*, u32) { nullptr };
    void (*GetCameraRange)(id::id_type*, f32*, f32*, u32) { nullptr };
    void (*GetCameraView)(id::id_type*, math::m4x4*, u32) { nullptr };
    void (*GetCameraProjection)(id::id_type*, math::m4x4*, u32) { nullptr };
    void (*GetCameraInverseProjection)(id::id_type*, math::m4x4*, u32) { nullptr };
    void (*GetCameraViewProjection)(id::id_type*, math::m4x4*, u32) { nullptr };
    void (*GetCameraInverseViewProjection)(id::id_type*, math::m4x4*, u32) { nullptr };
    u32 (*GetCameraProjectionType)(id::id_type) { nullptr };
    id::id_type (*GetCameraEntityId)(id::id_type) { nullptr };
    void (*SetCameraUp)(id::id_type*, f32*, f32*, f32*, u32) { nullptr };
    void (*SetCameraFieldOfView)(id::id_type*, f32*, u32) { nullptr };
    void (*SetCameraAspectRatio)(id::id_type*, f32*, u32) { nullptr };
    void (*SetCameraOrthographicSize)(id::id_type*, f32*, u32) { nullptr };
    void (*SetCameraRange)(id::id_type*, f32*, f32*, u32) { nullptr };
    // LIGHT
    void (*GetLightId)(id::id_type, id::id_type*, u64*) { nullptr };
    void (*GetLightIsEnabled)(id::id_type*, u64*, u32*, u32) { nullptr };
    void (*GetLightIntensity)(id::id_type*, u64*, f32*, u32) { nullptr };
    void (*GetLightRange)(id::id_type*, u64*, f32*, u32) { nullptr };
    void (*GetLightConeAngles)(id::id_type*, u64*, f32*, f32*, u32) { nullptr };
    void (*GetLightColor)(id::id_type*, u64*, f32*, f32*, f32*, u32) { nullptr };
    void (*GetLightAttenuation)(id::id_type*, u64*, f32*, f32*, f32*, u32) { nullptr };
    u32(*GetLightType)(id::id_type, u64) { nullptr };
    id::id_type(*GetLightEntityId)(id::id_type, u64) { nullptr };
    void (*SetLightIsEnabled)(id::id_type*, u64*, u32*, u32) { nullptr };
    void (*SetLightIntensity)(id::id_type*, u64*, f32*, u32) { nullptr };
    void (*SetLightRange)(id::id_type*, u64*, f32*, u32) { nullptr };
    void (*SetLightConeAngles)(id::id_type*, u64*, f32*, f32*, u32) { nullptr };
    void (*SetLightColor)(id::id_type*, u64*, f32*, f32*, f32*, u32) { nullptr };
    void (*SetLightAttenuation)(id::id_type*, u64*, f32*, f32*, f32*, u32) { nullptr };
    // ENTITY / COMPONENT
    id::id_type(*GetComponentId)(id::id_type, u32) { nullptr };
    void (*GetPosition)(id::id_type*, f32*, f32*, f32*, u32) { nullptr };
    void (*GetRotation)(id::id_type*, f32*, f32*, f32*, u32) { nullptr };
    void (*GetScale)(id::id_type*, f32*, f32*, f32*, u32) { nullptr };
    void (*SetPosition)(id::id_type*, f32*, f32*, f32*, u32, b32) { nullptr };
    void (*SetRotation)(id::id_type*, f32*, f32*, f32*, u32, u32) { nullptr };
    void (*SetScale)(id::id_type*, f32*, f32*, f32*, u32, b32) { nullptr };
    void (*GetLocalFrame)(id::id_type, math::m3x3*) { nullptr };
    void (*ScriptSetRotation)(id::id_type, f32, f32, f32, f32) { nullptr };
    void (*ScriptSetPosition)(id::id_type, f32, f32, f32) { nullptr };
    void (*ScriptSetScale)(id::id_type, f32, f32, f32) { nullptr };
    u8(*RegisterScript)(size_t, uintptr_t) { nullptr };
    u8(*AddScriptName)(const char*) { nullptr };
    // INPUT
    void (*GetInput)(u32, u32, f32*) { nullptr };
    void (*GetInputBinding)(u64, f32*) { nullptr };

    void set(editor_api* api)
    {
        *this = *api;
    }
};

} // namespace primal::editor
#endif // USE_WITH_EDITOR