// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#if !defined(SHIPPING) || defined(USE_WITH_EDITOR)
#pragma once
#include "CommonHeaders.h"

namespace primal::profiling::udp_log {

struct message_type {
    enum type : u32 {
        info = 1,
        warning = 2,
        error = 4,
    };
};

bool initialize();
void shutdown();

void send(u32 type, const char* msg, const char* file, u32 line);

#define SEND_UDP_LOG(type, msg) \
    primal::profiling::udp_log::send(type, msg, __FILE__, __LINE__)
} // namespace primal::profiling::udp_log
#else

bool initialize() { return true; }
void shutdown() {}
void send(u32 type, const char* msg, const char* file, u32 line) {}

#define SEND_UDP_LOG(type, msg) ((void)0)
#endif