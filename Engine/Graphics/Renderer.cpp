// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Renderer.h"
#include "GraphicsPlatformInterface.h"
#include "Direct3D12\D3D12Interface.h"


namespace primal::graphics {
namespace {

platform_interface gfx{};

bool
set_platform_interface(graphics_platform platform)
{
    switch (platform)
    {
    case graphics_platform::direct3d12:
        d3d12::get_platform_interface(gfx);
        break;
    default:
        return false;
    }

    return true;
}

} // anonymous namespace

bool
initialize(graphics_platform platform)
{
    return set_platform_interface(platform) && gfx.initialize();
}

void shutdown()
{
    gfx.shutdown();
}
}