// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
//#define GAME_CODE_DLL
#ifdef GAME_CODE_DLL

#include "EditorAPI.h"
#include "../EngineAPI/GameEntity.h"
#include "../EngineAPI/Input.h"
#include <atlsafe.h>

namespace primal {
namespace editor {
editor_api api{};
} // namespace editor

extern "C" __declspec(dllexport)
void SetEditorAPI(uintptr_t a)
{
    assert(a);
    editor::api.set((editor::editor_api*)a);
}

namespace transform {

math::v4
component::rotation() const
{
    assert(is_valid());
    f32 x, y, z;
    id::id_type id{ get_id() };
    editor::api.GetRotation(&id, &x, &y, &z, 1);
    math::v3 w{ x, y, z };

    w = math::to_radians(w);
    transform::component xform{ transform::transform_id{id} };
    DirectX::XMVECTOR q{ xform.calculate_absolute_rotation(w) };
    math::v4 result{};
    DirectX::XMStoreFloat4(&result, q);
    return result;
}

math::v3
component::position() const
{
    assert(is_valid());
    f32 x, y, z;
    id::id_type id{ get_id() };
    editor::api.GetPosition(&id, &x, &y, &z, 1);
    return math::v3(x, y, z);
}
math::v3
component::scale() const
{
    assert(is_valid());
    f32 x, y, z;
    id::id_type id{ get_id() };
    editor::api.GetScale(&id, &x, &y, &z, 1);
    return math::v3(x, y, z);
}


math::v3
component::right() const
{
    assert(is_valid());
    math::m3x3 f{ local_frame() };
    return { f._11, f._12, f._13 };
}

math::v3
component::up() const
{
    assert(is_valid());
    math::m3x3 f{ local_frame() };
    return { f._21, f._22, f._23 };
}

math::v3
component::front() const
{
    assert(is_valid());
    math::m3x3 f{ local_frame() };
    return { f._31, f._32, f._33 };
}

math::m3x3
component::local_frame() const
{
    assert(is_valid());
    math::m3x3 frame{};
    editor::api.GetLocalFrame(get_id(), &frame);
    return frame;
}

DirectX::XMVECTOR
component::calculate_local_position(math::v3 delta) const
{
    assert(is_valid());
    math::m3x3 f{ local_frame() };
    using namespace DirectX;
    XMMATRIX frame{ XMLoadFloat3x3(&f) };
    XMVECTOR l_pos{ XMLoadFloat3(&delta) };
    l_pos = XMVector3Transform(l_pos, frame);
    math::v3 p{ position() };
    XMVECTOR w_pos{ XMLoadFloat3(&p) };

    return w_pos + l_pos;
}

DirectX::XMVECTOR
component::calculate_absolute_rotation(math::v3 rotation) const
{
    assert(is_valid());
    return DirectX::XMQuaternionRotationRollPitchYawFromVector(XMLoadFloat3(&rotation));
}

DirectX::XMVECTOR
component::calculate_local_rotation(math::v3 delta) const
{
    assert(is_valid());
    using namespace DirectX;
    XMVECTOR d{ XMQuaternionRotationRollPitchYawFromVector(XMLoadFloat3(&delta)) };
    math::v4 r{ rotation() };
    XMVECTOR q{ XMLoadFloat4(&r) };

    return XMQuaternionMultiply(d, q);
}

DirectX::XMVECTOR
component::calculate_world_rotation(math::v3 delta) const
{
    assert(is_valid());
    math::v3 axis{ 1.f, 0.f, 0.f };
    f32 angle{ delta.x };

    if (abs(delta.x) < math::epsilon)
    {
        if (abs(delta.z) < math::epsilon)
        {
            axis = { 0.f, 1.f, 0.f };
            angle = delta.y;
        }
        else if (abs(delta.y) < math::epsilon)
        {
            axis = { 0.f, 0.f, 1.f };
            angle = delta.z;
        }
    }

    using namespace DirectX;
    XMVECTOR a{ XMLoadFloat3(&axis) };
    XMVECTOR d{ XMQuaternionRotationNormal(a, angle) };
    math::v4 r{ rotation() };
    XMVECTOR q{ XMLoadFloat4(&r) };

    return XMQuaternionMultiply(q, d);
}

} // namespace transform

namespace script {
void
entity_script::set_rotation(const game_entity::entity *const entity, math::v4 rotation_quaternion)
{
    assert(entity);
    editor::api.ScriptSetRotation((*entity).get_id(), rotation_quaternion.x, rotation_quaternion.y, rotation_quaternion.z, rotation_quaternion.w);
}

void
entity_script::set_position(const game_entity::entity *const entity, math::v3 position)
{
    assert(entity);
    editor::api.ScriptSetPosition((*entity).get_id(), position.x, position.y, position.z);
}

void
entity_script::set_scale(const game_entity::entity *const entity, math::v3 scale)
{
    assert(entity);
    editor::api.ScriptSetScale((*entity).get_id(), scale.x, scale.y, scale.z);
}

namespace detail {
namespace {
using script_registry = std::unordered_map<size_t, script_creator>;
script_registry&
registry()
{
    // NOTE: we put this static variable in a function because of
    //       the initialization order of static data. This way, we can
    //       be certain that the data is initialized before accessing it.
    static script_registry reg;
    return reg;
}

utl::vector<std::string>&
script_names()
{
    // NOTE: we put this static variable in a function because of
    //       the initialization order of static data. This way, we can
    //       be certain that the data is initialized before accessing it.
    static utl::vector<std::string> names;
    return names;
}
} // anonynous namespace
u8
register_script(size_t tag, script_creator func)
{
    bool result{ registry().insert(script_registry::value_type{tag, func}).second };
    assert(result);
    return result;
}

u8
add_script_name(const char* name)
{
    script_names().emplace_back(name);
    return true;
}

script_creator
get_script_creator(size_t tag)
{
    auto script = registry().find(tag);
    assert(script != registry().end() && script->first == tag);
    return script != registry().end() && script->first == tag ? script->second : nullptr;
}

extern "C" __declspec(dllexport)
LPSAFEARRAY
get_script_names()
{
    const u32 size{ (u32)script_names().size() };
    if (!size) return nullptr;
    CComSafeArray<BSTR> names(size);
    for (u32 i{ 0 }; i < size; ++i)
    {
        names.SetAt(i, A2BSTR_EX(script_names()[i].c_str()), false);
    }
    return names.Detach();
}

} // namespace detail
} // namespace script

namespace input {

void
get(input_source::type type, input_code::code code, input_value& value)
{
    editor::api.GetInput(type, code, (f32*)&value);
}

void
get(u64 binding, input_value& value)
{
    editor::api.GetInputBinding(binding, (f32*)&value);
}

detail::input_system_base::input_system_base()
{
    editor::api.AddInputCallback(this);
}

detail::input_system_base::~input_system_base()
{
    editor::api.RemoveInputCallback(this);
}

} // namespace input

namespace game_entity {
transform::component
entity::transform() const
{
    return transform::component{ transform::transform_id{editor::api.GetComponentId(_id, component_type::transform)} };
}

script::component
entity::script() const
{
    return script::component{ script::script_id{editor::api.GetComponentId(_id, component_type::script)} };
}

geometry::component
entity::geometry() const
{
    return geometry::component{ geometry::geometry_id{editor::api.GetComponentId(_id, component_type::geometry)} };
}
} // namespace game_entity

namespace graphics {

camera
get_camera(id::id_type entity_id)
{
    assert(id::is_valid(entity_id));
    id::id_type id{ id::invalid_id };
    editor::api.GetCameraId(entity_id, &id);
    assert(id::is_valid(id));
    return id::is_valid(id) ? camera{ graphics::camera_id{id} } : camera{};
}

void
camera::up(math::v3 up) const
{
    assert(is_valid());
    id::id_type id{ get_id() };
    editor::api.SetCameraUp(&id, &up.x, &up.y, &up.z, 1);
}

void
camera::field_of_view(f32 fov) const
{
    assert(is_valid());
    id::id_type id{ get_id() };
    editor::api.SetCameraFieldOfView(&id, &fov, 1);
}

void
camera::aspect_ratio(f32 aspect_ratio) const
{
    assert(is_valid());
    id::id_type id{ get_id() };
    editor::api.SetCameraAspectRatio(&id, &aspect_ratio, 1);
}

void
camera::orthographic_size(f32 size) const
{
    assert(is_valid());
    id::id_type id{ get_id() };
    editor::api.SetCameraOrthographicSize(&id, &size, 1);
}

void
camera::range(f32 near_z, f32 far_z) const
{
    assert(is_valid());
    id::id_type id{ get_id() };
    editor::api.SetCameraRange(&id, &near_z, &far_z, 1);
}

math::m4x4
camera::view() const
{
    assert(is_valid());
    math::m4x4 matrix{};
    id::id_type id{ get_id() };
    editor::api.GetCameraView(&id, &matrix, 1);
    return matrix;
}

math::m4x4
camera::projection() const
{
    assert(is_valid());
    math::m4x4 matrix{};
    id::id_type id{ get_id() };
    editor::api.GetCameraProjection(&id, &matrix, 1);
    return matrix;
}

math::m4x4
camera::inverse_projection() const
{
    assert(is_valid());
    math::m4x4 matrix{};
    id::id_type id{ get_id() };
    editor::api.GetCameraInverseProjection(&id, &matrix, 1);
    return matrix;
}

math::m4x4
camera::view_projection() const
{
    assert(is_valid());
    math::m4x4 matrix{};
    id::id_type id{ get_id() };
    editor::api.GetCameraViewProjection(&id, &matrix, 1);
    return matrix;
}

math::m4x4
camera::inverse_view_projection() const
{
    assert(is_valid());
    math::m4x4 matrix{};
    id::id_type id{ get_id() };
    editor::api.GetCameraInverseViewProjection(&id, &matrix, 1);
    return matrix;
}

math::v3
camera::up() const
{
    assert(is_valid());
    math::v3 up_vector{};
    id::id_type id{ get_id() };
    editor::api.GetCameraUp(&id, &up_vector.x, &up_vector.y, &up_vector.z, 1);
    return up_vector;
}

f32
camera::near_z() const
{
    assert(is_valid());
    f32 near_z{};
    f32 far_z{};
    id::id_type id{ get_id() };
    editor::api.GetCameraRange(&id, &near_z, &far_z, 1);
    return near_z;
}

f32
camera::far_z() const
{
    assert(is_valid());
    f32 near_z{};
    f32 far_z{};
    id::id_type id{ get_id() };
    editor::api.GetCameraRange(&id, &near_z, &far_z, 1);
    return far_z;
}

f32
camera::field_of_view() const
{
    assert(is_valid());
    f32 fov{};
    id::id_type id{ get_id() };
    editor::api.GetCameraFieldOfView(&id, &fov, 1);
    return fov;
}

f32
camera::aspect_ratio() const
{
    assert(is_valid());
    f32 aspect_ratio{};
    id::id_type id{ get_id() };
    editor::api.GetCameraAspectRatio(&id, &aspect_ratio, 1);
    return aspect_ratio;
}

f32
camera::orthographic_size() const
{
    assert(is_valid());
    f32 size{};
    id::id_type id{ get_id() };
    editor::api.GetCameraOrthographicSize(&id, &size, 1);
    return size;
}

camera::type
camera::projection_type() const
{
    assert(is_valid());
    return (camera::type)editor::api.GetCameraProjectionType(get_id());
}

id::id_type
camera::entity_id() const
{
    assert(is_valid());
    return editor::api.GetCameraEntityId(get_id());
}

light
get_light(id::id_type entity_id)
{
    assert(id::is_valid);
    id::id_type id{ id::invalid_id };
    u64 light_set_key{};
    editor::api.GetLightId(entity_id, &id, &light_set_key);
    assert(id::is_valid(id));
    return id::is_valid(id) ? light{ graphics::light_id{id}, light_set_key } : light{};
}

void
light::is_enabled(bool is_enabled) const
{
    assert(is_valid());
    id::id_type id{ get_id() };
    u32 enabled{ is_enabled ? (u32)1 : (u32)0 };
    u64 key{ _light_set_key };
    editor::api.SetLightIsEnabled(&id, &key, &enabled, 1);
}

void
light::intensity(f32 intensity) const
{
    assert(is_valid());
    id::id_type id{ get_id() };
    u64 key{ _light_set_key };
    editor::api.SetLightIntensity(&id, &key, &intensity, 1);
}

void
light::color(math::v3 color) const
{
    assert(is_valid());
    id::id_type id{ get_id() };
    u64 key{ _light_set_key };
    editor::api.SetLightColor(&id, &key, &color.x, &color.y, &color.z, 1);
}

void
light::attenuation(math::v3 attenuation) const
{
    assert(is_valid());
    id::id_type id{ get_id() };
    u64 key{ _light_set_key };
    editor::api.SetLightAttenuation(&id, &key, &attenuation.x, &attenuation.y, &attenuation.z, 1);
}

void
light::range(f32 range) const
{
    assert(is_valid());
    id::id_type id{ get_id() };
    u64 key{ _light_set_key };
    editor::api.SetLightRange(&id, &key, &range, 1);
}

void
light::cone_angles(f32 umbra, f32 penumbra) const
{
    assert(is_valid());
    id::id_type id{ get_id() };
    u64 key{ _light_set_key };
    umbra *= math::to_deg;
    penumbra *= math::to_deg;
    editor::api.SetLightConeAngles(&id, &key, &umbra, &penumbra, 1);
}

bool
light::is_enabled() const
{
    assert(is_valid());
    u32 is_enabled;
    id::id_type id{ get_id() };
    u64 key{ _light_set_key };
    editor::api.GetLightIsEnabled(&id, &key, &is_enabled, 1);
    return is_enabled;
}

f32
light::intensity() const
{
    assert(is_valid());
    f32 intensity;
    id::id_type id{ get_id() };
    u64 key{ _light_set_key };
    editor::api.GetLightIntensity(&id, &key, &intensity, 1);
    return intensity;
}

math::v3
light::color() const
{
    assert(is_valid());
    math::v3 color{};
    id::id_type id{ get_id() };
    u64 key{ _light_set_key };
    editor::api.GetLightColor(&id, &key, &color.x, &color.y, &color.z, 1);
    return color;
}

math::v3
light::attenuation() const
{
    assert(is_valid());
    math::v3 attenuation{};
    id::id_type id{ get_id() };
    u64 key{ _light_set_key };
    editor::api.GetLightAttenuation(&id, &key, &attenuation.x, &attenuation.y, &attenuation.z, 1);
    return attenuation;
}

f32
light::range() const
{
    assert(is_valid());
    f32 range;
    id::id_type id{ get_id() };
    u64 key{ _light_set_key };
    editor::api.GetLightRange(&id, &key, &range, 1);
    return range;
}

f32
light::umbra() const
{
    assert(is_valid());
    f32 umbra, penumbra;
    id::id_type id{ get_id() };
    u64 key{ _light_set_key };
    editor::api.GetLightConeAngles(&id, &key, &umbra, &penumbra, 1);
    return umbra;
}

f32
light::penumbra() const
{
    assert(is_valid());
    f32 umbra, penumbra;
    id::id_type id{ get_id() };
    u64 key{ _light_set_key };
    editor::api.GetLightConeAngles(&id, &key, &umbra, &penumbra, 1);
    return penumbra;
}

light::type
light::light_type() const
{
    assert(is_valid());
    return (light::type)editor::api.GetLightType(_id, _light_set_key);
}

id::id_type
light::entity_id() const
{
    assert(is_valid());
    return editor::api.GetLightEntityId(_id, _light_set_key);
}

} // namespace graphics

} // namespace primal
#endif // GAME_CODE_DLL