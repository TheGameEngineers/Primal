// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once

#define USE_STL_VECTOR 0
#define USE_STL_DEQUE 1

#if USE_STL_VECTOR
#include <vector>
namespace primal::utl {
template<typename T>
using vector = std::vector<T>;

template<typename T>
void erase_unordered(T& v, size_t index)
{
    if(v.size() > 1)
    { 
        std::iter_swap(v.begin() + index, v.end() - 1);
        v.pop_back();
    }
    else
    {
        v.clear();
    }
}
}
#else
#include "Vector.h"

namespace primal::utl {
template<typename T>
void erase_unordered(T& v, size_t index)
{
    v.erase_unordered(index);
}
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

#include "FreeList.h"