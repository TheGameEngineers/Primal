// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "CommonHeaders.h"
#include "Renderer.h"

namespace primal::graphics {
struct platform_interface
{
    bool(*initialize)(void);
    void(*shutdown)(void);

    struct {
        surface(*create)(platform::window);
        void(*remove)(surface_id);
        void(*resize)(surface_id, u32, u32);
        u32(*width)(surface_id);
        u32(*height)(surface_id);
        void(*render)(surface_id, frame_info);
    } surface;

    struct {
        void(*create_light_set)(u64);
        void(*remove_light_set)(u64);
        light(*create)(light_init_info);
        void(*remove)(light_id, u64);
        void(*set_parameter)(light_id, u64, light_parameter::parameter, const void *const, u32);
        void(*get_parameter)(light_id, u64, light_parameter::parameter, void *const, u32);
    } light;

    struct {
        camera(*create)(camera_init_info);
        void(*remove)(camera_id);
        void(*set_parameter)(camera_id, camera_parameter::parameter, const void *const, u32);
        void(*get_parameter)(camera_id, camera_parameter::parameter, void *const, u32);
    } camera;

    struct {
        id::id_type(*add_submesh)(const u8*&);
        void (*remove_submesh)(id::id_type);
        id::id_type(*add_material)(material_init_info);
        void (*remove_material)(id::id_type);
        id::id_type(*add_render_item)(id::id_type, id::id_type, u32, const id::id_type *const);
        void (*remove_render_item)(id::id_type);
    } resources;

    graphics_platform platform = (graphics_platform)-1;
};
}
