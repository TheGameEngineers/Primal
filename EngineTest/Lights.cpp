// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "EngineAPI/GameEntity.h"
#include "EngineAPI/Light.h"
#include "EngineAPI/TransfromComponent.h"
#include "Graphics/Renderer.h"

using namespace primal;

game_entity::entity create_one_game_entity(math::v3 position, math::v3 rotation, const char* script_name);
void remove_game_entity(game_entity::entity_id id);

namespace {
const u64 left_set{ 0 };
const u64 right_set{ 0 };

utl::vector<graphics::light> lights;

constexpr math::v3
rgb_to_color(u8 r, u8 g, u8 b) { return { r / 255.f, g / 255.f , b / 255.f }; }

} // anonymous namespace

void
generate_lights()
{
    // LEFT_SET
    graphics::light_init_info info{};
    info.entity_id = create_one_game_entity({}, { 0, 0, 0 }, nullptr).get_id();
    info.type = graphics::light::directional;
    info.light_set_key = left_set;
    info.intensity = 1.f;
    info.color = rgb_to_color(174, 174, 174);

    lights.emplace_back(graphics::create_light(info));

    info.entity_id = create_one_game_entity({}, { math::pi * 0.5f, 0, 0 }, nullptr).get_id();
    info.color = rgb_to_color(17, 27, 48);
    lights.emplace_back(graphics::create_light(info));

    info.entity_id = create_one_game_entity({}, { -math::pi * 0.5f, 0, 0 }, nullptr).get_id();
    info.color = rgb_to_color(63, 47, 30);
    lights.emplace_back(graphics::create_light(info));

    // RIGHT_SET
    info.entity_id = create_one_game_entity({}, { 0, 0, 0 }, nullptr).get_id();
    info.light_set_key = right_set;
    info.color = rgb_to_color(150, 100, 200);
    lights.emplace_back(graphics::create_light(info));

    info.entity_id = create_one_game_entity({}, { math::pi * 0.5f, 0, 0 }, nullptr).get_id();
    info.color = rgb_to_color(17, 27, 48);
    lights.emplace_back(graphics::create_light(info));

    info.entity_id = create_one_game_entity({}, { -math::pi * 0.5f, 0, 0 }, nullptr).get_id();
    info.color = rgb_to_color(63, 47, 30);
    lights.emplace_back(graphics::create_light(info));
}

void
remove_lights()
{
    for (auto& light : lights)
    {
        const game_entity::entity_id id{ light.entity_id() };
        graphics::remove_light(light.get_id(), light.ligh_set_key());
        remove_game_entity(id);
    }

    lights.clear();
}
