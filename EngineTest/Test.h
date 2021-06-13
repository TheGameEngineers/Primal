// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include <thread>

#define TEST_ENTITY_COMPONENTS 0
#define TEST_WINDOW 0
#define TEST_RENDERER 1

class test
{
public:
    virtual bool initialize() = 0;
    virtual void run() = 0;
    virtual void shutdown() = 0;
};
