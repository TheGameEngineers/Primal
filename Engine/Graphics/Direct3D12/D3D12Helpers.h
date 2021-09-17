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

ID3D12RootSignature* create_root_signature(const D3D12_ROOT_SIGNATURE_DESC1& desc);

struct d3d12_descriptor_range : public D3D12_DESCRIPTOR_RANGE1
{
    constexpr explicit d3d12_descriptor_range(D3D12_DESCRIPTOR_RANGE_TYPE range_type,
                                              u32 descriptor_count, u32 shader_register, u32 space = 0,
                                              D3D12_DESCRIPTOR_RANGE_FLAGS flags = D3D12_DESCRIPTOR_RANGE_FLAG_NONE,
                                              u32 offset_from_table_start = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND)
        : D3D12_DESCRIPTOR_RANGE1{ range_type, descriptor_count, shader_register, space, flags, offset_from_table_start }
    {}
};

struct d3d12_root_parameter : public D3D12_ROOT_PARAMETER1
{
    constexpr void as_constants(u32 num_constants, D3D12_SHADER_VISIBILITY visibility,
                                u32 shader_register, u32 space = 0)
    {
        ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
        ShaderVisibility = visibility;
        Constants.Num32BitValues = num_constants;
        Constants.ShaderRegister = shader_register;
        Constants.RegisterSpace = space;
    }

    constexpr void as_cbv(D3D12_SHADER_VISIBILITY visibility,
                          u32 shader_register, u32 space = 0,
                          D3D12_ROOT_DESCRIPTOR_FLAGS flags = D3D12_ROOT_DESCRIPTOR_FLAG_NONE)
    {
        as_descriptor(D3D12_ROOT_PARAMETER_TYPE_CBV, visibility, shader_register, space, flags);
    }

    constexpr void as_srv(D3D12_SHADER_VISIBILITY visibility,
                          u32 shader_register, u32 space = 0,
                          D3D12_ROOT_DESCRIPTOR_FLAGS flags = D3D12_ROOT_DESCRIPTOR_FLAG_NONE)
    {
        as_descriptor(D3D12_ROOT_PARAMETER_TYPE_SRV, visibility, shader_register, space, flags);
    }

    constexpr void as_uav(D3D12_SHADER_VISIBILITY visibility,
                          u32 shader_register, u32 space = 0,
                          D3D12_ROOT_DESCRIPTOR_FLAGS flags = D3D12_ROOT_DESCRIPTOR_FLAG_NONE)
    {
        as_descriptor(D3D12_ROOT_PARAMETER_TYPE_UAV, visibility, shader_register, space, flags);
    }

    constexpr void as_descriptor_table(D3D12_SHADER_VISIBILITY visibility,
                                       const d3d12_descriptor_range* ranges, u32 range_count)
    {
        ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
        ShaderVisibility = visibility;
        DescriptorTable.NumDescriptorRanges = range_count;
        DescriptorTable.pDescriptorRanges = ranges;
    }

private:
    constexpr void as_descriptor(D3D12_ROOT_PARAMETER_TYPE type, D3D12_SHADER_VISIBILITY visibility,
                                 u32 shader_register, u32 space, D3D12_ROOT_DESCRIPTOR_FLAGS flags)
    {
        ParameterType = type;
        ShaderVisibility = visibility;
        Descriptor.ShaderRegister = shader_register;
        Descriptor.RegisterSpace = space;
        Descriptor.Flags = flags;
    }
};

// Maximum 64 DWORDs (u32's) divided up amongst all root parameters.
// Root constants = 1 DWORD per 32-bit constant
// Root descriptor  (CBV, SRV or UAV) = 2 DWORDs each
// Descriptor table pointer = 1 DWORD
// Static samplers = 0 DWORDs (compiled into shader)
struct d3d12_root_signature_desc : public D3D12_ROOT_SIGNATURE_DESC1
{
    constexpr explicit d3d12_root_signature_desc(const d3d12_root_parameter* parameters,
                                                 u32 parameter_count,
                                                 const D3D12_STATIC_SAMPLER_DESC* static_samplers = nullptr,
                                                 u32 sampler_count = 0, D3D12_ROOT_SIGNATURE_FLAGS flags =
                                                 D3D12_ROOT_SIGNATURE_FLAG_DENY_VERTEX_SHADER_ROOT_ACCESS |
                                                 D3D12_ROOT_SIGNATURE_FLAG_DENY_HULL_SHADER_ROOT_ACCESS |
                                                 D3D12_ROOT_SIGNATURE_FLAG_DENY_DOMAIN_SHADER_ROOT_ACCESS |
                                                 D3D12_ROOT_SIGNATURE_FLAG_DENY_GEOMETRY_SHADER_ROOT_ACCESS |
                                                 D3D12_ROOT_SIGNATURE_FLAG_DENY_AMPLIFICATION_SHADER_ROOT_ACCESS |
                                                 D3D12_ROOT_SIGNATURE_FLAG_DENY_MESH_SHADER_ROOT_ACCESS)
        : D3D12_ROOT_SIGNATURE_DESC1{ parameter_count, parameters, sampler_count, static_samplers, flags }
    {}

    ID3D12RootSignature* create() const
    {
        return create_root_signature(*this);
    }
};

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

// Pipeline State Subobject (PSS) macro
#define PSS(name, ...) using d3d12_pipeline_state_subobject_##name = d3d12_pipeline_state_subobject<__VA_ARGS__>;

PSS(root_signature, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_ROOT_SIGNATURE, ID3D12RootSignature*);
PSS(vs, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_VS, D3D12_SHADER_BYTECODE);
PSS(ps, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_PS, D3D12_SHADER_BYTECODE);
PSS(ds, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_DS, D3D12_SHADER_BYTECODE);
PSS(hs, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_HS, D3D12_SHADER_BYTECODE);
PSS(gs, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_GS, D3D12_SHADER_BYTECODE);
PSS(cs, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_CS, D3D12_SHADER_BYTECODE);
PSS(stream_output, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_STREAM_OUTPUT, D3D12_STREAM_OUTPUT_DESC);
PSS(blend, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_BLEND, D3D12_BLEND_DESC);
PSS(sample_mask, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_SAMPLE_MASK, u32);
PSS(rasterizer, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_RASTERIZER, D3D12_RASTERIZER_DESC);
PSS(depth_stencil, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_DEPTH_STENCIL, D3D12_DEPTH_STENCIL_DESC);
PSS(input_layout, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_INPUT_LAYOUT, D3D12_INPUT_LAYOUT_DESC);
PSS(ib_strip_cut_value, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_IB_STRIP_CUT_VALUE, D3D12_INDEX_BUFFER_STRIP_CUT_VALUE);
PSS(primitive_topology, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_PRIMITIVE_TOPOLOGY, D3D12_PRIMITIVE_TOPOLOGY_TYPE);
PSS(render_target_formats, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_RENDER_TARGET_FORMATS, D3D12_RT_FORMAT_ARRAY);
PSS(depth_stencil_format, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_DEPTH_STENCIL_FORMAT, DXGI_FORMAT);
PSS(sample_desc, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_SAMPLE_DESC, DXGI_SAMPLE_DESC);
PSS(node_mask, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_NODE_MASK, u32);
PSS(cached_pso, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_CACHED_PSO, D3D12_CACHED_PIPELINE_STATE);
PSS(flags, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_FLAGS, D3D12_PIPELINE_STATE_FLAGS);
PSS(depth_stencil1, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_DEPTH_STENCIL1, D3D12_DEPTH_STENCIL_DESC1);
PSS(view_instancing, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_VIEW_INSTANCING, D3D12_VIEW_INSTANCING_DESC);
PSS(as, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_AS, D3D12_SHADER_BYTECODE);
PSS(ms, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_MS, D3D12_SHADER_BYTECODE);

#undef PSS

ID3D12PipelineState* create_pipeline_state(D3D12_PIPELINE_STATE_STREAM_DESC desc);
ID3D12PipelineState* create_pipeline_state(void* stream, u64 stream_size);
}
