// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "CommonHeaders.h"
#include "Light.h"
#include "Camera.h"

namespace primal {

namespace graphics {
camera get_camera(id::id_type entity_id);
light get_light(id::id_type entity_id);
} // namespace graphics

namespace profiling::udp_log {
void send(u32 type, const char* msg, const char* file, u32 line);
#define SEND_UDP_LOG(type, msg) \
    primal::profiling::udp_log::send(type, msg, __FILE__, __LINE__)
#define SEND_INFO(msg) SEND_UDP_LOG(1, msg)
#define SEND_WARNING(msg) SEND_UDP_LOG(2, msg)
#define SEND_ERROR(msg) SEND_UDP_LOG(4, msg)
}

} // namespace primal