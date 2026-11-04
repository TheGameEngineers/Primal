// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "Profiling/UdpLog.cpp"
#include "ToolsCommon.h"

namespace primal::tools {
extern void ShutDownTextureTools();
}

using namespace primal;

EDITOR_INTERFACE b32
InitializeContentTools()
{
    if (profiling::udp_log::initialize())
    {
        SEND_UDP_LOG(profiling::udp_log::message_type::info, "Content tools initialized successfully.");
        return TRUE;
    }
    return FALSE;
}

EDITOR_INTERFACE void
ShutDownContentTools()
{
    tools::ShutDownTextureTools();
    profiling::udp_log::shutdown();
}