// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "D3D12Core.h"
#include "D3D12Surface.h"
#include "D3D12Shaders.h"
#include "D3D12GPass.h"
#include "D3D12PostProcess.h"
#include "D3D12Upload.h"
#include "D3D12Content.h"
#include "D3D12Light.h"
#include "D3D12LightCulling.h"
#include "D3D12Camera.h"
#include "Shaders/SharedTypes.h"

extern "C" { __declspec(dllexport) extern const UINT D3D12SDKVersion = 610; }
extern "C" { __declspec(dllexport) extern const char* D3D12SDKPath = u8".\\D3D12\\"; }

using namespace Microsoft::WRL;

namespace primal::graphics::d3d12::core {

namespace {

class d3d12_command
{
public:
    d3d12_command() = default;
    DISABLE_COPY_AND_MOVE(d3d12_command);
    explicit d3d12_command(id3d12_device *const device, D3D12_COMMAND_LIST_TYPE type)
    {
        HRESULT hr{ S_OK };
        D3D12_COMMAND_QUEUE_DESC desc{};
        desc.Flags = D3D12_COMMAND_QUEUE_FLAG_NONE;
        desc.NodeMask = 0;
        desc.Priority = D3D12_COMMAND_QUEUE_PRIORITY_NORMAL;
        desc.Type = type;

        DXCall(hr = device->CreateCommandQueue(&desc, IID_PPV_ARGS(&_cmd_queue)));
        if (FAILED(hr)) goto _error;
        NAME_D3D12_OBJECT(_cmd_queue,
                          type == D3D12_COMMAND_LIST_TYPE_DIRECT ?
                          L"GFX Command Queue" :
                          type == D3D12_COMMAND_LIST_TYPE_COMPUTE ?
                          L"Compute Command Queue" : L"Command Queue");

        for (u32 i{ 0 }; i < frame_buffer_count; ++i)
        {
            command_frame& frame{ _cmd_frames[i] };
            DXCall(hr = device->CreateCommandAllocator(type, IID_PPV_ARGS(&frame.cmd_allocator)));
            if (FAILED(hr)) goto _error;
            NAME_D3D12_OBJECT_INDEXED(frame.cmd_allocator, i,
                                      type == D3D12_COMMAND_LIST_TYPE_DIRECT ?
                                      L"GFX Command Allocator" :
                                      type == D3D12_COMMAND_LIST_TYPE_COMPUTE ?
                                      L"Compute Command Allocator" : L"Command Allocator");
        }

        DXCall(hr = device->CreateCommandList(0, type, _cmd_frames[0].cmd_allocator, nullptr, IID_PPV_ARGS(&_cmd_list)));
        if (FAILED(hr)) goto _error;
        DXCall(_cmd_list->Close());
        NAME_D3D12_OBJECT(_cmd_list,
                          type == D3D12_COMMAND_LIST_TYPE_DIRECT ?
                          L"GFX Command List" :
                          type == D3D12_COMMAND_LIST_TYPE_COMPUTE ?
                          L"Compute Command List" : L"Command List");

        DXCall(hr = device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&_fence)));
        if (FAILED(hr)) goto _error;
        NAME_D3D12_OBJECT(_fence, L"D3D12 Fence");

        _fence_event = CreateEventEx(nullptr, nullptr, 0, EVENT_ALL_ACCESS);
        assert(_fence_event);
        if (!_fence_event) goto _error;

        return;

    _error:
        release();
    }

    ~d3d12_command()
    {
        assert(!_cmd_queue && !_cmd_list && !_fence);
    }

    // Wait for the current frame to be signalled and reset the command list/allocator.
    void begin_frame()
    {
        command_frame& frame{ _cmd_frames[_frame_index] };
        frame.wait(_fence_event, _fence);
        DXCall(frame.cmd_allocator->Reset());
        DXCall(_cmd_list->Reset(frame.cmd_allocator, nullptr));
    }

    // Signal the fence with the new fence value.
    void end_frame(const d3d12_surface& surface)
    {
        DXCall(_cmd_list->Close());
        ID3D12CommandList *const cmd_lists[]{ _cmd_list };
        _cmd_queue->ExecuteCommandLists(_countof(cmd_lists), &cmd_lists[0]);

        // Presenting swap chain buffers happens in lockstep with frame buffers.
        surface.present();

        u64& fence_value{ _fence_value };
        ++fence_value;
        command_frame& frame{ _cmd_frames[_frame_index] };
        frame.fence_value = fence_value;
        _cmd_queue->Signal(_fence, fence_value);

        _frame_index = (_frame_index + 1) % frame_buffer_count;
    }

    void flush()
    {
        for (u32 i{ 0 }; i < frame_buffer_count; ++i)
        {
            _cmd_frames[i].wait(_fence_event, _fence);
        }
        _frame_index = 0;
    }

    void release()
    {
        flush();
        core::release(_fence);
        _fence_value = 0;

        CloseHandle(_fence_event);
        _fence_event = nullptr;

        core::release(_cmd_queue);
        core::release(_cmd_list);

        for (u32 i{ 0 }; i < frame_buffer_count; ++i)
        {
            _cmd_frames[i].release();
        }
    }

    [[nodiscard]] constexpr ID3D12CommandQueue *const command_queue() const { return _cmd_queue; }
    [[nodiscard]] constexpr id3d12_graphics_command_list *const command_list() const { return _cmd_list; }
    [[nodiscard]] constexpr u32 frame_index() const { return _frame_index; }

private:
    struct command_frame
    {
        ID3D12CommandAllocator* cmd_allocator{ nullptr };
        u64                     fence_value{ 0 };

        void wait(HANDLE fence_event, ID3D12Fence1* fence)
        {
            assert(fence && fence_event);
            // If the current fence value is still less than "fence_value"
            // then we know the GPU has not finished executing the command lists
            // since it has not reached the "_cmd_queue->Signal()" command
            if (fence->GetCompletedValue() < fence_value)
            {
                // We have the fence create an event wich is singaled once the fence's current value equals "fence_value"
                DXCall(fence->SetEventOnCompletion(fence_value, fence_event));
                // Wait until the fence has triggered the event that its current value has reached "fence_value"
                // indicating that command queue has finished executing.
                WaitForSingleObject(fence_event, INFINITE);
            }
        }

        void release()
        {
            core::release(cmd_allocator);
            fence_value = 0;
        }
    };

    ID3D12CommandQueue*             _cmd_queue{ nullptr };
    id3d12_graphics_command_list*   _cmd_list{ nullptr };
    ID3D12Fence1*                   _fence{ nullptr };
    u64                             _fence_value{ 0 };
    command_frame                   _cmd_frames[frame_buffer_count]{};
    HANDLE                          _fence_event{ nullptr };
    u32                             _frame_index{ 0 };
};

using surface_collection = utl::free_list<d3d12_surface>;

id3d12_device*                  main_device{ nullptr };
IDXGIFactory7*                  dxgi_factory{ nullptr };
d3d12_command                   gfx_command;
surface_collection              surfaces;
d3dx::d3d12_resource_barrier    resource_barriers{};
constant_buffer                 constant_buffers[frame_buffer_count];

descriptor_heap                 rtv_desc_heap{ D3D12_DESCRIPTOR_HEAP_TYPE_RTV };
descriptor_heap                 dsv_desc_heap{ D3D12_DESCRIPTOR_HEAP_TYPE_DSV };
descriptor_heap                 srv_desc_heap{ D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV };
descriptor_heap                 uav_desc_heap{ D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV };

utl::vector<IUnknown*>          deferred_releases[frame_buffer_count]{};
u32                             deferred_releases_flag[frame_buffer_count]{};
std::mutex                      deferred_releases_mutex{};

constexpr D3D_FEATURE_LEVEL     minimum_feature_level{ D3D_FEATURE_LEVEL_11_0 };

bool
failed_init()
{
    shutdown();
    return false;
}

// Get the first most performing adapter that supports the minimum feature level.
// NOTE: this function can be expanded in functionality with, for example, checking if any
//       output devices (i.e. screens) are attached, enumerate the supported resolutions, provide
//       a means for the user to choose which adapter to use in a multi-adapter setting, etc.
IDXGIAdapter4*
determine_main_adapter()
{
    IDXGIAdapter4* adapter{ nullptr };

    // get adapters in descending order of performance
    for (u32 i{ 0 };
         dxgi_factory->EnumAdapterByGpuPreference(i, DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, IID_PPV_ARGS(&adapter)) != DXGI_ERROR_NOT_FOUND;
         ++i)
    {
        // pick the first adapter that supports the minimum feature level.
        if (SUCCEEDED(D3D12CreateDevice(adapter, minimum_feature_level, __uuidof(ID3D12Device), nullptr)))
        {
            return adapter;
        }
        release(adapter);
    }

    return nullptr;
}

D3D_FEATURE_LEVEL
get_max_feature_level(IDXGIAdapter4* adapter)
{
    constexpr D3D_FEATURE_LEVEL feature_levels[4]{
        D3D_FEATURE_LEVEL_11_0,
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_12_0,
        D3D_FEATURE_LEVEL_12_1,
    };

    D3D12_FEATURE_DATA_FEATURE_LEVELS feature_level_info{};
    feature_level_info.NumFeatureLevels = _countof(feature_levels);
    feature_level_info.pFeatureLevelsRequested = feature_levels;

    ComPtr<ID3D12Device> device;
    DXCall(D3D12CreateDevice(adapter, minimum_feature_level, IID_PPV_ARGS(&device)));
    DXCall(device->CheckFeatureSupport(D3D12_FEATURE_FEATURE_LEVELS, &feature_level_info, sizeof(feature_level_info)));
    return feature_level_info.MaxSupportedFeatureLevel;
}

void __declspec(noinline)
process_deferred_releases(u32 frame_idx)
{
    std::lock_guard lock{ deferred_releases_mutex };

    // NOTE: we clear this flag in the beginning. If we'd clear it at the end
    //       then it might overwrite some other thread that was trying to set it.
    //       It's fine if overwriting happens before processing the items.
    deferred_releases_flag[frame_idx] = 0;

    rtv_desc_heap.process_deferred_free(frame_idx);
    dsv_desc_heap.process_deferred_free(frame_idx);
    srv_desc_heap.process_deferred_free(frame_idx);
    uav_desc_heap.process_deferred_free(frame_idx);

    utl::vector<IUnknown*>& resources{ deferred_releases[frame_idx] };
    if (!resources.empty())
    {
        for (auto& resource : resources) release(resource);
        resources.clear();
    }
}

d3d12_frame_info
get_d3d12_frame_info(const frame_info& info, constant_buffer& cbuffer,
                     const d3d12_surface& surface, u32 frame_idx, f32 delta_time)
{
    camera::d3d12_camera& camera{ camera::get(info.camer_id) };
    camera.update();
    hlsl::GlobalShaderData data{};

    using namespace DirectX;
    XMStoreFloat4x4A(&data.View, camera.view());
    XMStoreFloat4x4A(&data.Projection, camera.projection());
    XMStoreFloat4x4A(&data.InvProjection, camera.inverse_projection());
    XMStoreFloat4x4A(&data.ViewProjection, camera.view_projection());
    XMStoreFloat4x4A(&data.InvViewProjection, camera.inverse_view_projection());
    XMStoreFloat3(&data.CameraPosition, camera.position());
    XMStoreFloat3(&data.CameraDirection, camera.direction());
    data.ViewWidth = surface. viewport().Width;
    data.ViewHeight = surface.viewport().Height;
    data.NumDirectionalLights = light::non_cullable_light_count(info.light_set_key);
    data.DeltaTime = delta_time;

    // NOTE: be careful not to read from this buffer. Reads are really really slow.
    hlsl::GlobalShaderData *const shader_data{ cbuffer.allocate<hlsl::GlobalShaderData>() };
    // TODO: handle the case when cbuffer is full.
    memcpy(shader_data, &data, sizeof(hlsl::GlobalShaderData));

    d3d12_frame_info d3d12_info
    {
        &info,
        &camera,
        cbuffer.gpu_address(shader_data),
        surface.width(),
        surface.height(),
        surface.light_culling_id(),
        frame_idx,
        delta_time
    };

    return d3d12_info;
}

} // anonymous namespace

namespace detail {
void deferred_release(IUnknown* resource)
{
    const u32 frame_idx{ current_frame_index() };
    std::lock_guard lock{ deferred_releases_mutex };
    deferred_releases[frame_idx].push_back(resource);
    set_deferred_releases_flag();
}
} // detail namespace

bool
initialize()
{
    // determine what is the maximum feature level that is supported
    // create a ID3D12Device (this a virtual adapter).

    if (main_device) shutdown();

    u32 dxgi_factory_flags{ 0 };
#ifdef _DEBUG
    // Enable debugging layer. Requires "Graphics Tools" optional feature
    {
        ComPtr<ID3D12Debug3> debug_interface;
        if (SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&debug_interface))))
        {
            debug_interface->EnableDebugLayer();
#if 0
#pragma message("WARNING: GPU_based validation is enabled. This will considerably slow down the renderer!")
            debug_interface->SetEnableGPUBasedValidation(1);
#endif
        }
        else
        {
            OutputDebugStringA("Warning: D3D12 Debug interface is not available. Verify that Graphics Tools optional feature is installed on this system.\n");
        }

        dxgi_factory_flags |= DXGI_CREATE_FACTORY_DEBUG;
    }
#endif // _DEBUG

    HRESULT hr{ S_OK };
    DXCall(hr = CreateDXGIFactory2(dxgi_factory_flags, IID_PPV_ARGS(&dxgi_factory)));
    if (FAILED(hr)) return failed_init();

    // determine which adapter (i.e. graphics card) to use, if any
    ComPtr<IDXGIAdapter4> main_adapter;
    main_adapter.Attach(determine_main_adapter());
    if (!main_adapter) return failed_init();

    D3D_FEATURE_LEVEL max_feature_level{ get_max_feature_level(main_adapter.Get()) };
    assert(max_feature_level >= minimum_feature_level);
    if (max_feature_level < minimum_feature_level) return failed_init();

    DXCall(hr = D3D12CreateDevice(main_adapter.Get(), max_feature_level, IID_PPV_ARGS(&main_device)));
    if (FAILED(hr)) return failed_init();

#ifdef _DEBUG
    {
        ComPtr<ID3D12InfoQueue> info_queue;
        DXCall(main_device->QueryInterface(IID_PPV_ARGS(&info_queue)));

        info_queue->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_CORRUPTION, true);
        info_queue->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_WARNING, true);
        info_queue->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_ERROR, true);
    }
#endif // _DEBUG

    bool result{ true };
    result &= rtv_desc_heap.initialize(512, false);
    result &= dsv_desc_heap.initialize(512, false);
    result &= srv_desc_heap.initialize(4096, true);
    result &= uav_desc_heap.initialize(512, false);
    if (!result) return failed_init();

    for (u32 i{ 0 }; i < frame_buffer_count; ++i)
    {
        new (&constant_buffers[i])
            constant_buffer{ constant_buffer::get_default_init_info(1024 * 1024) };
        NAME_D3D12_OBJECT_INDEXED(constant_buffers[i].buffer(), i, L"Global Constant Buffer");
    }

    new (&gfx_command) d3d12_command(main_device, D3D12_COMMAND_LIST_TYPE_DIRECT);
    if (!gfx_command.command_queue()) return failed_init();

    // initialize modules
    if (!(shaders::initialize() &&
          gpass::initialize() &&
          fx::initialize() &&
          upload::initialize() &&
          content::initialize() &&
          delight::initialize()))
        return failed_init();

    NAME_D3D12_OBJECT(main_device, L"Main D3D12 Device");
    NAME_D3D12_OBJECT(rtv_desc_heap.heap(), L"RTV Descriptor Heap");
    NAME_D3D12_OBJECT(dsv_desc_heap.heap(), L"DSV Descriptor Heap");
    NAME_D3D12_OBJECT(srv_desc_heap.heap(), L"SRV Descriptor Heap");
    NAME_D3D12_OBJECT(uav_desc_heap.heap(), L"UAV Descriptor Heap");

    return true;
}

void
shutdown()
{
    gfx_command.release();

    // NOTE: we don't call process_deferred_releases at the end because
    //       some resources (such as swap chains) can't be released before
    //       their depending resources are released.
    for (u32 i{ 0 }; i < frame_buffer_count; ++i)
    {
        process_deferred_releases(i);
    }

    // shutdown modules
    delight::shutdown();
    content::shutdown();
    upload::shutdown();
    fx::shutdown();
    gpass::shutdown();
    shaders::shutdown();

    release(dxgi_factory);

    for (u32 i{ 0 }; i < frame_buffer_count; ++i)
    {
        constant_buffers[i].release();
    }

    // NOTE: some modules free their descriptors when they shutdown.
    //       We process those by calling process_deferred_free once more.
    rtv_desc_heap.process_deferred_free(0);
    dsv_desc_heap.process_deferred_free(0);
    srv_desc_heap.process_deferred_free(0);
    uav_desc_heap.process_deferred_free(0);

    rtv_desc_heap.release();
    dsv_desc_heap.release();
    srv_desc_heap.release();
    uav_desc_heap.release();

    // NOTE: some types only use deferred release for their resources during
    //       shutdown/reset/clear. To finally release these resources we call
    //       process_deferred_releases once more.
    process_deferred_releases(0);

#ifdef _DEBUG
    {
        {
            ComPtr<ID3D12InfoQueue> info_queue;
            DXCall(main_device->QueryInterface(IID_PPV_ARGS(&info_queue)));
            info_queue->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_CORRUPTION, false);
            info_queue->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_WARNING, false);
            info_queue->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_ERROR, false);
        }

        ComPtr<ID3D12DebugDevice2> debug_device;
        DXCall(main_device->QueryInterface(IID_PPV_ARGS(&debug_device)));
        release(main_device);
        DXCall(debug_device->ReportLiveDeviceObjects(
            D3D12_RLDO_SUMMARY | D3D12_RLDO_DETAIL | D3D12_RLDO_IGNORE_INTERNAL));
    }
#endif // _DEBUG

    release(main_device);
}

id3d12_device *const
device() { return main_device; }

descriptor_heap&
rtv_heap() { return rtv_desc_heap; }

descriptor_heap&
dsv_heap() { return dsv_desc_heap; }

descriptor_heap&
srv_heap() { return srv_desc_heap; }

descriptor_heap&
uav_heap() { return uav_desc_heap; }

constant_buffer&
cbuffer() { return constant_buffers[current_frame_index()]; }

u32
current_frame_index() { return gfx_command.frame_index(); }

void
set_deferred_releases_flag() { deferred_releases_flag[current_frame_index()] = 1; }

surface
create_surface(platform::window window)
{
    surface_id id{ surfaces.add(window) };
    surfaces[id].create_swap_chain(dxgi_factory, gfx_command.command_queue());
    return surface{ id };
}

void
remove_surface(surface_id id)
{
    gfx_command.flush();
    surfaces.remove(id);
}

void
resize_surface(surface_id id, u32, u32)
{
    gfx_command.flush();
    surfaces[id].resize();
}

u32
surface_width(surface_id id)
{
    return surfaces[id].width();
}

u32
surface_height(surface_id id)
{
    return surfaces[id].height();
}

void
render_surface(surface_id id, frame_info info)
{
    // Wait for the GPU to finish with the command allocator and
    // reset the allocator once the GPU is done with it.
    // This frees the memory that was used to store commands.
    gfx_command.begin_frame();
    id3d12_graphics_command_list* cmd_list{ gfx_command.command_list() };

    const u32 frame_idx{ current_frame_index() };

    // Reset (clear) the global constant buffer for the current frame.
    constant_buffer& cbuffer{ constant_buffers[frame_idx] };
    cbuffer.clear();

    if (deferred_releases_flag[frame_idx])
    {
        process_deferred_releases(frame_idx);
    }

    const d3d12_surface& surface{ surfaces[id] };
    ID3D12Resource *const current_back_buffer{ surface.back_buffer() };

    const d3d12_frame_info d3d12_info{
        get_d3d12_frame_info(info, cbuffer, surface, frame_idx, 16.7f) };
    

    gpass::set_size({ d3d12_info.surface_width, d3d12_info.surface_height });
    d3dx::d3d12_resource_barrier& barriers{ resource_barriers };

    // Record commands
    ID3D12DescriptorHeap *const heaps[]{ srv_desc_heap.heap() };
    cmd_list->SetDescriptorHeaps(1, &heaps[0]);

    cmd_list->RSSetViewports(1, &surface.viewport());
    cmd_list->RSSetScissorRects(1, &surface.scissor_rect());

    // Depth prepass
    barriers.add(current_back_buffer,
                 D3D12_RESOURCE_STATE_PRESENT,
                 D3D12_RESOURCE_STATE_RENDER_TARGET,
                 D3D12_RESOURCE_BARRIER_FLAG_BEGIN_ONLY);
    gpass::add_transitions_for_depth_prepass(barriers);
    barriers.apply(cmd_list);
    gpass::set_render_targets_for_depth_prepass(cmd_list);
    gpass::depth_prepass(cmd_list, d3d12_info);

    // Geometry and lighting pass
    light::update_light_buffers(d3d12_info);
    delight::cull_lights(cmd_list, d3d12_info, barriers);
    gpass::add_transitions_for_gpass(barriers);
    barriers.apply(cmd_list);
    gpass::set_render_targets_for_gpass(cmd_list);
    gpass::render(cmd_list, d3d12_info);

    // Post-process
    barriers.add(current_back_buffer,
                 D3D12_RESOURCE_STATE_PRESENT,
                 D3D12_RESOURCE_STATE_RENDER_TARGET,
                 D3D12_RESOURCE_BARRIER_FLAG_END_ONLY);
    gpass::add_transitions_for_post_process(barriers);
    barriers.apply(cmd_list);
    // Will write to the current back buffer, so back buffer is a render target
    fx::post_process(cmd_list, d3d12_info, surface.rtv());
    // after post process
    d3dx::transition_resource(cmd_list, current_back_buffer,
                              D3D12_RESOURCE_STATE_RENDER_TARGET,
                              D3D12_RESOURCE_STATE_PRESENT);

    // Done recording commands. Now execute commands,
    // signal and increment the fence value for next frame.
    gfx_command.end_frame(surface);
}
}