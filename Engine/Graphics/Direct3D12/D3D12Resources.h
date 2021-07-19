// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "D3D12CommonHeaders.h"

namespace primal::graphics::d3d12 {

class descriptor_heap;

struct descriptor_handle {
    D3D12_CPU_DESCRIPTOR_HANDLE cpu{};
    D3D12_GPU_DESCRIPTOR_HANDLE gpu{};

    constexpr bool is_valid() const { return cpu.ptr != 0; }
    constexpr bool is_shader_visible() const { return gpu.ptr != 0; }

#ifdef _DEBUG
private:
    friend class descriptor_heap;
    descriptor_heap* container{ nullptr };
    u32              index{ u32_invalid_id };
#endif
};

class descriptor_heap
{
public:
    explicit descriptor_heap(D3D12_DESCRIPTOR_HEAP_TYPE type) : _type{ type } {}
    DISABLE_COPY_AND_MOVE(descriptor_heap);
    ~descriptor_heap() { assert(!_heap); }

    bool initialize(u32 capacity, bool is_shader_visible);
    void release();
    void process_deferred_free(u32 frame_idx);

    [[nodiscard]] descriptor_handle allocate();
    void free(descriptor_handle& handle);

    constexpr D3D12_DESCRIPTOR_HEAP_TYPE type() const { return _type; }
    constexpr D3D12_CPU_DESCRIPTOR_HANDLE cpu_start() const { return _cpu_start; }
    constexpr D3D12_GPU_DESCRIPTOR_HANDLE gpu_start() const { return _gpu_start; }
    constexpr ID3D12DescriptorHeap *const heap() const { return _heap; }
    constexpr u32 capacity() const { return _capacity; }
    constexpr u32 size() const { return _size; }
    constexpr u32 descriptor_size() const { return _descriptor_size; }
    constexpr bool is_shader_visible() const { return _gpu_start.ptr != 0; }

private:
    ID3D12DescriptorHeap*               _heap;
    D3D12_CPU_DESCRIPTOR_HANDLE         _cpu_start{};
    D3D12_GPU_DESCRIPTOR_HANDLE         _gpu_start{};
    std::unique_ptr<u32[]>              _free_handles{};
    utl::vector<u32>                    _deferred_free_indices[frame_buffer_count]{};
    std::mutex                          _mutex{};
    u32                                 _capacity{ 0 };
    u32                                 _size{ 0 };
    u32                                 _descriptor_size{};
    const D3D12_DESCRIPTOR_HEAP_TYPE    _type{};
};
}
