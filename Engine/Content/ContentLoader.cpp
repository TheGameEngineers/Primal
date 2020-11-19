// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "ContentLoader.h"
#include "..\Components\Entity.h"
#include "..\Components\Transform.h"
#include "..\Components\Script.h"

#if !defined(SHIPPING)

#include <fstream>
#include <filesystem>
#include <Windows.h>

namespace primal::content {
namespace {

enum component_type
{
    transform,
    script,

    count
};

utl::vector<game_entity::entity> entities;
transform::init_info transform_info{};
script::init_info script_info{};

bool
read_transform(const u8*& data, game_entity::entity_info& info)
{
    using namespace DirectX;
    f32 rotation[3];

    assert(!info.transform);
    memcpy(&transform_info.position[0], data, sizeof(transform_info.position)); data += sizeof(transform_info.position);
    memcpy(&rotation[0], data, sizeof(rotation)); data += sizeof(rotation);
    memcpy(&transform_info.scale[0], data, sizeof(transform_info.scale)); data += sizeof(transform_info.scale);

    XMFLOAT3A rot{ &rotation[0] };
    XMVECTOR quat{ XMQuaternionRotationRollPitchYawFromVector(XMLoadFloat3A(&rot)) };
    XMFLOAT4A rot_quat{};
    XMStoreFloat4A(&rot_quat, quat);
    memcpy(&transform_info.rotation[0], &rot_quat.x, sizeof(transform_info.rotation));

    info.transform = &transform_info;

    return true;
}

bool
read_script(const u8*& data, game_entity::entity_info& info)
{
    assert(!info.script);
    const u32 name_length{ *data }; data += sizeof(u32);
    if (!name_length) return false;
    // if a script name is longer than 255 characters then something is probably
    // very wrong, either with the binary writer or the game programmer.
    assert(name_length < 256);
    char script_name[256];
    memcpy(&script_name[0], data, name_length); data += name_length;
    // make the name a zero-terminated c-string.
    script_name[name_length] = 0;
    script_info.script_creator = script::detail::get_script_creator(script::detail::string_hash()(script_name));
    info.script = &script_info;
    return script_info.script_creator != nullptr;
}

using component_reader = bool(*)(const u8*&, game_entity::entity_info&);
component_reader component_readers[]
{
    read_transform,
    read_script,
};
static_assert(_countof(component_readers) == component_type::count);

} // anonymous namespace

bool
load_game()
{
    // set the working directory to the executable path
    wchar_t path[MAX_PATH];
    const u32 length{ GetModuleFileName(0, &path[0], MAX_PATH) };
    if (!length || GetLastError() == ERROR_INSUFFICIENT_BUFFER) return false;
    std::filesystem::path p{ path };
    SetCurrentDirectory(p.parent_path().wstring().c_str());

    // read game.bin and create the entities.
    std::ifstream game("game.bin", std::ios::in | std::ios::binary);
    utl::vector<u8> buffer(std::istreambuf_iterator<char>(game), {});
    assert(buffer.size());
    const u8* at{ buffer.data() };
    constexpr u32 su32{ sizeof(u32) };
    const u32 num_entities{ *at }; at += su32;
    if (!num_entities) return false;

    for (u32 entity_index{ 0 }; entity_index < num_entities; ++entity_index)
    {
        game_entity::entity_info info{};
        const u32 entity_type{ *at }; at += su32;
        const u32 num_components{ *at }; at += su32;
        if (!num_components) return false;

        for (u32 component_index{ 0 }; component_index < num_components; ++component_index)
        {
            const u32 component_type{ *at }; at += su32;
            assert(component_type < component_type::count);
            if (!component_readers[component_type](at, info)) return false;
        }

        assert(info.transform);
        game_entity::entity entity{ game_entity::create(info) };
        if (!entity.is_valid()) return false;
        entities.emplace_back(entity);
    }

    assert(at == buffer.data() + buffer.size());
    return true;
}

void
unload_game()
{
    for (auto entity : entities)
    {
        game_entity::remove(entity.get_id());
    }
}
}
#endif // !defined(SHIPPING)