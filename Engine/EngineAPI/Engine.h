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

} // namespace primal