// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "D3D12Helpers.h"
#include "D3D12Core.h"
#include "D3D12Upload.h"

namespace primal::graphics::d3d12::d3dx {
namespace {

} // anonymous namespace

void
transition_resource(
    id3d12_graphics_command_list* cmd_list,
    ID3D12Resource* resource,
    D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after,
    D3D12_RESOURCE_BARRIER_FLAGS flags/* = D3D12_RESOURCE_BARRIER_FLAG_NONE*/,
    u32 subresource /*= D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES*/)
{
    D3D12_RESOURCE_BARRIER barrier{};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Flags = flags;
    barrier.Transition.pResource = resource;
    barrier.Transition.StateBefore = before;
    barrier.Transition.StateAfter = after;
    barrier.Transition.Subresource = subresource;

    cmd_list->ResourceBarrier(1, &barrier);
}


ID3D12RootSignature*
create_root_signature(const D3D12_ROOT_SIGNATURE_DESC1& desc)
{
    D3D12_VERSIONED_ROOT_SIGNATURE_DESC versioned_desc{};
    versioned_desc.Version = D3D_ROOT_SIGNATURE_VERSION_1_1;
    versioned_desc.Desc_1_1 = desc;

    using namespace Microsoft::WRL;
    ComPtr<ID3DBlob> signature_blob{ nullptr };
    ComPtr<ID3DBlob> error_blob{ nullptr };
    HRESULT hr{ S_OK };
    if (FAILED(hr = D3D12SerializeVersionedRootSignature(&versioned_desc, &signature_blob, &error_blob)))
    {
        DEBUG_OP(const char* error_msg{ error_blob ? (const char*)error_blob->GetBufferPointer() : "" });
        DEBUG_OP(OutputDebugStringA(error_msg));
        return nullptr;
    }

    ID3D12RootSignature* signature{ nullptr };
    DXCall(hr = core::device()->CreateRootSignature(0, signature_blob->GetBufferPointer(),
                                                    signature_blob->GetBufferSize(), IID_PPV_ARGS(&signature)));

    if (FAILED(hr))
    {
        core::release(signature);
    }

    return signature;
}

ID3D12PipelineState*
create_pipeline_state(D3D12_PIPELINE_STATE_STREAM_DESC desc)
{
    assert(desc.pPipelineStateSubobjectStream && desc.SizeInBytes);
    ID3D12PipelineState* pso{ nullptr };
    DXCall(core::device()->CreatePipelineState(&desc, IID_PPV_ARGS(&pso)));
    assert(pso);
    return pso;
}


ID3D12PipelineState*
create_pipeline_state(void* stream, u64 stream_size)
{
    assert(stream && stream_size);
    D3D12_PIPELINE_STATE_STREAM_DESC desc{};
    desc.SizeInBytes = stream_size;
    desc.pPipelineStateSubobjectStream = stream;
    return create_pipeline_state(desc);
}

ID3D12Resource*
create_buffer(const void* data, u32 buffer_size, bool is_cpu_accessible /* = false */,
              D3D12_RESOURCE_STATES state /* = D3D12_RESOURCE_STATE_COMMON */,
              D3D12_RESOURCE_FLAGS flags /* = D3D12_RESOURCE_FLAG_NONE */,
              ID3D12Heap* heap /* = nullptr */, u64 heap_offset /* = 0 */)
{
    assert(buffer_size);

    D3D12_RESOURCE_DESC desc{};
    desc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    desc.Alignment = 0;
    desc.Width = buffer_size;
    desc.Height = 1;
    desc.DepthOrArraySize = 1;
    desc.MipLevels = 1;
    desc.Format = DXGI_FORMAT_UNKNOWN;
    desc.SampleDesc = { 1,0 };
    desc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    desc.Flags = is_cpu_accessible ? D3D12_RESOURCE_FLAG_NONE : flags;

    // The buffer will be only used for upload or as constant buffer/UAV.
    assert(desc.Flags == D3D12_RESOURCE_FLAG_NONE ||
           desc.Flags == D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);

    ID3D12Resource* resource{ nullptr };
    const D3D12_RESOURCE_STATES resource_state
    { is_cpu_accessible ? D3D12_RESOURCE_STATE_GENERIC_READ : state };

    if (heap)
    {
        DXCall(core::device()->CreatePlacedResource(
            heap, heap_offset, &desc, resource_state,
            nullptr, IID_PPV_ARGS(&resource)));
    }
    else
    {
        DXCall(core::device()->CreateCommittedResource(
            is_cpu_accessible ? &heap_properties.upload_heap : &heap_properties.default_heap,
            D3D12_HEAP_FLAG_NONE, &desc, resource_state,
            nullptr, IID_PPV_ARGS(&resource)));
    }

    if (data)
    {
        // If we have initial data which we'd like to be able to change later, we set is_cpu_accessible
        // to true. If we only want to upload some data once to be used by the GPU, then is_cpu_accessible
        // should be set to false.
        if (is_cpu_accessible)
        {
            // NOTE: range's Begin and End fields are set to 0, to indicate that
            //       the CPU is not reading any data (i.e. write-only)
            const D3D12_RANGE range{};
            void* cpu_address{ nullptr };
            DXCall(resource->Map(0, &range, reinterpret_cast<void**>(&cpu_address)));
            assert(cpu_address);
            memcpy(cpu_address, data, buffer_size);
            resource->Unmap(0, nullptr);
        }
        else
        {
            upload::d3d12_upload_context context{ buffer_size };
            memcpy(context.cpu_address(), data, buffer_size);
            context.command_list()->CopyResource(resource, context.upload_buffer());
            context.end_upload();
        }
    }

    assert(resource);
    return resource;
}

}