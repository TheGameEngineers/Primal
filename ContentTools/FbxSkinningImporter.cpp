// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "FbxImporter.h"
#include "Geometry.h"
#include "Profiling/UdpLog.h"
#include <algorithm>

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
    if ((node->GetSkeleton() || is_cluster_joint) && joint_index_of(joint_nodes, node) == u32_invalid_id)
        joint_nodes.emplace_back(node);

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

void
get_joint_hierarchy_recursive(
    u32 index,
    u32 parent_index,
    const utl::vector<utl::vector<u32>>& children,
    utl::vector<u8>& visited,
    utl::vector<u32>& hierarchy,
    utl::vector<u32>& hierarchy_map)
{
    if (visited[index]) return;
    visited[index] = 1;

    const u32 hierarchy_index{ (u32)hierarchy_map.size() };
    hierarchy_map.emplace_back(index);
    hierarchy.emplace_back(parent_index);

    for (const u32 child_index : children[index])
    {
        get_joint_hierarchy_recursive(child_index, hierarchy_index, children, visited, hierarchy, hierarchy_map);
    }
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

bool
get_joint_hierarchy(
    FbxScene* scene,
    const utl::vector<FbxNode*>& joint_nodes,
    const utl::vector<std::pair<FbxNode*, FbxAMatrix>>& cluster_bind_poses,
    f32 scene_scale,
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
            if (parent_index == u32_invalid_id) continue;
            parent_indices[i] = parent_index;
            break;
        }
    }

    break_parent_cycles(parent_indices);

    utl::vector<utl::vector<u32>> children(num_source_joints);
    for (u32 i{ 0 }; i < num_source_joints; ++i)
    {
        if (parent_indices[i] != u32_invalid_id)
        {
            children[parent_indices[i]].emplace_back(i);
        }
    }

    // A tree is kept when any vertex has a positive weight on it. The whole tree
    // stays, including bones this mesh does not weight, so the skeleton matches
    // other meshes on the same rig. Trees with no weights are dropped.
    utl::vector<u8> keep_root(num_source_joints, 0);
    const u32 num_vertices{ (u32)m.joint_indices.size() };

    for (u32 vertex_idx{ 0 }; vertex_idx < num_vertices; ++vertex_idx)
    {
        const u32* joint_indices{ &m.joint_indices[vertex_idx].x };
        const f32* joint_weights{ &m.joint_weights[vertex_idx].x };
        for (u32 i{ 0 }; i < mesh::max_joints_per_vertex; ++i)
        {
            if (joint_weights[i] <= 0.f || joint_indices[i] >= num_source_joints) continue;

            u32 root_index{ joint_indices[i] };
            while (parent_indices[root_index] != u32_invalid_id)
            {
                root_index = parent_indices[root_index];
            }
            keep_root[root_index] = 1;
        }
    }

    utl::vector<u8> visited(num_source_joints, 0);
    utl::vector<u32> hierarchy;
    utl::vector<u32> hierarchy_map;
    hierarchy.reserve(num_source_joints);
    hierarchy_map.reserve(num_source_joints);

    for (u32 i{ 0 }; i < num_source_joints; ++i)
    {
        if (keep_root[i] && parent_indices[i] == u32_invalid_id)
        {
            get_joint_hierarchy_recursive(i, u32_invalid_id, children, visited, hierarchy, hierarchy_map);
        }
    }

    const u32 num_joints{ (u32)hierarchy_map.size() };
    if (!num_joints)
    {
        m.is_skeletal_mesh = false;
        m.joint_indices.clear();
        m.joint_weights.clear();
        return false;
    }

    m.joint_hierarchy.swap(hierarchy);
    m.inverse_bind_pose.clear();
    m.inverse_bind_pose.reserve(num_joints);
    m.joint_ids.resize(num_joints, u64_invalid_id);
    m.joint_names.resize(num_joints);

    utl::vector<math::u32v4> new_joint_indices(num_vertices, { 0, 0, 0, 0 });
    utl::vector<u32> source_to_hierarchy(num_source_joints, u32_invalid_id);

    for (u32 i{ 0 }; i < num_joints; ++i)
    {
        const u32 source_index{ hierarchy_map[i] };
        source_to_hierarchy[source_index] = i;
        FbxNode* joint{ joint_nodes[source_index] };
        m.joint_names[i] = joint->GetName();
        m.joint_ids[i] = joint->GetUniqueID();

        FbxAMatrix bind_pose;
        if (m.joint_hierarchy[i] != u32_invalid_id) // if not a root joint...
        {
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
        }
        else
        {
            bind_pose.SetIdentity();
        }

        const FbxAMatrix inverse_bind_pose{ bind_pose.Inverse() };
        math::m4x4& mtx{ m.inverse_bind_pose.emplace_back() };
        for (u32 row{ 0 }; row < 4; ++row)
            for (u32 col{ 0 }; col < 4; ++col)
                mtx.m[row][col] = (float)inverse_bind_pose[row][col];
    }

    for (u32 j{ 0 }; j < num_vertices; ++j)
    {
        for (u32 k{ 0 }; k < mesh::max_joints_per_vertex; ++k)
        {
            const u32 source_index{ (&m.joint_indices[j].x)[k] };
            const u32 mapped{ source_index < source_to_hierarchy.size() && source_to_hierarchy[source_index] != u32_invalid_id ?
                source_to_hierarchy[source_index] : 0 };

            (&new_joint_indices[j].x)[k] = mapped;
        }
    }

    m.joint_indices.swap(new_joint_indices);
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
                link_mode != FbxCluster::eTotalOne) continue;

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
        // TODO: remove invalid weights (i.e. !std::isfinite(weight)).
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

    if (get_joint_hierarchy(_fbx_scene, joint_nodes, cluster_bind_poses, _scene_scale, m))
        normalize_joint_weights(m);
}

} // namespace primal::tools
