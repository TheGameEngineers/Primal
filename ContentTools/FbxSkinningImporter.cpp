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

u32
joint_index_of(const utl::vector<FbxNode*>& nodes, FbxNode* node)
{
    const auto it{ std::find(nodes.begin(), nodes.end(), node) };
    return it == nodes.end() ? u32_invalid_id : (u32)(it - nodes.begin());
}

// Scene preorder. Skeleton nodes and nodes actually used as cluster links, so a
// null used as a bone is included without pulling in unrelated transforms.
void
collect_joint_nodes(FbxNode* node, const utl::vector<FbxNode*>& cluster_joints, utl::vector<FbxNode*>& joint_nodes)
{
    if (!node) return;

    const bool is_cluster_joint{ joint_index_of(cluster_joints, node) != u32_invalid_id };
    if ((node->GetSkeleton() || is_cluster_joint) &&
        joint_index_of(joint_nodes, node) == u32_invalid_id)
    {
        joint_nodes.emplace_back(node);
    }

    for (s32 i{ 0 }; i < node->GetChildCount(); ++i)
    {
        collect_joint_nodes(node->GetChild(i), cluster_joints, joint_nodes);
    }
}

bool
get_bind_pose_matrix(FbxScene* scene, FbxNode* node, FbxAMatrix& matrix)
{
    if (!scene || !node) return false;

    for (s32 pose_idx{ 0 }; pose_idx < scene->GetPoseCount(); ++pose_idx)
    {
        FbxPose* pose{ scene->GetPose(pose_idx) };
        if (!pose || !pose->IsBindPose()) continue;

        const s32 node_idx{ pose->Find(node) };
        if (node_idx < 0) continue;

        const FbxMatrix& bind_pose{ pose->GetMatrix(node_idx) };

        for (u32 row{ 0 }; row < 4; ++row)
            for (u32 col{ 0 }; col < 4; ++col)
                matrix[row][col] = bind_pose.Get(row, col);

        return true;
    }

    return false;
}

// parent_indices must be nearest-ancestor links. A repeated node along a chain
// means a cycle; cut the edge that closes it so the graph is a forest.
void
break_parent_cycles(utl::vector<u32>& parent_indices)
{
    const u32 num_joints{ (u32)parent_indices.size() };
    // 0 -> not seen yet
    // 1 -> on the chain currently being walked
    // 2 -> already finished, and its chain is known to be acyclic
    utl::vector<u8> marker(num_joints, 0);
    utl::vector<u32> chain;

    for (u32 start{ 0 }; start < num_joints; ++start)
    {
        if (marker[start]) continue;

        chain.clear();
        u32 index{ start };
        while (index != u32_invalid_id && !marker[index])
        {
            marker[index] = 1;
            chain.emplace_back(index);
            index = parent_indices[index];
        }

        if (index != u32_invalid_id && marker[index] == 1 && !chain.empty())
            parent_indices[chain.back()] = u32_invalid_id;

        for (const u32 node : chain)
        {
            marker[node] = 2;
        }
    }
}

// Top skeleton joint. Parents that are not skeleton nodes are not part of the rig root.
FbxNode*
skeleton_root(FbxNode* node)
{
    FbxNode* root{ nullptr };
    for (FbxNode* current{ node }; current; current = current->GetParent())
    {
        if (current->GetSkeleton()) root = current;
        else if (root) break;
    }
    return root;
}

void
note_imported_skeleton_root(utl::vector<FbxUInt64>& roots, const utl::vector<FbxNode*>& joint_nodes)
{
    // joint_nodes[0] may be a null cluster link above the first skeleton joint.
    // Store the skeleton root so it matches the id get_skeletons compares.
    FbxNode* root{ nullptr };
    for (FbxNode* joint : joint_nodes)
    {
        if (!joint || !joint->GetSkeleton()) continue;
        root = skeleton_root(joint);
        break;
    }
    if (!root) return;

    const FbxUInt64 root_id{ root->GetUniqueID() };
    if (std::find(roots.begin(), roots.end(), root_id) == roots.end())
        roots.emplace_back(root_id);
}


// True when node, or a transform above it, is a root already taken by a skinned mesh.
// The parent walk covers a cluster link that sits above the first skeleton node.
bool
is_imported_skeleton(const utl::vector<u64>& roots, FbxNode* node)
{
    FbxNode* root{ skeleton_root(node) };
    if (!root) return false;
    const u64 root_id{ root->GetUniqueID() };
    return std::find(roots.begin(), roots.end(), root_id) != roots.end();
}

bool
get_joint_hierarchy(
    FbxScene* scene,
    f32 scene_scale,
    const utl::vector<std::pair<FbxNode*, FbxAMatrix>>& cluster_bind_poses,
    utl::vector<FbxNode*>& joint_nodes,
    mesh& m)
{
    const u32 num_source_joints{ (u32)joint_nodes.size() };
    utl::vector<u32> parent_indices(num_source_joints, u32_invalid_id);
    utl::vector<FbxNode*> seen;

    for (u32 i{ 0 }; i < num_source_joints; ++i)
    {
        seen.clear();
        seen.emplace_back(joint_nodes[i]);
        for (FbxNode* parent{ joint_nodes[i]->GetParent() }; parent; parent = parent->GetParent())
        {
            if (std::find(seen.begin(), seen.end(), parent) != seen.end()) break;
            seen.emplace_back(parent);

            const u32 parent_index{ joint_index_of(joint_nodes, parent) };
            if (parent_index != u32_invalid_id)
            {
                parent_indices[i] = parent_index;
                break;
            }
        }
    }

    break_parent_cycles(parent_indices);

    // Find the root joint's index
    // One joint tree per skeletal mesh. Bones this mesh does not weight stay in
    // that tree so the skeleton matches other meshes on the same rig.
    u32 root_index{ u32_invalid_id };
    bool multiple_roots{ false };
    const u32 num_vertices{ (u32)m.joint_indices.size() };

    for (u32 vertex_idx{ 0 }; vertex_idx < num_vertices && !multiple_roots; ++vertex_idx)
    {
        const u32* joint_indices{ &m.joint_indices[vertex_idx].x };
        const f32* joint_weights{ &m.joint_weights[vertex_idx].x };
        for (u32 i{ 0 }; i < mesh::max_joints_per_vertex; ++i)
        {
            if (joint_weights[i] <= 0.f || joint_indices[i] >= num_source_joints) continue;

            u32 weighted_root{ joint_indices[i] };
            while (parent_indices[weighted_root] != u32_invalid_id)
            {
                weighted_root = parent_indices[weighted_root];
            }

            if (root_index == u32_invalid_id)
            {
                root_index = weighted_root;
            }
            else if (root_index != weighted_root)
            {
                multiple_roots = true;
                break;
            }
        }
    }

    // No influences: this joint list is a skeleton that is not tied to a mesh.
    // One parentless joint is that skeleton's root.
    if (!multiple_roots && root_index == u32_invalid_id && num_vertices == 0)
    {
        u32 root_count{ 0 };
        for (u32 i{ 0 }; i < num_source_joints; ++i)
        {
            if (parent_indices[i] != u32_invalid_id) continue;
            root_index = i;
            ++root_count;
        }
        if (root_count != 1) root_index = u32_invalid_id;
    }

    if (multiple_roots || root_index == u32_invalid_id)
    {
        SEND_UDP_LOG(message_type::error, multiple_roots ?
            ("Skeletal mesh " + m.name + " has more than one root joint").c_str() :
            ("No skinned joints for " + m.name).c_str());
        return false;
    }

    // Create the joint index hierarchy
    utl::vector<u32> hierarchy;
    utl::vector<u32> hierarchy_map;
    hierarchy.reserve(num_source_joints);
    hierarchy_map.reserve(num_source_joints);

    struct index_pair { u32 index; u32 parent_index; };
    utl::vector<index_pair> pending;
    pending.emplace_back(root_index, u32_invalid_id);
    // Each joint has one parent. Children are the entries in that flat array.
    while (pending.size())
    {
        const index_pair pair{ pending.back() };
        pending.pop_back();

        const u32 hierarchy_index{ (u32)hierarchy_map.size() };
        hierarchy_map.emplace_back(pair.index);
        hierarchy.emplace_back(pair.parent_index);

        for (u32 child_index{ num_source_joints }; child_index > 0; --child_index)
        {
            if (parent_indices[child_index - 1] == pair.index)
            {
                pending.emplace_back(child_index - 1, hierarchy_index);
            }
        }
    }

    const u32 num_joints{ (u32)hierarchy_map.size() };
    // Joints this root cannot reach belong to another skeleton. Leave them out
    // so get_skeletons can import that rig. One rig still keeps every bone in its tree.
    assert(num_joints > 0 && hierarchy[0] == u32_invalid_id);
    // Check if each parent was written before its children, so this is a single tree.
    assert(([&] {
        for (u32 i{ 1 }; i < num_joints; ++i)
            if (hierarchy[i] >= i) return false;
        return true;
        }()));

    m.joint_hierarchy.swap(hierarchy);
    m.inverse_bind_pose.resize(num_joints);
    m.joint_names.resize(num_joints);
    utl::vector<FbxNode*> new_joint_nodes(num_joints);
    utl::vector<u32> source_to_hierarchy(num_source_joints, 0);
    // Get bind poses and joint names, and reorder joint_nodes to match the joint hierarchy
    for (u32 i{ 0 }; i < num_joints; ++i)
    {
        const u32 source_index{ hierarchy_map[i] };
        source_to_hierarchy[source_index] = i;
        FbxNode* joint{ joint_nodes[source_index] };
        new_joint_nodes[i] = joint;
        m.joint_names[i] = joint->GetName();

        FbxAMatrix bind_pose;
        const auto it{ std::find_if(cluster_bind_poses.begin(), cluster_bind_poses.end(),
            [joint](const auto& bind_pose) { return bind_pose.first == joint; }) };

        if (it != cluster_bind_poses.end())
        {
            bind_pose = it->second;
        }
        else if (!get_bind_pose_matrix(scene, joint, bind_pose))
        {
            SEND_UDP_LOG(message_type::warning,
                ("Missing bind pose for joint " + std::string(joint->GetName()) + " in " + m.name).c_str());
            bind_pose = joint->EvaluateGlobalTransform();
        }

        // Vertices are stored in meters. Scale the translation only; the 3x3 is unitless.
        FbxVector4 translation{ bind_pose.GetT() };
        translation *= (double)scene_scale;
        bind_pose.SetT(translation);

        const FbxAMatrix inverse_bind_pose{ bind_pose.Inverse() };
        math::m4x4& mtx{ m.inverse_bind_pose[i] };
        for (u32 row{ 0 }; row < 4; ++row)
            for (u32 col{ 0 }; col < 4; ++col)
                mtx.m[row][col] = (f32)inverse_bind_pose[row][col];
    }

    joint_nodes.swap(new_joint_nodes);

    // Remap the joint indices according to joint hierarchy
    utl::vector<math::u32v4> new_joint_indices(num_vertices, { 0, 0, 0, 0 });

    for (u32 i{ 0 }; i < num_vertices; ++i)
    {
        u32* indices{ &m.joint_indices[i].x };
        for (u32 j{ 0 }; j < mesh::max_joints_per_vertex; ++j)
        {
            const u32 source_index{ indices[j] };
            indices[j] = source_index < num_source_joints ? source_to_hierarchy[source_index] : 0;
        }
    }

    return true;
}

void
normalize_joint_weights(mesh& m)
{
    const u32 num_vertices{ (u32)m.joint_weights.size() };
    // Make sure that the weights for each vertex sum up to 1.0
    // Assumes non-negative weights.
    for (u32 i{ 0 }; i < num_vertices; ++i)
    {
        math::v4& weights{ m.joint_weights[i] };
        math::u32v4& indices{ m.joint_indices[i] };

        // Clamp negatives to zero (Maya can export them when normalization is off)
        weights.x = std::max(weights.x, 0.f);
        weights.y = std::max(weights.y, 0.f);
        weights.z = std::max(weights.z, 0.f);
        weights.w = std::max(weights.w, 0.f);

        const double weight_sum{
            (double)weights.x + (double)weights.y + (double)weights.z + (double)weights.w };

        if (!math::is_equal((f32)weight_sum, 1.f))
        {
            if (weight_sum > 0.f)
            {
                // Standard normalization
                const f32 inv_sum{ (f32)(1.0 / weight_sum) };
                weights.x *= inv_sum;
                weights.y *= inv_sum;
                weights.z *= inv_sum;
                weights.w *= inv_sum;
            }
            else
            {
                // Unskinned or corrupted vertex (sum is 0 or negative).
                // Safely bind 100% to the root joint.
                weights = { 1.f, 0.f, 0.f, 0.f };
                indices = { 0, 0, 0, 0 };
            }
        }
    }
}

} // anonymous namespace

void
fbx_context::get_skinning_data(FbxMesh* fbx_mesh, mesh& m)
{
    const s32 num_deformers{ fbx_mesh->GetDeformerCount(FbxDeformer::eSkin) };
    m.is_skeletal_mesh = num_deformers > 0;
    if (!m.is_skeletal_mesh) return;

    const u32 num_vertices{ (u32)fbx_mesh->GetControlPointsCount() };
    struct joint_influence { FbxNode* joint; f32 weight; };
    utl::vector<utl::vector<joint_influence>> accumulated_influences(num_vertices);
    utl::vector<FbxNode*> cluster_joints;
    utl::vector<std::pair<FbxNode*, FbxAMatrix>> cluster_bind_poses;

    for (s32 deformer_idx{ 0 }; deformer_idx < num_deformers; ++deformer_idx)
    {
        FbxSkin* skin{ FbxCast<FbxSkin>(fbx_mesh->GetDeformer(deformer_idx, FbxDeformer::eSkin)) };
        if (!skin) continue;

        const s32 num_clusters{ skin->GetClusterCount() };
        for (s32 cluster_idx{ 0 }; cluster_idx < num_clusters; ++cluster_idx)
        {
            FbxCluster* cluster{ skin->GetCluster(cluster_idx) };
            FbxNode* joint_node{ cluster ? cluster->GetLink() : nullptr };
            if (!joint_node) continue;

            // NOTE: we don't support additive clusters yet (FbxCluster::eAdditive)
            const FbxCluster::ELinkMode link_mode{ cluster->GetLinkMode() };
            if (link_mode != FbxCluster::eNormalize &&
                link_mode != FbxCluster::eTotalOne)
            {
                SEND_UDP_LOG(message_type::warning, "SKipped unsupported skinning mode.");
                continue;
            }

            if (joint_index_of(cluster_joints, joint_node) == u32_invalid_id)
            {
                cluster_joints.emplace_back(joint_node);
                FbxAMatrix bind_pose;
                cluster->GetTransformLinkMatrix(bind_pose);
                cluster_bind_poses.emplace_back(joint_node, bind_pose);
            }

            const s32 num_indices{ cluster->GetControlPointIndicesCount() };
            const int* cluster_indices{ cluster->GetControlPointIndices() };
            const double* cluster_weights{ cluster->GetControlPointWeights() };
            if (!cluster_indices || !cluster_weights) continue;

            for (s32 i{ 0 }; i < num_indices; ++i)
            {
                const s32 c_idx{ cluster_indices[i] };
                if (c_idx < 0 || (u32)c_idx >= num_vertices) continue;

                const f32 weight{ (f32)cluster_weights[i] };
                if (!std::isfinite(weight)) continue;

                auto& influences{ accumulated_influences[c_idx] };
                auto influence{ std::find_if(influences.begin(), influences.end(),
                    [joint_node](const joint_influence& item) { return item.joint == joint_node; }) };

                if (influence == influences.end())
                    influences.emplace_back(joint_node, weight);
                else
                    influence->weight += weight;
            }
        }
    }

    // Scene order, not cluster order, so two meshes on one rig get the same joint indices.
    utl::vector<FbxNode*> joint_nodes;
    collect_joint_nodes(_fbx_scene->GetRootNode(), cluster_joints, joint_nodes);

    for (FbxNode* joint : cluster_joints)
    {
        if (joint_index_of(joint_nodes, joint) == u32_invalid_id)
        {
            joint_nodes.emplace_back(joint);
        }
    }

    if (joint_nodes.empty())
    {
        m.is_skeletal_mesh = false;
        return;
    }

    // NOTE: get_joint_hierarchy will get rid of all u32_invalid_id's
    //       so the shader always has valid joint ids to work with.
    m.joint_indices.resize(num_vertices, { u32_invalid_id, u32_invalid_id, u32_invalid_id, u32_invalid_id });
    m.joint_weights.resize(num_vertices, { 0.f, 0.f, 0.f, 0.f });

    for (u32 vertex_idx{ 0 }; vertex_idx < num_vertices; ++vertex_idx)
    {
        auto& influences{ accumulated_influences[vertex_idx] };
        std::sort(influences.begin(), influences.end(),
            [](const joint_influence& lhs, const joint_influence& rhs) { return lhs.weight > rhs.weight; });

        u32* joint_indices{ &m.joint_indices[vertex_idx].x };
        f32* joint_weights{ &m.joint_weights[vertex_idx].x };
        u32 written{ 0 };

        for (u32 i{ 0 }; i < influences.size() && written < mesh::max_joints_per_vertex; ++i)
        {
            const u32 index{ joint_index_of(joint_nodes, influences[i].joint) };
            if (index == u32_invalid_id) continue;
            joint_indices[written] = index;
            joint_weights[written] = influences[i].weight;
            ++written;
        }
    }

    if (get_joint_hierarchy(_fbx_scene, _scene_scale, cluster_bind_poses, joint_nodes, m))
    {
        normalize_joint_weights(m);
        note_imported_skeleton_root(_imported_skeleton_roots, joint_nodes);
        get_animation_data(joint_nodes, m);
    }
    else
    {
        m.is_skeletal_mesh = false;
        m.joint_indices.clear();
        m.joint_weights.clear();
    }
}

void
fbx_context::get_skeletons()
{
    if (!_fbx_scene || !_scene || !_scene_data) return;

    FbxNode* scene_root{ _fbx_scene->GetRootNode() };
    if (!scene_root) return;

    // Same walk the skinned path uses, but with no cluster links: skeleton nodes only.
    // One preorder walk. A rig is a contiguous span of that list: the root, then
    // every following joint whose top skeleton ancestor is that same root.
    utl::vector<FbxNode*> skeleton_nodes;
    const utl::vector<FbxNode*> no_clusters;
    collect_joint_nodes(scene_root, no_clusters, skeleton_nodes);

    const u32 count{ (u32)skeleton_nodes.size() };
    if (!count) return;

    utl::vector<FbxNode*> root_of(count);
    for (u32 i{ 0 }; i < count; ++i)
    {
        root_of[i] = skeleton_root(skeleton_nodes[i]);
    }

    const utl::vector<std::pair<FbxNode*, FbxAMatrix>> no_bind_poses;
    u32 unnamed{ 0 };

    for (u32 begin{ 0 }; begin < count; )
    {
        FbxNode* root{ root_of[begin] };
        u32 end{ begin + 1 };
        // Skip joints with the same root
        for (; end < count && root_of[end] == root; ++end) {}

        if (root && !is_imported_skeleton(_imported_skeleton_roots, root))
        {
            // Found new skeleton
            utl::vector<FbxNode*> joint_nodes;
            joint_nodes.reserve(end - begin);
            for (u32 index{ begin }; index < end; ++index)
            {
                joint_nodes.emplace_back(skeleton_nodes[index]);
            }

            mesh skeleton{};
            skeleton.settings = _scene_data->settings;
            skeleton.is_skeletal_mesh = true;
            skeleton.lod_id = 0;
            skeleton.lod_threshold = -1.f;
            skeleton.name = (root->GetName()[0] != '\0') ? root->GetName() :
                ("Skeleton" + std::to_string(unnamed++));

            if (get_joint_hierarchy(_fbx_scene, _scene_scale, no_bind_poses, joint_nodes, skeleton))
            {
                note_imported_skeleton_root(_imported_skeleton_roots, joint_nodes);
                get_animation_data(joint_nodes, skeleton);

                lod_group lod{};
                lod.name = skeleton.name;
                lod.meshes.emplace_back(skeleton);
                _scene->lod_groups.emplace_back(lod);

                if (_progression)
                    _progression->callback(_progression->value(), _progression->max_value() + 1);
            }
        }

        begin = end;
    }
}

} // namespace primal::tools