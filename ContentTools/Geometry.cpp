// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Geometry.h"

namespace primal::tools {
namespace {

using namespace math;
using namespace DirectX;

void
recalculate_normals(mesh& m)
{
    const u32 num_indices{ (u32)m.raw_indices.size() };
    m.normals.reserve(num_indices);

    for (u32 i{ 0 }; i < num_indices; ++i)
    {
        const u32 i0{ m.raw_indices[i] };
        const u32 i1{ m.raw_indices[++i] };
        const u32 i2{ m.raw_indices[++i] };

        XMVECTOR v0{ XMLoadFloat3(&m.positions[i0]) };
        XMVECTOR v1{ XMLoadFloat3(&m.positions[i1]) };
        XMVECTOR v2{ XMLoadFloat3(&m.positions[i2]) };

        XMVECTOR e0{ v1 - v0 };
        XMVECTOR e1{ v2 - v0 };
        XMVECTOR n{ XMVector3Normalize(XMVector3Cross(e0, e1)) };

        XMStoreFloat3(&m.normals[i], n);
        m.normals[i - 1] = m.normals[i];
        m.normals[i - 2] = m.normals[i];
    }
}

void
process_normals(mesh& m, f32 smoothing_angle)
{
    const f32 cos_angle{ XMScalarCos(pi - smoothing_angle * pi / 180.f) };
    const bool is_hard_edge{ XMScalarNearEqual(smoothing_angle, 180.f, epsilon) };
    const bool is_soft_edge{ XMScalarNearEqual(smoothing_angle, 0.f, epsilon) };
    const u32 num_indices{ (u32)m.raw_indices.size() };
    const u32 num_vertices{ (u32)m.positions.size() };
    assert(num_indices && num_vertices);

    m.indices.resize(num_indices);

    utl::vector<utl::vector<u32>> idx_ref(num_vertices);
    for (u32 i{ 0 }; i < num_indices; ++i)
        idx_ref[m.raw_indices[i]].emplace_back(i);

    for (u32 i{ 0 }; i < num_vertices; ++i)
    {
        auto& refs{ idx_ref[i] };
        u32 num_refs{ (u32)refs.size() };
        for (u32 j{ 0 }; j < num_refs; ++j)
        {
            m.indices[refs[j]] = (u32)m.vertices.size();
            vertex& v{ m.vertices.emplace_back() };
            v.position = m.positions[m.raw_indices[refs[j]]];

            XMVECTOR n1{ XMLoadFloat3(&m.normals[refs[j]]) };
            if (!is_hard_edge)
            {
                for (u32 k{ j + 1 }; k < num_refs; ++k)
                {
                    // this value represents the cosine of the angle between normals.
                    f32 n{ 0.f };
                    XMVECTOR n2{ XMLoadFloat3(&m.normals[refs[k]]) };
                    if (!is_soft_edge)
                    {
                        // NOTE: we're accounting for the length of n1 in this calculation because
                        //       it can possibly change in this loop iteration. We assume unit length
                        //       for n2. 
                        //       cos(angle) = dot(n1, n2) / (||n1||*||n2||)
                        XMStoreFloat(&n, XMVector3Dot(n1, n2) * XMVector3ReciprocalLength(n1));
                    }

                    if (is_soft_edge || n >= cos_angle)
                    {
                        n1 += n2;

                        m.indices[refs[k]] = m.indices[refs[j]];
                        refs.erase(refs.begin() + k);
                        --num_refs;
                        --k;
                    }
                }
            }
            XMStoreFloat3(&v.normal, XMVector3Normalize(n1));
        }
    }
}

void
process_uvs(mesh& m)
{
    utl::vector<vertex> old_vertices;
    old_vertices.swap(m.vertices);
    utl::vector<u32> old_indices(m.indices.size());
    old_indices.swap(m.indices);

    const u32 num_vertices{ (u32)old_vertices.size() };
    const u32 num_indices{ (u32)old_indices.size() };
    assert(num_vertices && num_indices);

    utl::vector<utl::vector<u32>> idx_ref(num_vertices);
    for (u32 i{ 0 }; i < num_indices; ++i)
        idx_ref[old_indices[i]].emplace_back(i);

    for (u32 i{ 0 }; i < num_indices; ++i)
    {
        auto& refs{ idx_ref[i] };
        u32 num_refs{ (u32)refs.size() };
        for (u32 j{ 0 }; j < num_refs; ++j)
        {
            m.indices[refs[j]] = (u32)m.vertices.size();
            vertex& v{ old_vertices[old_indices[refs[j]]] };
            v.uv = m.uv_sets[0][refs[j]];
            m.vertices.emplace_back(v);

            for (u32 k{ j + 1 }; k < num_refs; ++k)
            {
                v2& uv1{ m.uv_sets[0][refs[k]] };
                if (XMScalarNearEqual(v.uv.x, uv1.x, epsilon) &&
                    XMScalarNearEqual(v.uv.y, uv1.y, epsilon))
                {
                    m.indices[refs[k]] = m.indices[refs[j]];
                    refs.erase(refs.begin() + k);
                    --num_refs;
                    --k;
                }
            }
        }
    }
}

void
pack_vertices_static(mesh& m)
{
    const u32 num_vertices{ (u32)m.vertices.size() };
    assert(num_vertices);
    m.packed_vertices_static.reserve(num_vertices);

    for (u32 i{ 0 }; i < num_vertices; ++i)
    {
        vertex& v{ m.vertices[i] };
        const u8 signs{ (u8)((v.normal.z > 0.f) << 1) };
        const u16 normal_x{ (u16)pack_float<16>(v.normal.x, -1.f, 1.f) };
        const u16 normal_y{ (u16)pack_float<16>(v.normal.y, -1.f, 1.f) };
        // TODO: pack tangents in sign and in x/y components

        m.packed_vertices_static
            .emplace_back(packed_vertex::vertex_static
                          {
                              v.position, {0, 0, 0}, signs,
                              {normal_x, normal_y}, {},
                              v.uv
                          });
    }
}

void
process_vertices(mesh& m, const geometry_import_settings& settings)
{
    assert((m.raw_indices.size() % 3) == 0);
    if (settings.calculate_normals || m.normals.empty())
    {
        recalculate_normals(m);
    }

    process_normals(m, settings.smoothing_angle);

    if (!m.uv_sets.empty())
    {
        process_uvs(m);
    }

    pack_vertices_static(m);
}

} // anonymous namespace

void
process_scene(scene& scene, const geometry_import_settings& settings)
{
    for (auto& lod : scene.lod_groups)
        for (auto& m : lod.meshes)
        {
            process_vertices(m, settings);
        }
}

void
pack_data(const scene& scene, scene_data& data)
{}

}