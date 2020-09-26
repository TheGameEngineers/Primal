// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once

#pragma warning(disable: 4530) // disable exception warning

// C/C++
#include <stdint.h>
#include <assert.h>
#include <typeinfo>
#include <memory>
#include <unordered_map>

#if defined(_WIN64)
#include <DirectXMath.h>
#endif

// common headers
#include "..\Utilities\Utilities.h"
#include "..\Utilities\MathTypes.h"
#include "PrimitiveTypes.h"