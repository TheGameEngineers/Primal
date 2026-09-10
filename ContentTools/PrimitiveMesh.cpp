// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "PrimitiveMesh.h"
#include "Geometry.h"
#include <string>

namespace primal::tools {
namespace {

using namespace DirectX;

// The vertices of an icosahedron centered at the origin with an edge-length of 2 and a circumradius of 
// sqrt(phi+2) ~ 1.9 are described by cyclic permutations of (0, (+/-)1, (+/-)phi)
// see: https://en.wikipedia.org/wiki/Regular_icosahedron
constexpr math::v3 ico_sphere_vertices[]
{
     { 0.f,-1.f, math::phi }, { math::phi, 0.f,-1.f }, {-1.f, math::phi, 0.f },
     { 0.f, 1.f, math::phi }, { math::phi, 0.f, 1.f }, { 1.f, math::phi, 0.f },
     { 0.f,-1.f,-math::phi }, {-math::phi, 0.f,-1.f }, {-1.f,-math::phi, 0.f },
     { 0.f, 1.f,-math::phi }, {-math::phi, 0.f, 1.f }, { 1.f,-math::phi, 0.f }
};

constexpr math::v2 ico_sphere_uvs[]
{
    {0.0f,      0.157461f},  // 0
    {0.090909f, 0.0f},       // 1
    {0.090909f, 0.314921f},  // 2       //
    {0.181818f, 0.157461f},  // 3       // Verts & UVs are ordered by U then Y coords,
    {0.181818f, 0.472382f},  // 4       //
    {0.272727f, 0.0f},       // 5       //      4   8   C   G   K
    {0.272727f, 0.314921f},  // 6       //     / \ / \ / \ / \ / \                   .
    {0.363636f, 0.157461f},  // 7       //    2---6---A---E---I---L
    {0.363636f, 0.472382f},  // 8       //   / \ / \ / \ / \ / \ /
    {0.454545f, 0.0f},       // 9       //  0---3---7---B---F---J
    {0.454545f, 0.314921f},  // 10  A   //   \ / \ / \ / \ / \ /
    {0.545454f, 0.157461f},  // 11  B   //    1   5   9   D   H
    {0.545454f, 0.472382f},  // 12  C   //
    {0.636363f, 0.0f},       // 13  D   // [4, 8, C, G, K] have the same position vert
    {0.636363f, 0.314921f},  // 14  E   // [1, 5, 9, D, H] have the same position vert
    {0.727272f, 0.157461f},  // 15  F   // [0, J]          have the same position vert
    {0.727272f, 0.472382f},  // 16  G   // [2, L]          have the same position vert
    {0.818181f, 0.0f},       // 17  H   // 
    {0.818181f, 0.314921f},  // 18  I   
    {0.90909f,  0.157461f},  // 19  J
    {0.90909f,  0.472382f},  // 20  K
    {1.0f,      0.314921f}   // 21  L
};

constexpr u32 ico_sphere_uv_indices[]
{
    4, 2, 6,      6, 10, 8,     12, 10, 14,   14, 18, 16,   20, 18, 21,
    2, 0, 3,      2, 3, 6,      6, 3, 7,      6, 7, 10,     10, 7, 11,
    10, 11, 14,   14, 11, 15,   14, 15, 18,   18, 15, 19,   18, 19, 21,
    0, 1, 3,      3, 5, 7,      7, 9, 11,     11, 13, 15,   15, 17, 19
};

constexpr u32 ico_sphere_indices[]
{
    5, 2, 3,    3, 4, 5,    5, 4, 1,    1, 9, 5,    5, 9, 2,
    2, 7, 10,   2, 10, 3,   3, 10, 0,   3, 0, 4,    4, 0, 11,
    4, 11, 1,   1, 11, 6,   1, 6, 9,    9, 6, 7,    9, 7, 2,
    7, 8, 10,   10, 8, 0,   0, 8, 11,   11, 8, 6,   6, 8, 7
};

using index_lookup = std::unordered_map<u64, u32>;
using primitive_mesh_creator = void(*)(scene&, const primitive_init_info& info);

void create_plane(scene& scene, const primitive_init_info& info);
void create_cube(scene& scene, const primitive_init_info& info);
void create_uv_sphere(scene& scene, const primitive_init_info& info);
void create_ico_sphere(scene& scene, const primitive_init_info& info);
void create_cylinder(scene& scene, const primitive_init_info& info);
void create_capsule(scene& scene, const primitive_init_info& info);

primitive_mesh_creator creators[]
{
    create_plane,
    create_cube,
    create_uv_sphere,
    create_ico_sphere,
    create_cylinder,
    create_capsule,
};

static_assert(_countof(creators) == primitive_mesh_type::count);

struct axis {
    enum : u32 {
        x = 0,
        y = 1,
        z = 2
    };
};

mesh
create_plane(const primitive_init_info& info,
    u32 horizontal_index = axis::x, u32 vertical_index = axis::z, bool flip_winding = false,
    math::v3 offset = { -1.f, 0.f, -1.f }, math::v2 u_range = { 0.f, 1.f }, math::v2 v_range = { 0.f, 1.f })
{
    assert(horizontal_index < 3 && vertical_index < 3);
    assert(horizontal_index != vertical_index);

    const u32* const segments{ &info.segments[0] };

    const u32 horizontal_count{ math::clamp(segments[horizontal_index], 1u, 10u) };
    const u32 vertical_count{ math::clamp(segments[vertical_index], 1u, 10u) };
    const f32 horizontal_step{ 2.f / horizontal_count };
    const f32 vertical_step{ 2.f / vertical_count };
    const f32 u_step{ (u_range.y - u_range.x) / horizontal_count };
    const f32 v_step{ (v_range.y - v_range.x) / vertical_count };

    mesh m{};
    utl::vector<math::v2> uvs;

    for (u32 j{ 0 }; j <= vertical_count; ++j)
    {
        const f32 vertical_increment{ j * vertical_step };
        const f32 v_increment{ j * v_step };

        for (u32 i{ 0 }; i <= horizontal_count; ++i)
        {
            math::v3 position{ offset };
            f32* const as_array{ &position.x };
            as_array[horizontal_index] += i * horizontal_step;
            as_array[vertical_index] += vertical_increment;
            m.positions.emplace_back(position.x * info.size.x, position.y * info.size.y, position.z * info.size.z);

            math::v2 uv{ u_range.x, 1.f - v_range.x };
            uv.x += i * u_step;
            uv.y -= v_increment;
            uvs.emplace_back(uv);
        }
    }

    assert(m.positions.size() == (((u64)horizontal_count + 1) * ((u64)vertical_count + 1)));

    const u32 row_length{ horizontal_count + 1 }; // number of vertices in a row
    for (u32 j{ 0 }; j < vertical_count; ++j)
    {
        for (u32 i{ 0 }; i < horizontal_count; ++i)
        {
            const u32 index[4]
            {
                i + j * row_length,
                i + (j + 1) * row_length,
                (i + 1) + j * row_length,
                (i + 1) + (j + 1) * row_length
            };

            m.raw_indices.emplace_back(index[0]);
            m.raw_indices.emplace_back(index[flip_winding ? 2 : 1]);
            m.raw_indices.emplace_back(index[flip_winding ? 1 : 2]);

            m.raw_indices.emplace_back(index[2]);
            m.raw_indices.emplace_back(index[flip_winding ? 3 : 1]);
            m.raw_indices.emplace_back(index[flip_winding ? 1 : 3]);
        }
    }

    const u32 num_indices{ 3 * 2 * horizontal_count * vertical_count };
    assert(m.raw_indices.size() == num_indices);

    m.uv_sets.resize(1);

    for (u32 i{ 0 }; i < num_indices; ++i)
    {
        m.uv_sets[0].emplace_back(uvs[m.raw_indices[i]]);
    }

    return m;
}

constexpr math::v3
get_face_vertex(u32 face, f32 x, f32 y)
{
    math::v3 face_vertex[6] = {
        {-1.f, -y,    x},   // X- Right
        { 1.f, -y,   -x},   // X+ Left
        { x,    1.f,  y},   // Y+ Bottom
        { x,   -1.f, -y},   // Y- Top
        { x,   -y,    1.f}, // Z+ Front
        {-x,   -y,   -1.f}, // Z- Back
    };

    return face_vertex[face];
}

mesh
create_cube(const primitive_init_info& info)
{
    const u32 *const segments{ &info.segments[0] };
    constexpr math::u32v2 axes[3]{ {axis::z, axis::y}, {axis::x, axis::z}, {axis::x, axis::y} };
    constexpr f32 u_range[6]{ 0.f, 0.5f, 0.25f, 0.25f, 0.25f, 0.75f };
    constexpr f32 v_range[6]{ 0.375f, 0.375f, 0.125f, 0.625f, 0.375f, 0.375f };
    mesh m{};
    utl::vector<math::v2> uvs{};

    for (u32 face{ 0 }; face < 6; ++face)
    {
        const u32 axes_index{ face >> 1 };
        const math::u32v2& axis{ axes[axes_index] };
        const u32 x_count{ math::clamp(segments[axis.x], (u32)1, (u32)10) };
        const u32 y_count{ math::clamp(segments[axis.y], (u32)1, (u32)10) };
        const f32 x_step{ 1.f / x_count };
        const f32 y_step{ 1.f / y_count };
        const f32 u_step{ 0.25f / x_count };
        const f32 v_step{ 0.25f / y_count };

        const u32 raw_index_offset{ (u32)m.positions.size() };

        for (u32 y{ 0 }; y <= y_count; ++y)
        {
            for (u32 x{ 0 }; x <= x_count; ++x)
            {
                math::v2 pos{ 2.f * x * x_step - 1.f, 2.f * y * y_step - 1.f };
                math::v3 position{ get_face_vertex(face, pos.x, pos.y) };
                m.positions.emplace_back(position.x * info.size.x, position.y * info.size.y, position.z * info.size.z);
#if 1
                math::v2 uv{ u_range[face], 1.f - v_range[face] };
                uv.x += x * u_step;
                uv.y -= y * v_step;
#else
                // for checking if the segments are correct, this will show the segments on the texture
                math::v2 uv{ 0.f, 1.f };
                uv.x += (f32)(x % 2);
                uv.y -= (f32)(y % 2);
#endif
                uvs.emplace_back(uv);
            }
        }

        const u32 row_length{ x_count + 1 }; // number of vertices in a row
        for (u32 y{ 0 }; y < y_count; ++y)
        {
            for (u32 x{ 0 }; x < x_count; ++x)
            {
                const u32 index[4]{
                    raw_index_offset + x + y * row_length,
                    raw_index_offset + x + (y + 1) * row_length,
                    raw_index_offset + (x + 1) + y * row_length,
                    raw_index_offset + (x + 1) + (y + 1) * row_length
                };

                m.raw_indices.emplace_back(index[0]);
                m.raw_indices.emplace_back(index[1]);
                m.raw_indices.emplace_back(index[2]);

                m.raw_indices.emplace_back(index[2]);
                m.raw_indices.emplace_back(index[1]);
                m.raw_indices.emplace_back(index[3]);
            }
        }
    }

    m.uv_sets.resize(1);
    for (u32 i{ 0 }; i < m.raw_indices.size(); ++i)
    {
        m.uv_sets[0].emplace_back(uvs[m.raw_indices[i]]);
    }

    return m;
}

mesh
create_uv_sphere(const primitive_init_info& info)
{
    const u32 phi_count{ math::clamp(info.segments[axis::x], 3u, 64u) };
    const u32 theta_count{ math::clamp(info.segments[axis::y], 2u, 64u) };
    const f32 theta_step{ math::pi / theta_count };
    const f32 phi_step{ math::two_pi / phi_count };
    const u32 num_indices{ 2 * 3 * phi_count + 2 * 3 * phi_count * (theta_count - 2) };
    const u32 num_vertices{ 2 + phi_count * (theta_count - 1) };

    mesh m{};
    m.name = "uv_sphere";
    m.positions.resize(num_vertices);

    // Add the top vertex
    u32 c{ 0 };
    m.positions[c++] = { 0.f, info.size.y, 0.f };

    for (u32 j{ 1 }; j <= (theta_count - 1); ++j)
    {
        const f32 theta{ j * theta_step };
        for (u32 i{ 0 }; i < phi_count; ++i)
        {
            const f32 phi{ i * phi_step };
            m.positions[c++] = {
                info.size.x * XMScalarSin(theta) * XMScalarCos(phi),
                info.size.y * XMScalarCos(theta),
                -info.size.z * XMScalarSin(theta) * XMScalarSin(phi) };
        }
    }

    // Add the bottom vertex
    m.positions[c++] = { 0.f, -info.size.y, 0.f };
    assert(c == num_vertices);

    c = 0;
    m.raw_indices.resize(num_indices);
    utl::vector<math::v2> uvs(num_indices);
    const f32 inv_theta_count{ 1.f / theta_count };
    const f32 inv_phi_count{ 1.f / phi_count };

    // Indices for the top cap, connecting the north pole to the first ring
    for (u32 i{ 0 }; i < phi_count - 1; ++i)
    {
        uvs[c] = { (2 * i + 1) * 0.5f * inv_phi_count, 1.f };
        m.raw_indices[c++] = 0;
        uvs[c] = { i * inv_phi_count, 1.f - inv_theta_count };
        m.raw_indices[c++] = i + 1;
        uvs[c] = { (i + 1) * inv_phi_count, 1.f - inv_theta_count };
        m.raw_indices[c++] = i + 2;
    }

    uvs[c] = { 1.f - 0.5f * inv_phi_count, 1.f };
    m.raw_indices[c++] = 0;
    uvs[c] = { 1.f - inv_phi_count, 1.f - inv_theta_count };
    m.raw_indices[c++] = phi_count;
    uvs[c] = { 1.f , 1.f - inv_theta_count };
    m.raw_indices[c++] = 1;

    // Indices for the section between the top and bottom rings
    for (u32 j{ 0 }; j < (theta_count - 2); ++j)
    {
        for (u32 i{ 0 }; i < (phi_count - 1); ++i)
        {
            const u32 index[4]{
                1 + i + j * phi_count,
                1 + i + (j + 1) * phi_count,
                1 + (i + 1) + (j + 1) * phi_count,
                1 + (i + 1) + j * phi_count
            };

            uvs[c] = { i * inv_phi_count, 1.f - (j + 1) * inv_theta_count };
            m.raw_indices[c++] = index[0];
            uvs[c] = { i * inv_phi_count, 1.f - (j + 2) * inv_theta_count };
            m.raw_indices[c++] = index[1];
            uvs[c] = { (i + 1) * inv_phi_count, 1.f - (j + 2) * inv_theta_count };
            m.raw_indices[c++] = index[2];

            uvs[c] = { i * inv_phi_count, 1.f - (j + 1) * inv_theta_count };
            m.raw_indices[c++] = index[0];
            uvs[c] = { (i + 1) * inv_phi_count, 1.f - (j + 2) * inv_theta_count };
            m.raw_indices[c++] = index[2];
            uvs[c] = { (i + 1) * inv_phi_count, 1.f - (j + 1) * inv_theta_count };
            m.raw_indices[c++] = index[3];
        }

        const u32 index[4]{
            phi_count + j * phi_count,
            phi_count + (j + 1) * phi_count,
            1 + (j + 1) * phi_count,
            1 + j * phi_count
        };

        uvs[c] = { 1.f - inv_phi_count, 1.f - (j + 1) * inv_theta_count };
        m.raw_indices[c++] = index[0];
        uvs[c] = { 1.f - inv_phi_count, 1.f - (j + 2) * inv_theta_count };
        m.raw_indices[c++] = index[1];
        uvs[c] = { 1.f, 1.f - (j + 2) * inv_theta_count };
        m.raw_indices[c++] = index[2];

        uvs[c] = { 1.f - inv_phi_count, 1.f - (j + 1) * inv_theta_count };
        m.raw_indices[c++] = index[0];
        uvs[c] = { 1.f, 1.f - (j + 2) * inv_theta_count };
        m.raw_indices[c++] = index[2];
        uvs[c] = { 1.f, 1.f - (j + 1) * inv_theta_count };
        m.raw_indices[c++] = index[3];
    }

    // Indices for the bottom cap, connecting the south posle to the last ring
    const u32 south_pole_index{ (u32)m.positions.size() - 1 };
    for (u32 i{ 0 }; i < (phi_count - 1); ++i)
    {
        uvs[c] = { (2 * i + 1) * 0.5f * inv_phi_count, 0.f };
        m.raw_indices[c++] = south_pole_index;
        uvs[c] = { (i + 1) * inv_phi_count, inv_theta_count };
        m.raw_indices[c++] = south_pole_index - phi_count + i + 1;
        uvs[c] = { i * inv_phi_count, inv_theta_count };
        m.raw_indices[c++] = south_pole_index - phi_count + i;
    }

    uvs[c] = { 1.f - 0.5f * inv_phi_count, 0.f };
    m.raw_indices[c++] = south_pole_index;
    uvs[c] = { 1.f, inv_theta_count };
    m.raw_indices[c++] = south_pole_index - phi_count;
    uvs[c] = { 1.f - inv_phi_count, inv_theta_count };
    m.raw_indices[c++] = south_pole_index - 1;

    assert(c == num_indices);

    m.uv_sets.emplace_back(uvs);

    return m;
}

u32
vertex_for_edge(index_lookup& lookup, utl::vector<math::v3>& vertices, u32 first, u32 second)
{
    const u64 key{ (first > second) ? ((u64)first << 32) | second : ((u64)second << 32) | first };
    auto inserted = lookup.insert({ key, (u32)vertices.size() });
    if (inserted.second)
    {
        XMVECTOR e0{ XMLoadFloat3(&vertices[first]) };
        XMVECTOR e1{ XMLoadFloat3(&vertices[second]) };
        math::v3 v;
        XMStoreFloat3(&v, (e0 + e1) * 0.5f);
        vertices.emplace_back(v);
    }

    return inserted.first->second;
}

math::v2
uv_for_edge(math::v2 first, math::v2 second)
{
    XMVECTOR uv0{ XMLoadFloat2(&first) };
    XMVECTOR uv1{ XMLoadFloat2(&second) };
    math::v2 uv;
    XMStoreFloat2(&uv, (uv0 + uv1) * 0.5f);
    return uv;
}

void
subdivide(lod_group& lod, u32 index)
{
    assert(index);
    assert(lod.meshes.size() == index);

    mesh& m{ lod.meshes.emplace_back() };
    const mesh& prev{ lod.meshes[index - 1] };
    m.positions.reserve(prev.positions.size() * 4);
    m.positions = prev.positions;
    m.raw_indices.resize(prev.raw_indices.size() * 4);
    m.uv_sets.resize(1);
    m.uv_sets[0].resize(prev.uv_sets[0].size() * 4);

    index_lookup lookup{};
    u32 num_indices{ 0 };
    u32 num_uvs{ 0 };

    for (u32 i{ 0 }; i < prev.raw_indices.size(); i += 3)
    {
        const u32 triangle[3]
        {
            prev.raw_indices[i],
            prev.raw_indices[i + 1],
            prev.raw_indices[i + 2]
        };

        const u32 mid[3]
        {
            vertex_for_edge(lookup, m.positions, triangle[0], triangle[1]),
            vertex_for_edge(lookup, m.positions, triangle[1], triangle[2]),
            vertex_for_edge(lookup, m.positions, triangle[2], triangle[0])
        };

        m.raw_indices[num_indices++] = triangle[0]; m.raw_indices[num_indices++] = mid[0]; m.raw_indices[num_indices++] = mid[2];
        m.raw_indices[num_indices++] = triangle[1]; m.raw_indices[num_indices++] = mid[1]; m.raw_indices[num_indices++] = mid[0];
        m.raw_indices[num_indices++] = triangle[2]; m.raw_indices[num_indices++] = mid[2]; m.raw_indices[num_indices++] = mid[1];
        m.raw_indices[num_indices++] = mid[0];      m.raw_indices[num_indices++] = mid[1]; m.raw_indices[num_indices++] = mid[2];

        const math::v2 uv_tri[3]
        {
            prev.uv_sets[0][i],
            prev.uv_sets[0][i + 1],
            prev.uv_sets[0][i + 2]
        };

        const math::v2 mid_uv[3]
        {
            uv_for_edge(uv_tri[0], uv_tri[1]),
            uv_for_edge(uv_tri[1], uv_tri[2]),
            uv_for_edge(uv_tri[2], uv_tri[0])
        };

        m.uv_sets[0][num_uvs++] = uv_tri[0]; m.uv_sets[0][num_uvs++] = mid_uv[0]; m.uv_sets[0][num_uvs++] = mid_uv[2];
        m.uv_sets[0][num_uvs++] = uv_tri[1]; m.uv_sets[0][num_uvs++] = mid_uv[1]; m.uv_sets[0][num_uvs++] = mid_uv[0];
        m.uv_sets[0][num_uvs++] = uv_tri[2]; m.uv_sets[0][num_uvs++] = mid_uv[2]; m.uv_sets[0][num_uvs++] = mid_uv[1];
        m.uv_sets[0][num_uvs++] = mid_uv[0]; m.uv_sets[0][num_uvs++] = mid_uv[1]; m.uv_sets[0][num_uvs++] = mid_uv[2];
    }

    assert(num_indices == m.raw_indices.size());
}

lod_group
create_ico_sphere(const primitive_init_info& info)
{
    const u32 subdivisions{ math::clamp(info.segments[axis::x], (u32)0, (u32)6) };

    lod_group lod{};
    lod.name = "icosphere";
    mesh& m{ lod.meshes.emplace_back() };
    m.name = lod.name + "_" + std::to_string(subdivisions);

    m.positions.resize(_countof(ico_sphere_vertices));
    memcpy(m.positions.data(), &ico_sphere_vertices[0], sizeof(ico_sphere_vertices));
    m.raw_indices.resize(_countof(ico_sphere_indices));
    memcpy(m.raw_indices.data(), &ico_sphere_indices[0], sizeof(ico_sphere_indices));
    m.uv_sets.resize(1);

    for (u32 i{ 0 }; i < m.raw_indices.size(); ++i)
    {
        m.uv_sets[0].emplace_back(ico_sphere_uvs[ico_sphere_uv_indices[i]]);
    }

    for (u32 i{ 1 }; i <= subdivisions; ++i)
    {
        subdivide(lod, i);
        lod.meshes.back().name = lod.name + "_" + std::to_string(subdivisions - i);
    }

    XMVECTOR s{ XMLoadFloat3(&info.size) };

    for (auto& mesh : lod.meshes)
        for (auto& v : mesh.positions)
        {
            XMStoreFloat3(&v, XMVector3Normalize(XMLoadFloat3(&v)) * s);
        }

    std::reverse(lod.meshes.begin(), lod.meshes.end());
    const u32 num_meshes{ info.lods ? std::min(info.lods, (u32)lod.meshes.size()) : (u32)lod.meshes.size() };
    lod.meshes.resize(num_meshes);

    const f32 radius{ info.size.x * info.size.x + info.size.y * info.size.y + info.size.z * info.size.z };
    for (u32 i{ 1 }; i < num_meshes; ++i)
    {
        lod.meshes[i].lod_threshold = radius < 1.f ? radius * i * i : powf(radius, (f32)i);
    }

    return lod;
}

void
create_plane(scene& scene, const primitive_init_info& info)
{
    lod_group lod{};
    lod.name = "plane";
    lod.meshes.emplace_back(create_plane(info));
    scene.lod_groups.emplace_back(lod);
}

void
create_cube(scene& scene, const primitive_init_info& info)
{
    mesh cube{};
    cube.name = "cube";
    cube.uv_sets.resize(1);

    lod_group lod{};
    lod.name = "cube";
    lod.meshes.emplace_back(create_cube(info));
    scene.lod_groups.emplace_back(lod);
}

void
create_uv_sphere(scene& scene, const primitive_init_info& info)
{
    lod_group lod{};
    lod.name = "uv_sphere";
    lod.meshes.emplace_back(create_uv_sphere(info));
    scene.lod_groups.emplace_back(lod);
}

void
create_ico_sphere(scene& scene, const primitive_init_info& info)
{
    scene.lod_groups.emplace_back(create_ico_sphere(info));
}

void
create_cylinder(scene&, const primitive_init_info&)
{
}

void
create_capsule(scene&, const primitive_init_info&)
{
}

} // anonymous namespace

EDITOR_INTERFACE void
CreatePrimitiveMesh(scene_data* data, primitive_init_info* info)
{
    assert(data && info);
    assert(info->type < primitive_mesh_type::count);
    scene scene{};
    creators[info->type](scene, *info);

    progression progression{};
    process_scene(scene, data->settings, &progression);
    pack_data(scene, *data);
}

}