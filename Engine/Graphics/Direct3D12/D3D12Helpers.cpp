// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "D3D12Helpers.h"
#include "D3D12Core.h"

namespace primal::graphics::d3d12::d3dx {
namespace {

} // anonymous namespace

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

}