// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "CommonHeaders.h"
#include "D3D12Interface.h"
#include "D3D12Core.h"
#include "Graphics\GraphicsPlatformInterface.h"

namespace primal::graphics::d3d12 {

void
get_platform_interface(platform_interface& pi)
{
    pi.initialize = core::initialize;
    pi.shutdown = core::shutdown;
    pi.render = core::render;
}

}