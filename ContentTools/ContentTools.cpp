// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "ToolsCommon.h"

namespace primal::tools {
extern void ShutDownTextureTools();
}

EDITOR_INTERFACE void
ShutDownContentTools()
{
    using namespace primal::tools;
    ShutDownTextureTools();
}