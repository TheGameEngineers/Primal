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

struct d3d12_texture_init_info
{
    ID3D12Heap1*                        heap{ nullptr };
    ID3D12Resource*                     resource{ nullptr };
    D3D12_SHADER_RESOURCE_VIEW_DESC*    srv_desc{ nullptr };
    D3D12_RESOURCE_DESC*                desc{ nullptr };
    D3D12_RESOURCE_ALLOCATION_INFO1     allocation_info{};
    D3D12_RESOURCE_STATES               initial_state{};
    D3D12_CLEAR_VALUE                   clear_value{};
};

class d3d12_texture
{
public:
    constexpr static u32 max_mips{ 14 }; // support up to 16k resolutions.
    d3d12_texture() = default;
    explicit d3d12_texture(d3d12_texture_init_info info);
    DISABLE_COPY(d3d12_texture);
    constexpr d3d12_texture(d3d12_texture&& o)
        : _resource{ o._resource }, _srv{ o._srv }
    {
        o.reset();
    }

    constexpr d3d12_texture& operator=(d3d12_texture&& o)
    {
        assert(this != &o);
        if (this != &o)
        {
            release();
            move(o);
        }
        return *this;
    }

    ~d3d12_texture() { release(); }

    void release();
    constexpr ID3D12Resource *const resource() const { return _resource; }
    constexpr descriptor_handle srv() const { return _srv; }

private:
    constexpr void move(d3d12_texture& o)
    {
        _resource = o._resource;
        _srv = o._srv;
        o.reset();
    }

    constexpr void reset()
    {
        _resource = nullptr;
        _srv = {};
    }

    ID3D12Resource*     _resource{ nullptr };
    descriptor_handle   _srv;
};

class d3d12_render_texture
{
public:
    d3d12_render_texture() = default;
    explicit d3d12_render_texture(d3d12_texture_init_info info);
    DISABLE_COPY(d3d12_render_texture);
    constexpr d3d12_render_texture(d3d12_render_texture&& o)
        : _texture{ std::move(o._texture) }, _mip_count{ o._mip_count }
    {
        for (u32 i{ 0 }; i < _mip_count; ++i) _rtv[i] = o._rtv[i];
        o.reset();
    }

    constexpr d3d12_render_texture& operator=(d3d12_render_texture&& o)
    {
        assert(this != &o);
        if (this != &o)
        {
            release();
            move(o);
        }
        return *this;
    }

    ~d3d12_render_texture() { release(); }

    void release();
    constexpr u32 mip_count() const { return _mip_count; }
    constexpr D3D12_CPU_DESCRIPTOR_HANDLE rtv(u32 mip_index) const { assert(mip_index < _mip_count); return _rtv[mip_index].cpu; }
    constexpr descriptor_handle srv() const { return _texture.srv(); }
    constexpr ID3D12Resource *const resource() const { return _texture.resource(); }
private:
    constexpr void move(d3d12_render_texture& o)
    {
        _texture = std::move(o._texture);
        _mip_count = o._mip_count;
        for (u32 i{ 0 }; i < _mip_count; ++i) _rtv[i] = o._rtv[i];
        o.reset();
    }

    constexpr void reset()
    {
        for (u32 i{ 0 }; i < _mip_count; ++i) _rtv[i] = {};
        _mip_count = 0;
    }

    d3d12_texture       _texture{};
    descriptor_handle   _rtv[d3d12_texture::max_mips]{};
    u32                 _mip_count{ 0 };
};

class d3d12_depth_buffer
{
public:
    d3d12_depth_buffer() = default;
    explicit d3d12_depth_buffer(d3d12_texture_init_info info);
    DISABLE_COPY(d3d12_depth_buffer);
    constexpr d3d12_depth_buffer(d3d12_depth_buffer&& o)
        : _texture{ std::move(o._texture) }, _dsv{ o._dsv }
    {
        o._dsv = {};
    }

    constexpr d3d12_depth_buffer& operator=(d3d12_depth_buffer&& o)
    {
        assert(this != &o);
        if (this != &o)
        {
            _texture = std::move(o._texture);
            _dsv = o._dsv;
            o._dsv = {};
        }
        return *this;
    }

    ~d3d12_depth_buffer() { release(); }

    void release();
    constexpr D3D12_CPU_DESCRIPTOR_HANDLE dsv() const { return _dsv.cpu; }
    constexpr descriptor_handle srv() const { return _texture.srv(); }
    constexpr ID3D12Resource *const resource() const { return _texture.resource(); }

private:
    d3d12_texture       _texture{};
    descriptor_handle   _dsv{};
};
}
