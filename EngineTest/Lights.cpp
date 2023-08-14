// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "EngineAPI/GameEntity.h"
#include "EngineAPI/Light.h"
#include "EngineAPI/TransfromComponent.h"
#include "Graphics/Renderer.h"

#define RANDOM_LIGHTS 1

using namespace primal;

game_entity::entity create_one_game_entity(math::v3 position, math::v3 rotation, const char* script_name);
void remove_game_entity(game_entity::entity_id id);

namespace {
const u64 left_set{ 0 };
const u64 right_set{ 1 };
constexpr f32 inv_rand_max{ 1.f / RAND_MAX };

utl::vector<graphics::light> lights;
utl::vector<graphics::light> disabled_lights;

constexpr math::v3
rgb_to_color(u8 r, u8 g, u8 b) { return { r / 255.f, g / 255.f , b / 255.f }; }

f32 random(f32 min = 0.f) { return std::max(min, rand() * inv_rand_max); }

void create_light(math::v3 position, math::v3 rotation, graphics::light::type type, u64 light_set_key)
{
    const char* script_name{ nullptr }; // { type == graphics::light::spot ? "rotator_script" : nullptr };
    game_entity::entity_id entity_id{ create_one_game_entity(position, rotation, script_name).get_id() };

    graphics::light_init_info info{};
    info.entity_id = entity_id;
    info.type = type;
    info.light_set_key = light_set_key;
    info.intensity = 10.f;

    info.color = { random(0.2f), random(0.2f), random(0.2f) };

#if RANDOM_LIGHTS
    if (type == graphics::light::point)
    {
        info.point_params.range = random(0.5f) * 2.f;
        info.point_params.attenuation = { 1, 1, 1 };
    }
    else if (type == graphics::light::spot)
    {
        info.spot_params.range = random(0.5f) * 2.f;
        info.spot_params.umbra = (random(0.5f) - 0.4f) * math::pi;
        info.spot_params.penumbra = info.spot_params.umbra + (0.1f * math::pi);
        info.spot_params.attenuation = { 1, 1, 1 };
    }
#else
    if (type == graphics::light::point)
    {
        info.point_params.range = 1.f;
        info.point_params.attenuation = { 1, 1, 1 };
    }
    else if (type == graphics::light::spot)
    {
        info.spot_params.range = 2.f;
        info.spot_params.umbra = 0.1f * math::pi;
        info.spot_params.penumbra = info.spot_params.umbra + (0.1f * math::pi);
        info.spot_params.attenuation = { 1, 1, 1 };
    }
#endif

    graphics::light light{ graphics::create_light(info) };
    assert(light.is_valid());
    lights.push_back(light);
}

} // anonymous namespace

void
generate_lights()
{
    graphics::create_light_set(left_set);
    graphics::create_light_set(right_set);

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
    info.color = rgb_to_color(163, 47, 30);
    lights.emplace_back(graphics::create_light(info));

#if !RANDOM_LIGHTS
    create_light({ 0, -3, 0 }, {}, graphics::light::point, left_set);
    create_light({ 0, 0.2f, 1.f }, {}, graphics::light::point, left_set);
    create_light({ 0, 3, 2.5f }, {}, graphics::light::point, left_set);
    create_light({ 0, 0.1f, 7 }, { 0, 3.14f, 0 }, graphics::light::spot, left_set);
#else
    srand(37);

    constexpr f32 scale1{ 1 };
    constexpr math::v3 scale{ 1.f * scale1, 0.5f * scale1, 1.f * scale1 };
    constexpr s32 dim{ 20 };
    for (s32 x{ -dim }; x < dim; ++x)
        for (s32 y{ 0 }; y < 2 * dim; ++y)
            for (s32 z{ -dim }; z < dim; ++z)
            {
                create_light({ (f32)(x * scale.x), (f32)(y * scale.y), (f32)(z * scale.z) },
                             { random() * 3.14f, random() * 3.14f, random() * 3.14f },
                             random() > 0.5f ? graphics::light::spot : graphics::light::point, left_set);
                create_light({ (f32)(x * scale.x), (f32)(y * scale.y), (f32)(z * scale.z) },
                             { random() * 3.14f, random() * 3.14f, random() * 3.14f },
                             random() > 0.5f ? graphics::light::spot : graphics::light::point, right_set);
            }
#endif
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
    disabled_lights.clear();

    graphics::remove_light_set(left_set);
    graphics::remove_light_set(right_set);
}

void
test_lights(f32 dt)
{
#if 0
    static f32 t{0};
    t += 0.05f;
    for (u32 i{ 0 }; i < (u32)lights.size(); ++i)
    {
        f32 sine{ DirectX::XMScalarSin(t + lights[i].get_id()) };
        sine *= sine;
        lights[i].intensity(2.f * sine);
    }
#else
    u32 count{ (u32)(random(0.1f) * 100) };
    for (u32 i{ 0 }; i < count; ++i)
    {
        if (!lights.size()) break;
        const u32 index{ (u32)(random() * (lights.size() - 1)) };
        graphics::light light{lights[index]};
        light.is_enabled(false);
        utl::erase_unordered(lights, index);
        disabled_lights.emplace_back(light);
    }

    count = (u32)(random(0.1f) * 50);
    for (u32 i{ 0 }; i < count; ++i)
    {
        if (!lights.size()) break;
        const u32 index{ (u32)(random() * (lights.size() - 1)) };
        graphics::light light{lights[index]};
        const game_entity::entity_id id{light.entity_id()};
        graphics::remove_light(light.get_id(), light.ligh_set_key());
        remove_game_entity(id);
        utl::erase_unordered(lights, index);
    }

    count = (u32)(random(0.1f) * 50);
    for (u32 i{ 0 }; i < count; ++i)
    {
        if (!disabled_lights.size()) break;
        const u32 index{ (u32)(random() * (disabled_lights.size() - 1)) };
        graphics::light light{disabled_lights[index]};
        const game_entity::entity_id id{light.entity_id()};
        graphics::remove_light(light.get_id(), light.ligh_set_key());
        remove_game_entity(id);
        utl::erase_unordered(disabled_lights, index);
    }

    count = (u32)(random(0.1f) * 100);
    for (u32 i{ 0 }; i < count; ++i)
    {
        if (!disabled_lights.size()) break;
        const u32 index{ (u32)(random() * (disabled_lights.size() - 1)) };
        graphics::light light{disabled_lights[index]};
        light.is_enabled(true);
        utl::erase_unordered(disabled_lights, index);
        lights.emplace_back(light);
    }

    constexpr f32 scale1{ 1 };
    constexpr math::v3 scale{ 1.f * scale1, 0.5f * scale1, 1.f * scale1 };
    count = (u32)(random(0.1f) * 50);

    for (u32 i{ 0 }; i < count; ++i)
    {
        math::v3 p1{(random() * 2 - 1.f) * 13.f * scale.x, random() * 2 * 13.f * scale.y, (random() * 2 - 1.f) * 13.f * scale.z};
        math::v3 p2{(random() * 2 - 1.f) * 13.f * scale.x, random() * 2 * 13.f * scale.y, (random() * 2 - 1.f) * 13.f * scale.z};
        create_light(p1, { random() * 3.14f, random() * 3.14f, random() * 3.14f },
                     random() > 0.5f ? graphics::light::spot : graphics::light::point, left_set);
        create_light(p2, { random() * 3.14f, random() * 3.14f, random() * 3.14f },
                     random() > 0.5f ? graphics::light::spot : graphics::light::point, right_set);
    }

#endif
}
