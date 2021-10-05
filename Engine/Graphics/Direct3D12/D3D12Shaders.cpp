// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "D3D12Shaders.h"
#include "Content\ContentLoader.h"

namespace primal::graphics::d3d12::shaders {
namespace {

typedef struct compiled_shader
{
    u64         size;
    const u8*   byte_code;
} const *compiled_shader_ptr;

// Each element in this array points to an offset withing the shaders blob.
compiled_shader_ptr engine_shaders[engine_shader::count]{};

// This is a chunk of memory that contains all compiled engine shaders.
// The blob is an array of shader byte code consisting of a u64 size and 
// an array of bytes.
std::unique_ptr<u8[]> shaders_blob{};

bool
load_engine_shaders()
{
    assert(!shaders_blob);
    u64 size{ 0 };
    bool result{ content::load_engine_shaders(shaders_blob, size) };
    assert(shaders_blob && size);

    u64 offset{ 0 };
    u32 index{ 0 };
    while (offset < size && result)
    {
        assert(index < engine_shader::count);
        compiled_shader_ptr& shader{ engine_shaders[index] };
        assert(!shader);
        result &= index < engine_shader::count && !shader;
        if (!result) break;
        shader = reinterpret_cast<const compiled_shader_ptr>(&shaders_blob[offset]);
        offset += sizeof(u64) + shader->size;
        ++index;
    }
    assert(offset == size && index == engine_shader::count);

    return result;
}

} // anonymous namespace

bool
initialize()
{
    return load_engine_shaders();
}

void
shutdown()
{
    for (u32 i{ 0 }; i < engine_shader::count; ++i)
    {
        engine_shaders[i] = {};
    }
    shaders_blob.reset();
}

D3D12_SHADER_BYTECODE
get_engine_shader(engine_shader::id id)
{
    assert(id < engine_shader::count);
    const compiled_shader_ptr shader{ engine_shaders[id] };
    assert(shader && shader->size);
    return { &shader->byte_code, shader->size };
}

}