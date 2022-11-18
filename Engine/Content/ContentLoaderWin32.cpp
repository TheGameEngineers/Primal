// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "ContentLoader.h"
#include "Components/Entity.h"
#include "Components/Transform.h"
#include "Components/Script.h"
#include "Graphics/Renderer.h"

#if !defined(SHIPPING) && defined(_WIN64)

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
    char script_name[256]{};
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

bool
read_file(std::filesystem::path path, std::unique_ptr<u8[]>& data, u64& size)
{
    if (!std::filesystem::exists(path)) return false;

    size = std::filesystem::file_size(path);
    assert(size);
    if (!size) return false;
    data = std::make_unique<u8[]>(size);
    std::ifstream file{ path, std::ios::in | std::ios::binary };
    if (!file || !file.read((char*)data.get(), size))
    {
        file.close();
        return false;
    }

    file.close();
    return true;
}

} // anonymous namespace

bool
load_game()
{
    // read game.bin and create the entities.
    std::unique_ptr<u8[]> game_data{};
    u64 size{ 0 };
    if (!read_file("game.bin", game_data, size)) return false;
    assert(game_data.get());
    const u8* at{ game_data.get() };
    constexpr u32 su32{ sizeof(u32) };
    const u32 num_entities{ *at }; at += su32;
    if (!num_entities) return false;

    for (u32 entity_index{ 0 }; entity_index < num_entities; ++entity_index)
    {
        game_entity::entity_info info{};
        //const u32 entity_type{ *at }; 
        // skip over entity type (for now):
        at += su32;
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

    assert(at == game_data.get() + size);
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

bool
load_engine_shaders(std::unique_ptr<u8[]>& shaders, u64& size)
{
    auto path = graphics::get_engine_shaders_path();
    return read_file(path, shaders, size);
}

}
#endif // !defined(SHIPPING)