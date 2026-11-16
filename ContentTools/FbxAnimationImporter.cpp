// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "FbxImporter.h"
#include "Geometry.h"
#include "Profiling/UdpLog.h"
#include <algorithm>
#include <string>

namespace primal::tools {
namespace {
using profiling::udp_log::message_type;

} // anonymous namespace

void
fbx_context::get_animation_data(const utl::vector<FbxNode*>& joint_nodes, mesh& m)
{
    if (!m.settings.import_animations || !_fbx_scene || joint_nodes.empty()) return;

    FbxAnimEvaluator* evaluator{ _fbx_scene->GetAnimationEvaluator() };
    const s32 num_stacks{ _fbx_scene->GetSrcObjectCount<FbxAnimStack>() };
    if (!evaluator || num_stacks <= 0) return;

    const u32 num_joints{ (u32)joint_nodes.size() };
    assert(m.joint_hierarchy.size() == num_joints);
    utl::vector<FbxNode*> joint_parents(num_joints, nullptr);
    for (u32 joint_idx{ 0 }; joint_idx < num_joints; ++joint_idx)
    {
        const u32 parent_index{ m.joint_hierarchy[joint_idx] };
        joint_parents[joint_idx] = parent_index != u32_invalid_id ? joint_nodes[parent_index] : nullptr;
    }

    const FbxTime::EMode time_mode{ _fbx_scene->GetGlobalSettings().GetTimeMode() };
    FbxTime step;
    step.SetTime(0, 0, 0, 1, 0, time_mode);
    const FbxLongLong step_ticks{ step.Get() };
    if (step_ticks <= 0) return;

    const f32 frames_per_second{ (f32)FbxTime::GetFrameRate(time_mode) };
    FbxAnimStack *const previous_stack{ _fbx_scene->GetCurrentAnimationStack() };

    for (s32 stack_idx{ 0 }; stack_idx < num_stacks; ++stack_idx)
    {
        FbxAnimStack* stack{ _fbx_scene->GetSrcObject<FbxAnimStack>(stack_idx) };
        if (!stack) continue;
        _fbx_scene->SetCurrentAnimationStack(stack);

        FbxTimeSpan span{ stack->GetLocalTimeSpan() };
        if (span.GetDuration().Get() <= 0)
        {
            if (FbxTakeInfo * take{ _fbx_scene->GetTakeInfo(stack->GetName()) })
                span = take->mLocalTimeSpan;
        }

        const FbxLongLong start{ span.GetStart().Get() };
        const FbxLongLong stop{ span.GetStop().Get() };
        if (stop < start) continue;

        utl::vector<FbxLongLong> ticks;
        for (FbxLongLong tick{ start }; tick < stop; tick += step_ticks)
        {
            ticks.emplace_back(tick);
        }
        ticks.emplace_back(stop);

        animation_clip clip{};
        clip.name = stack->GetName();
        if (clip.name.empty()) clip.name = "Anim" + std::to_string(stack_idx);
        clip.frames_per_second = frames_per_second;
        clip.frame_count = (u32)ticks.size();
        const u64 sample_count{ (u64)clip.frame_count * num_joints };
        clip.translations.reserve(sample_count);
        clip.rotations.reserve(sample_count);
        clip.scales.reserve(sample_count);

        for (const FbxLongLong tick : ticks)
        {
            FbxTime time;
            time.Set(tick);

            for (u32 joint_idx{ 0 }; joint_idx < num_joints; ++joint_idx)
            {
                // Rebase against the nearest exported joint to include skipped helper transforms.
                // A root uses its global transform, including transforms above the skeleton root.
                FbxNode* joint{ joint_nodes[joint_idx] };
                const FbxAMatrix global{ evaluator->GetNodeGlobalTransform(joint, time) };
                FbxAMatrix local{ global };
                if (joint_parents[joint_idx])
                {
                    const FbxAMatrix parent_global{
                        evaluator->GetNodeGlobalTransform(joint_parents[joint_idx], time) };
                    local = parent_global.Inverse() * global;
                }

                FbxVector4 t{ local.GetT() * (double)_scene_scale };    // translation
                const FbxQuaternion q{ local.GetQ() };                  // rotation quaternion
                const FbxVector4 s{ local.GetS() };                     // scale

                clip.translations.emplace_back((f32)t[0], (f32)t[1], (f32)t[2]);
                clip.rotations.emplace_back((f32)q[0], (f32)q[1], (f32)q[2], (f32)q[3]);
                clip.scales.emplace_back((f32)s[0], (f32)s[1], (f32)s[2]);
            }
        }

        m.animation_clips.emplace_back(clip);
    }

    _fbx_scene->SetCurrentAnimationStack(previous_stack);
}

} // namespace primal::tools