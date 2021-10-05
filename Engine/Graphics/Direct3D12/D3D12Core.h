// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "D3D12CommonHeaders.h"

namespace primal::graphics::d3d12 {
class descriptor_heap;
}

namespace primal::graphics::d3d12::core {

bool initialize();
void shutdown();

template<typename T>
constexpr void release(T*& resource)
{
    if (resource)
    {
        resource->Release();
        resource = nullptr;
    }
}

namespace detail {
void deferred_release(IUnknown* resource);
}

template<typename T>
constexpr void deferred_release(T*& resource)
{
    if (resource)
    {
        detail::deferred_release(resource);
        resource = nullptr;
    }
}

id3d12_device *const device();
descriptor_heap& rtv_heap();
descriptor_heap& dsv_heap();
descriptor_heap& srv_heap();
descriptor_heap& uav_heap();

u32 current_frame_index();
void set_deferred_releases_flag();

surface create_surface(platform::window window);
void remove_surface(surface_id id);
void resize_surface(surface_id id, u32, u32);
u32 surface_width(surface_id id);
u32 surface_height(surface_id id);
void render_surface(surface_id id);
}