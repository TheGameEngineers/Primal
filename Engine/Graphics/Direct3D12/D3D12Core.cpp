// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "D3D12Core.h"
#include "D3D12Resources.h"
#include "D3D12Surface.h"
#include "D3D12Helpers.h"

using namespace Microsoft::WRL;

namespace primal::graphics::d3d12::core {
// TODO: remove when you're done showing how to create a root signature the tedious way
void create_a_root_signature();
void create_a_root_signature2();

namespace {

class d3d12_command
{
public:
    d3d12_command() = default;
    DISABLE_COPY_AND_MOVE(d3d12_command);
    explicit d3d12_command(ID3D12Device8 *const device, D3D12_COMMAND_LIST_TYPE type)
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
    void end_frame()
    {
        DXCall(_cmd_list->Close());
        ID3D12CommandList *const cmd_lists[]{ _cmd_list };
        _cmd_queue->ExecuteCommandLists(_countof(cmd_lists), &cmd_lists[0]);

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

    constexpr ID3D12CommandQueue *const command_queue() const { return _cmd_queue; }
    constexpr ID3D12GraphicsCommandList6 *const command_list() const { return _cmd_list; }
    constexpr u32 frame_index() const { return _frame_index; }

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

    ID3D12CommandQueue*         _cmd_queue{ nullptr };
    ID3D12GraphicsCommandList6* _cmd_list{ nullptr };
    ID3D12Fence1*               _fence{ nullptr };
    u64                         _fence_value{ 0 };
    command_frame               _cmd_frames[frame_buffer_count]{};
    HANDLE                      _fence_event{ nullptr };
    u32                         _frame_index{ 0 };
};

using surface_collection = utl::free_list<d3d12_surface>;

ID3D12Device8*              main_device{ nullptr };
IDXGIFactory7*              dxgi_factory{ nullptr };
d3d12_command               gfx_command;
surface_collection          surfaces;

descriptor_heap             rtv_desc_heap{ D3D12_DESCRIPTOR_HEAP_TYPE_RTV };
descriptor_heap             dsv_desc_heap{ D3D12_DESCRIPTOR_HEAP_TYPE_DSV };
descriptor_heap             srv_desc_heap{ D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV };
descriptor_heap             uav_desc_heap{ D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV };

utl::vector<IUnknown*>      deferred_releases[frame_buffer_count]{};
u32                         deferred_releases_flag[frame_buffer_count]{};
std::mutex                  deferred_releases_mutex{};

constexpr DXGI_FORMAT       render_target_format{ DXGI_FORMAT_R8G8B8A8_UNORM_SRGB };
constexpr D3D_FEATURE_LEVEL minimum_feature_level{ D3D_FEATURE_LEVEL_11_0 };

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

    new (&gfx_command) d3d12_command(main_device, D3D12_COMMAND_LIST_TYPE_DIRECT);
    if (!gfx_command.command_queue()) return failed_init();

    NAME_D3D12_OBJECT(main_device, L"Main D3D12 Device");
    NAME_D3D12_OBJECT(rtv_desc_heap.heap(), L"RTV Descriptor Heap");
    NAME_D3D12_OBJECT(dsv_desc_heap.heap(), L"DSV Descriptor Heap");
    NAME_D3D12_OBJECT(srv_desc_heap.heap(), L"SRV Descriptor Heap");
    NAME_D3D12_OBJECT(uav_desc_heap.heap(), L"UAV Descriptor Heap");

    // TODO: remove.
    create_a_root_signature();
    create_a_root_signature2();

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

    release(dxgi_factory);

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

ID3D12Device8 *const
device() { return main_device; }

descriptor_heap&
rtv_heap() { return rtv_desc_heap; }

descriptor_heap&
dsv_heap() { return dsv_desc_heap; }

descriptor_heap&
srv_heap() { return srv_desc_heap; }

descriptor_heap&
uav_heap() { return uav_desc_heap; }

DXGI_FORMAT
default_render_target_format() { return render_target_format; }

u32
current_frame_index() { return gfx_command.frame_index(); }

void
set_deferred_releases_flag() { deferred_releases_flag[current_frame_index()] = 1; }

surface
create_surface(platform::window window)
{
    surface_id id{ surfaces.add(window) };
    surfaces[id].create_swap_chain(dxgi_factory, gfx_command.command_queue(), render_target_format);
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
render_surface(surface_id id)
{
    // Wait for the GPU to finish with the command allocator and
    // reset the allocator once the GPU is done with it.
    // This frees the memory that was used to store commands.
    gfx_command.begin_frame();
    ID3D12GraphicsCommandList6* cmd_list{ gfx_command.command_list() };

    const u32 frame_idx{ current_frame_index() };
    if (deferred_releases_flag[frame_idx])
    {
        process_deferred_releases(frame_idx);
    }

    const d3d12_surface& surface{ surfaces[id] };

    // Presenting swap chain buffers happens in lockstep with frame buffers.
    surface.present();
    // Record commands
    // ...
    // 
    // Done recording commands. Now execute commands,
    // signal and increment the fence value for next frame.
    gfx_command.end_frame();
}

// NOTE: this function demonstrates how to create a root signature as en example
//       it will be removed later.
void create_a_root_signature()
{
    D3D12_ROOT_PARAMETER1 params[3];
    {// param 0: 2 constants
        auto& param = params[0];
        param.ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
        D3D12_ROOT_CONSTANTS consts{};
        consts.Num32BitValues = 2;
        consts.ShaderRegister = 0; // b0
        consts.RegisterSpace = 0;
        param.Constants = consts;
        param.ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;
    }
    {// param 1: 1 Constant Buffer View (Descriptor)
        auto& param = params[1];
        param.ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
        D3D12_ROOT_DESCRIPTOR1 root_desc{};
        root_desc.Flags = D3D12_ROOT_DESCRIPTOR_FLAG_NONE;
        root_desc.ShaderRegister = 1;
        root_desc.RegisterSpace = 0;
        param.Descriptor = root_desc;
        param.ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;
    }
    {// param 2: descriptor table (unbounded/bindless)
        auto& param = params[2];
        param.ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
        D3D12_ROOT_DESCRIPTOR_TABLE1 table{};
        table.NumDescriptorRanges = 1;
        D3D12_DESCRIPTOR_RANGE1 range{};
        range.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
        range.NumDescriptors = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;
        range.Flags = D3D12_DESCRIPTOR_RANGE_FLAG_DESCRIPTORS_VOLATILE;
        range.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;
        range.BaseShaderRegister = 0;
        range.RegisterSpace = 0;
        table.pDescriptorRanges = &range;
        param.DescriptorTable = table;
        param.ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;
    }

    D3D12_STATIC_SAMPLER_DESC sampler_desc{};
    sampler_desc.AddressU = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    sampler_desc.AddressV = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    sampler_desc.AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    sampler_desc.ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;

    D3D12_ROOT_SIGNATURE_DESC1 desc{};
    desc.Flags =
        D3D12_ROOT_SIGNATURE_FLAG_DENY_HULL_SHADER_ROOT_ACCESS |
        D3D12_ROOT_SIGNATURE_FLAG_DENY_DOMAIN_SHADER_ROOT_ACCESS |
        D3D12_ROOT_SIGNATURE_FLAG_DENY_GEOMETRY_SHADER_ROOT_ACCESS |
        D3D12_ROOT_SIGNATURE_FLAG_DENY_AMPLIFICATION_SHADER_ROOT_ACCESS |
        D3D12_ROOT_SIGNATURE_FLAG_DENY_MESH_SHADER_ROOT_ACCESS;
    desc.NumParameters = _countof(params);
    desc.pParameters = &params[0];
    desc.NumStaticSamplers = 1;
    desc.pStaticSamplers = &sampler_desc;

    D3D12_VERSIONED_ROOT_SIGNATURE_DESC rs_desc{};
    rs_desc.Version = D3D_ROOT_SIGNATURE_VERSION_1_1;
    rs_desc.Desc_1_1 = desc;

    HRESULT hr{ S_OK };
    ID3DBlob* root_sig_blob{ nullptr };
    ID3DBlob* error_blob{ nullptr };
    if (FAILED(hr = D3D12SerializeVersionedRootSignature(&rs_desc, &root_sig_blob, &error_blob)))
    {
        DEBUG_OP(const char* error_msg{ error_blob ? (const char*)error_blob->GetBufferPointer() : "" });
        DEBUG_OP(OutputDebugStringA(error_msg));
        return;
    }

    assert(root_sig_blob);
    ID3D12RootSignature* root_sig{ nullptr };
    DXCall(hr = device()->CreateRootSignature(0, root_sig_blob->GetBufferPointer(), root_sig_blob->GetBufferSize(), IID_PPV_ARGS(&root_sig)));

    release(root_sig_blob);
    release(error_blob);

    // use root_sig during rendering (not in this function, obviously)
#if 0
    ID3D12GraphicsCommandList6* cmd_list{};
    cmd_list->SetGraphicsRootSignature(root_sig);
    // only one resource heap and one sampler heap can be set at any time
    // So, max number of heaps is 2.
    ID3D12DescriptorHeap* heaps[]{ srv_heap().heap() };
    cmd_list->SetDescriptorHeaps(1, &heaps[0]);

    // set root paramters:
    float dt{ 16.6f };
    u32 dt_uint{ *((u32*)&dt) };
    u32 frame_nr{ 4287827 };
    D3D12_GPU_VIRTUAL_ADDRESS address_of_constant_buffer{/* our constant buffer which we don't have right now*/ };
    cmd_list->SetGraphicsRoot32BitConstant(0, dt_uint, 0);
    cmd_list->SetGraphicsRoot32BitConstant(0, frame_nr, 1);
    cmd_list->SetGraphicsRootConstantBufferView(1, address_of_constant_buffer);
    cmd_list->SetGraphicsRootDescriptorTable(2, srv_heap().gpu_start());
    // record the rest of rendering commands...
#endif

    // when renderer shuts down
    release(root_sig);
}

void create_a_root_signature2()
{
    d3dx::d3d12_descriptor_range range{ D3D12_DESCRIPTOR_RANGE_TYPE_SRV, D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND, 0 };
    d3dx::d3d12_root_parameter params[3]{};
    params[0].as_constants(2, D3D12_SHADER_VISIBILITY_PIXEL, 0);
    params[1].as_cbv(D3D12_SHADER_VISIBILITY_PIXEL, 1);
    params[2].as_descriptor_table(D3D12_SHADER_VISIBILITY_PIXEL, &range, 1);

    d3dx::d3d12_root_signature_desc root_sig_desc{ &params[0], _countof(params) };
    ID3D12RootSignature* root_sig{ root_sig_desc.create() };

    // use root_sig

    // when renderer shuts down
    release(root_sig);
}

ID3D12RootSignature* _root_signature;
D3D12_SHADER_BYTECODE _vs{};

template<D3D12_PIPELINE_STATE_SUBOBJECT_TYPE type, typename T>
class alignas(void*) d3d12_pipeline_state_subobject
{
public:
    d3d12_pipeline_state_subobject() = default;
    constexpr explicit d3d12_pipeline_state_subobject(T subobject) : _type{ type }, _subobject{ subobject }{}
    d3d12_pipeline_state_subobject& operator=(const T& subobject) { _subobject = subobject; return *this; }
private:
    const D3D12_PIPELINE_STATE_SUBOBJECT_TYPE _type{ type };
    T _subobject{};
};

using d3d12_pipeline_state_subobject_root_signature = d3d12_pipeline_state_subobject< D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_ROOT_SIGNATURE, ID3D12RootSignature*>;
using d3d12_pipeline_state_subobject_vs = d3d12_pipeline_state_subobject< D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_VS, D3D12_SHADER_BYTECODE>;


void create_a_pipeline_state_object()
{
    struct {
        struct alignas(void*) {
            const D3D12_PIPELINE_STATE_SUBOBJECT_TYPE type{ D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_ROOT_SIGNATURE };
            ID3D12RootSignature* root_signature;
        } root_sig;
        struct alignas(void*) {
            const D3D12_PIPELINE_STATE_SUBOBJECT_TYPE type{ D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_VS };
            D3D12_SHADER_BYTECODE vs_code{};
        } vs;
    } stream;

    stream.root_sig.root_signature = _root_signature;
    stream.vs.vs_code = _vs;

    D3D12_PIPELINE_STATE_STREAM_DESC desc{};
    desc.pPipelineStateSubobjectStream = &stream;
    desc.SizeInBytes = sizeof(stream);

    ID3D12PipelineState* pso{ nullptr };
    device()->CreatePipelineState(&desc, IID_PPV_ARGS(&pso));

    // use pso during rendering

    //when renderer shuts down
    release(pso);
}

void create_a_pipeline_state_object2()
{
    struct {
        d3dx::d3d12_pipeline_state_subobject_root_signature root_sig{ _root_signature };
        d3dx::d3d12_pipeline_state_subobject_vs vs{ _vs };
    } stream;

    auto pso = d3dx::create_pipeline_state(&stream, sizeof(stream));

    // use pso during rendering

    //when renderer shuts down
    release(pso);
}
}