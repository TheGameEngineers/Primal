// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "D3D12PostProcess.h"
#include "D3D12Core.h"
#include "D3D12Shaders.h"
#include "D3D12Surface.h"
#include "D3D12GPass.h"
#include "D3D12LightCulling.h"

namespace primal::graphics::d3d12::fx {
namespace {

struct fx_root_param_indices
{
    enum : u32 {
        global_shader_data,
        root_constants,

        // TODO: temporary for visualizing light culling. Remove later.
        frustums,
        light_grid_opaque,

        count
    };
};

ID3D12RootSignature*        fx_root_sig{ nullptr };
ID3D12PipelineState*        fx_pso{ nullptr };

bool
creat_fx_pos_and_root_signature()
{
    assert(!fx_root_sig && !fx_pso);
    // Create FX root signature
    using idx = fx_root_param_indices;
    d3dx::d3d12_root_parameter parameters[idx::count]{};
    parameters[idx::global_shader_data].as_cbv(D3D12_SHADER_VISIBILITY_PIXEL, 0);
    parameters[idx::root_constants].as_constants(1, D3D12_SHADER_VISIBILITY_PIXEL, 1);
    parameters[idx::frustums].as_srv(D3D12_SHADER_VISIBILITY_PIXEL, 0);
    parameters[idx::light_grid_opaque].as_srv(D3D12_SHADER_VISIBILITY_PIXEL, 1);

    d3dx::d3d12_root_signature_desc root_signature{ &parameters[0], _countof(parameters) };
    root_signature.Flags &= ~D3D12_ROOT_SIGNATURE_FLAG_DENY_PIXEL_SHADER_ROOT_ACCESS;
    fx_root_sig = root_signature.create();
    assert(fx_root_sig);
    NAME_D3D12_OBJECT(fx_root_sig, L"Post-process FX Root Signature");

    // Create FX PSO
    struct {
        d3dx::d3d12_pipeline_state_subobject_root_signature         root_signature{ fx_root_sig };
        d3dx::d3d12_pipeline_state_subobject_vs                     vs{ shaders::get_engine_shader(shaders::engine_shader::fullscreen_triangle_vs) };
        d3dx::d3d12_pipeline_state_subobject_ps                     ps{ shaders::get_engine_shader(shaders::engine_shader::post_process_ps) };
        d3dx::d3d12_pipeline_state_subobject_primitive_topology     primitive_topology{ D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE };
        d3dx::d3d12_pipeline_state_subobject_render_target_formats  render_target_formats;
        d3dx::d3d12_pipeline_state_subobject_rasterizer             rasterizer{ d3dx::rasterizer_state.no_cull };
    } stream;

    D3D12_RT_FORMAT_ARRAY rtf_array{};
    rtf_array.NumRenderTargets = 1;
    rtf_array.RTFormats[0] = d3d12_surface::default_back_buffer_format;

    stream.render_target_formats = rtf_array;

    fx_pso = d3dx::create_pipeline_state(&stream, sizeof(stream));
    NAME_D3D12_OBJECT(fx_pso, L"Post-process FX Pipeline State Object");

    return fx_root_sig && fx_pso;
}

} // anonymous namespace

bool
initialize()
{
    return creat_fx_pos_and_root_signature();
}

void
shutdown()
{
    core::release(fx_root_sig);
    core::release(fx_pso);
}

void
post_process(id3d12_graphics_command_list* cmd_list,
             const d3d12_frame_info& d3d12_info,
             D3D12_CPU_DESCRIPTOR_HANDLE target_rtv)
{
    const u32 frame_index{ d3d12_info.frame_index };
    const id::id_type light_culling_id{ d3d12_info.light_culling_id };

    cmd_list->SetGraphicsRootSignature(fx_root_sig);
    cmd_list->SetPipelineState(fx_pso);

    using idx = fx_root_param_indices;
    cmd_list->SetGraphicsRootConstantBufferView(idx::global_shader_data, d3d12_info.global_shader_data);
    cmd_list->SetGraphicsRoot32BitConstant(idx::root_constants, gpass::main_buffer().srv().index, 0);
    cmd_list->SetGraphicsRootShaderResourceView(idx::frustums, delight::frustums(light_culling_id, frame_index));
    cmd_list->SetGraphicsRootShaderResourceView(idx::light_grid_opaque, delight::light_grid_opaque(light_culling_id, frame_index));
    cmd_list->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    // NOTE: we don't need to clear the render target, because each pixel will 
    //       be overwritten by pixels from gpass main buffer.
    //       We also don't need a depth buffer.
    cmd_list->OMSetRenderTargets(1, &target_rtv, 1, nullptr);
    cmd_list->DrawInstanced(3, 1, 0, 0);
}

}