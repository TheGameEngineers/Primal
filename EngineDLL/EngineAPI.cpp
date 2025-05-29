// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Common.h"
#include "CommonHeaders.h"
#include "Components/Script.h"
#include "Graphics/Renderer.h"
#include "Platform/PlatformTypes.h"
#include "Platform/Platform.h"
#include "Content/ContentToEngine.h"
#include "ShaderCompilation.h"
#include "../ContentTools/ToolsCommon.h"
#include "Utilities/IOStream.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif

#include <Windows.h>
#include <atlsafe.h>

using namespace primal;

namespace {
HMODULE game_code_dll{ nullptr };
using _get_script_creator = primal::script::detail::script_creator(*)(size_t);
_get_script_creator get_script_creator{ nullptr };
using _get_script_names = LPSAFEARRAY(*)(void);
_get_script_names get_script_names{ nullptr };

utl::vector<graphics::render_surface> surfaces;

struct engine_init_error {
    enum error_code :u32 {
        succeeded = 0,
        unknown,
        shader_compilation,
        graphics,
    };
};

struct shader_data
{
    u32 type;
    u32 code_size;
    u32 byte_code_size;
    u32 errors_size;
    u32 assembly_size;
    u32 hash_size;
    u8* code;
    u8* byte_code_error_assembly_hash;
    const char* function_name;
    const char* extra_args;
};

struct shader_group_data
{
    u32 type;
    u32 count;
    u32 data_size;
    u8* data;
};

u8*
patch_material_data(u8* data)
{
    utl::blob_stream_reader blob{ data };
    const u32 texture_count{ blob.read<u32>() };
    if (texture_count)
    {
        id::id_type *const texture_ids{ (id::id_type *const)blob.position() };
        blob.skip(sizeof(id::id_type) * texture_count);
        *((id::id_type**)blob.position()) = texture_ids;
    }

    return (u8*)blob.position();
}

} // anonymous namespace

EDITOR_INTERFACE engine_init_error::error_code
InitializeEngine()
{
    while (!compile_shaders())
    {
        // Pop up a message box allowing the user to retry compilation.
        if (MessageBox(nullptr, L"Failed to compile engine shaders.", L"Shader Compilation Error", MB_RETRYCANCEL) != IDRETRY)
            return engine_init_error::shader_compilation;
    }

    return graphics::initialize(graphics::graphics_platform::direct3d12) ? engine_init_error::succeeded : engine_init_error::graphics;
}

EDITOR_INTERFACE void
ShutdownEngine()
{
    graphics::shutdown();
}

EDITOR_INTERFACE u32
LoadGameCodeDll(const char* dll_path)
{
    if (game_code_dll) return FALSE;
    game_code_dll = LoadLibraryA(dll_path);
    assert(game_code_dll);

    get_script_creator = (_get_script_creator)GetProcAddress(game_code_dll, "get_script_creator");
    get_script_names = (_get_script_names)GetProcAddress(game_code_dll, "get_script_names");

    return (game_code_dll && get_script_creator && get_script_names) ? TRUE : FALSE;
}

EDITOR_INTERFACE u32
UnloadGameCodeDll()
{
    if (!game_code_dll) return FALSE;
    assert(game_code_dll);
    [[maybe_unused]] int result{ FreeLibrary(game_code_dll) };
    assert(result);
    game_code_dll = nullptr;
    return TRUE;
}
EDITOR_INTERFACE script::detail::script_creator
GetScriptCreator(const char* name)
{
    return (game_code_dll && get_script_creator) ? get_script_creator(script::detail::string_hash()(name)) : nullptr;
}

EDITOR_INTERFACE LPSAFEARRAY
GetScriptNames()
{
    return (game_code_dll && get_script_names) ? get_script_names() : nullptr;
}

EDITOR_INTERFACE u32
CreateRenderSurface(HWND host, s32 width, s32 height)
{
    assert(host);
    platform::window_init_info info{ nullptr, host, nullptr, 0, 0, width, height };
    graphics::render_surface surface{ platform::create_window(&info), {} };
    assert(surface.window.is_valid());
    surfaces.emplace_back(surface);
    return (u32)surfaces.size() - 1;
}

EDITOR_INTERFACE void
RemoveRenderSurface(u32 id)
{
    assert(id < surfaces.size());
    platform::remove_window(surfaces[id].window.get_id());
}

EDITOR_INTERFACE void
ResizeRenderSurface(u32 id)
{
    assert(id < surfaces.size());
    surfaces[id].window.resize(0, 0);
}

EDITOR_INTERFACE HWND
GetWindowHandle(u32 id)
{
    assert(id < surfaces.size());
    return (HWND)surfaces[id].window.handle();
}

EDITOR_INTERFACE id::id_type
CreateResource(u8* data, content::asset_type::type type)
{
    if (type == content::asset_type::material)
    {
        data = patch_material_data(data);
    }

    assert(data && type < content::asset_type::count);
    return content::create_resource(data, type);
}

EDITOR_INTERFACE void
DestroyResource(id::id_type id, content::asset_type::type type)
{
    assert(id::is_valid(id) && type < content::asset_type::count);
    content::destroy_resource(id, type);
}

EDITOR_INTERFACE id::id_type
AddShaderGroup(shader_group_data* data)
{
    assert(data && data->type < graphics::shader_type::count && data->count && data->data_size && data->data);
    const u32 count{ data->count };

    // data->data =
    // {
    //    u32 keys[count];
    //    struct{
    //      u64 bytecode_length;
    //      u8  hash[hash_length];
    //      u8  bytecode[bytecode_length];
    //    } blocks[count];
    // }
    //
    utl::blob_stream_reader blob{ data->data };
    const u32 *const keys{ (const u32*)blob.position() };
    blob.skip(count * sizeof(u32)); // skip keys

    const u8** shader_pointers{ (const u8**)alloca(count * sizeof(u8*)) };

    for (u32 i{ 0 }; i < count; ++i)
    {

        // NOTE: byteCodeLength is a 64-bit value!
        const u32 block_size{ sizeof(u64) + content::compiled_shader::hash_length + *(u32*)blob.position() };
        shader_pointers[i] = blob.position();
        blob.skip(block_size);
    }

    assert(blob.position() == (data->data + data->data_size));

    return content::add_shader_group(shader_pointers, count, keys);
}

EDITOR_INTERFACE void
RemoveShaderGroup(id::id_type id)
{
    content::remove_shader_group(id);
}

EDITOR_INTERFACE u32
CompileShader(shader_data* data)
{
    assert(data && data->code && data->code_size && data->function_name);
    shader_file_info info{};
    info.function = data->function_name;
    info.type = (graphics::shader_type::type)data->type;

    utl::vector<std::string> extra_args{ split(data->extra_args, ';') };
    utl::vector<std::wstring> w_extra_args{};

    for (const auto& str : extra_args)
    {
        w_extra_args.emplace_back(to_wstring(str.c_str()));
    }

    std::unique_ptr<u8[]> compiled_shader{ compile_shader(info, data->code, data->code_size, w_extra_args, true) };

    if (!compiled_shader) return FALSE;

    u64 buffer_size{ 0 };

    {
        utl::blob_stream_reader blob{ compiled_shader.get() };
        data->byte_code_size = (u32)blob.read<u64>();
        data->hash_size = content::compiled_shader::hash_length;
        blob.skip(data->hash_size + data->byte_code_size);
        data->errors_size = (u32)blob.read<u64>();
        data->assembly_size = (u32)blob.read<u64>();
        buffer_size = data->byte_code_size + data->hash_size + data->errors_size + data->assembly_size;
    }

    assert(buffer_size);

    data->byte_code_error_assembly_hash = (u8*)CoTaskMemAlloc(buffer_size);
    assert(data->byte_code_error_assembly_hash);

    {
        utl::blob_stream_reader blob{ compiled_shader.get() };
        blob.skip(sizeof(u64)); // skip the size of byte-code buffer.
        blob.read(&data->byte_code_error_assembly_hash[buffer_size - data->hash_size], data->hash_size);
        blob.read(data->byte_code_error_assembly_hash, data->byte_code_size);
        blob.skip(2 * sizeof(u64)); // skip the size of error and assembly buffers.
        blob.read(&data->byte_code_error_assembly_hash[data->byte_code_size], data->errors_size + data->assembly_size);
    }

    return TRUE;
}