// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "D3D12CommonHeaders.h"

namespace primal::graphics::d3d12::d3dx {

constexpr struct {
    const D3D12_HEAP_PROPERTIES default_heap
    {
        D3D12_HEAP_TYPE_DEFAULT,                        // Type;
        D3D12_CPU_PAGE_PROPERTY_UNKNOWN,                // CPUPageProperty;
        D3D12_MEMORY_POOL_UNKNOWN,                      // MemoryPoolPreference;
        0,                                              // CreationNodeMask;
        0                                               // VisibleNodeMask;
    };
} heap_properties;
}
