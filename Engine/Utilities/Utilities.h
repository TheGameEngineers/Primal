// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once

#define USE_STL_VECTOR 1
#define USE_STL_DEQUE 1

#if USE_STL_VECTOR
#include <vector>
namespace primal::utl {
template<typename T>
using vector = std::vector<T>;
}
#endif

#if USE_STL_DEQUE
#include <deque>
namespace primal::utl {
template<typename T>
using deque = std::deque<T>;
}
#endif


namespace primal::utl {

// TODO: implement our own containers

}