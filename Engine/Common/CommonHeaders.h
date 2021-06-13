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

#ifndef DISABLE_COPY
#define DISABLE_COPY(T)                     \
            explicit T(const T&) = delete;  \
            T& operator=(const T&) = delete;
#endif

#ifndef DISABLE_MOVE
#define DISABLE_MOVE(T)                 \
            explicit T(T&&) = delete;   \
            T& operator=(T&&) = delete;
#endif

#ifndef DISABLE_COPY_AND_MOVE
#define DISABLE_COPY_AND_MOVE(T) DISABLE_COPY(T) DISABLE_MOVE(T)
#endif

// common headers
#include "PrimitiveTypes.h"
#include "..\Utilities\Math.h"
#include "..\Utilities\Utilities.h"
#include "..\Utilities\MathTypes.h"
#include "Id.h"

#ifdef _DEBUG
#define DEBUG_OP(x) x
#else
#define DEBUG_OP(x) (void(0))
#endif