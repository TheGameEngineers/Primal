// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
namespace primal::graphics {
struct platform_interface;

namespace d3d12 {
void get_platform_interface(platform_interface& pi);
}
}
