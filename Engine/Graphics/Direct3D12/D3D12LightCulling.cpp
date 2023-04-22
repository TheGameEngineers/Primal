// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "D3D12LightCulling.h"
#include "D3D12Core.h"
#include "Shaders/SharedTypes.h"
#include "D3D12Shaders.h"
#include "D3D12Light.h"
#include "D3D12Camera.h"
#include "D3D12GPass.h"

namespace primal::graphics::d3d12::delight {
namespace {

struct light_culling_root_parameter {
    enum parameter : u32 {
        global_shader_data,
        constants,
        frustums_out_or_index_counter,

        count
    };
};

struct culling_parameters
{
    d3d12_buffer                            frustums;
    hlsl::LightCullingDispatchParameters    grid_frustums_dispatch_params{};
    u32                                     frustum_count{ 0 };
    u32                                     view_width{ 0 };
    u32                                     view_height{ 0 };
    f32                                     camera_fov{ 0.f };
};

struct light_culler
{
    culling_parameters                      cullers[frame_buffer_count]{};
};

ID3D12RootSignature*                        light_culling_root_signature{ nullptr };
ID3D12PipelineState*                        grid_frustum_pso{ nullptr };
utl::free_list<light_culler>                light_cullers;

bool
create_root_signatures()
{
    assert(!light_culling_root_signature);
    using param = light_culling_root_parameter;
    d3dx::d3d12_root_parameter parameters[param::count]{};
    parameters[param::global_shader_data].as_cbv(D3D12_SHADER_VISIBILITY_ALL, 0);
    parameters[param::constants].as_cbv(D3D12_SHADER_VISIBILITY_ALL, 1);
    parameters[param::frustums_out_or_index_counter].as_uav(D3D12_SHADER_VISIBILITY_ALL, 0);

    light_culling_root_signature = d3dx::d3d12_root_signature_desc{ &parameters[0], _countof(parameters) }.create();
    NAME_D3D12_OBJECT(light_culling_root_signature, L"Light Culling Root Signature");

    return light_culling_root_signature != nullptr;
}

bool create_psos()
{
    assert(!grid_frustum_pso);
    struct {
        d3dx::d3d12_pipeline_state_subobject_root_signature root_signature{ light_culling_root_signature };
        d3dx::d3d12_pipeline_state_subobject_cs cs{ shaders::get_engine_shader(shaders::engine_shader::grid_frustums_cs) };
    } stream;

    grid_frustum_pso = d3dx::create_pipeline_state(&stream, sizeof(stream));
    NAME_D3D12_OBJECT(grid_frustum_pso, L"Grid Frustums PSO");

    return grid_frustum_pso != nullptr;
}

void
resize_buffers(culling_parameters& culler)
{
    const u32 frustum_count{ culler.frustum_count };

    const u32 frustum_buffer_size{ sizeof(hlsl::Frustum) * frustum_count };

    d3d12_buffer_init_info info{};
    info.alignment = sizeof(math::v4);
    info.flags = D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;

    if (frustum_buffer_size > culler.frustums.size())
    {
        info.size = frustum_buffer_size;
        culler.frustums = d3d12_buffer{ info, false };
        NAME_D3D12_OBJECT_INDEXED(culler.frustums.buffer(), frustum_count, L"Light Grid Frustums Buffer - count");
    }
}

void
resize(culling_parameters& culler)
{
    constexpr u32 tile_size{ light_culling_tile_size };
    assert(culler.view_width >= tile_size && culler.view_height >= tile_size);
    const math::u32v2 tile_count
    {
        (u32)math::align_size_up<tile_size>(culler.view_width) / tile_size,
        (u32)math::align_size_up<tile_size>(culler.view_height) / tile_size
    };

    culler.frustum_count = tile_count.x * tile_count.y;

    // Dispatch parameters for grid frustums
    {
        hlsl::LightCullingDispatchParameters& params{ culler.grid_frustums_dispatch_params };
        params.NumThreads = tile_count;
        params.NumThreadGroups.x = (u32)math::align_size_up<tile_size>(tile_count.x) / tile_size;
        params.NumThreadGroups.y = (u32)math::align_size_up<tile_size>(tile_count.y) / tile_size;
    }

    resize_buffers(culler);
}

void
calculate_grid_frustums(culling_parameters& culler,
                        id3d12_graphics_command_list *const cmd_list,
                        const d3d12_frame_info d3d12_info,
                        d3dx::d3d12_resource_barrier& barriers)
{
    constant_buffer& cbuffer{ core::cbuffer() };
    hlsl::LightCullingDispatchParameters *const buffer{ cbuffer.allocate<hlsl::LightCullingDispatchParameters>() };
    const hlsl::LightCullingDispatchParameters& params{ culler.grid_frustums_dispatch_params };
    memcpy(buffer, &params, sizeof(hlsl::LightCullingDispatchParameters));

    // Make frustums buffer writable
    // TODO: remove pixel_shader_resource flag (it's only there so we can visualize grid frustums).
    barriers.add(culler.frustums.buffer(),
                 D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,
                 D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
    barriers.apply(cmd_list);

    using param = light_culling_root_parameter;
    cmd_list->SetComputeRootSignature(light_culling_root_signature);
    cmd_list->SetPipelineState(grid_frustum_pso);
    cmd_list->SetComputeRootConstantBufferView(param::global_shader_data, d3d12_info.global_shader_data);
    cmd_list->SetComputeRootConstantBufferView(param::constants, cbuffer.gpu_address(buffer));
    cmd_list->SetComputeRootUnorderedAccessView(param::frustums_out_or_index_counter, culler.frustums.gpu_address());
    cmd_list->Dispatch(params.NumThreadGroups.x, params.NumThreadGroups.y, 1);

    // Make frustums buffer readable
    // NOTE: cull_lights() will apply this transition.
    // TODO: remove pixel_shader_resource flag (it's only there so we can visualize grid frustums).
    barriers.add(culler.frustums.buffer(),
                 D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
                 D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
}

void _declspec(noinline)
resize_and_calculate_grid_frustums(culling_parameters& culler,
                                   id3d12_graphics_command_list *const cmd_list,
                                   const d3d12_frame_info d3d12_info,
                                   d3dx::d3d12_resource_barrier& barriers)
{
    culler.camera_fov = d3d12_info.camera->field_of_view();
    culler.view_width = d3d12_info.surface_width;
    culler.view_height = d3d12_info.surface_height;

    resize(culler);
    calculate_grid_frustums(culler, cmd_list, d3d12_info, barriers);
}
} // anonymous namespace

bool initialize()
{
    return create_root_signatures() && create_psos() && light::initialize();
}

void shutdown()
{
    light::shutdown();
    assert(light_culling_root_signature && grid_frustum_pso);
    core::deferred_release(light_culling_root_signature);
    core::deferred_release(grid_frustum_pso);
}

id::id_type
add_culler()
{
    return light_cullers.add();
}

void
remove_culler(id::id_type id)
{
    assert(id::is_valid(id));
    light_cullers.remove(id);
}

void cull_lights(id3d12_graphics_command_list *const cmd_list,
                 const d3d12_frame_info& d3d12_info,
                 d3dx::d3d12_resource_barrier& barriers)
{
    const id::id_type id{ d3d12_info.light_culling_id };
    assert(id::is_valid(id));
    culling_parameters& culler{ light_cullers[id].cullers[d3d12_info.frame_index] };

    if (d3d12_info.surface_width != culler.view_width ||
        d3d12_info.surface_height != culler.view_height ||
        !math::is_equal(d3d12_info.camera->field_of_view(), culler.camera_fov))
    {
        resize_and_calculate_grid_frustums(culler, cmd_list, d3d12_info, barriers);
    }

    //barriers.apply(cmd_list);
}

// TODO: temporary for visualizing light culling. Remove later.
D3D12_GPU_VIRTUAL_ADDRESS frustums(id::id_type light_culling_id, u32 frame_index)
{
    assert(frame_index < frame_buffer_count && id::is_valid(light_culling_id));
    return light_cullers[light_culling_id].cullers[frame_index].frustums.gpu_address();
}

}