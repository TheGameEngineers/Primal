// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "Test.h"

class engine_test : public test
{
public: 
    bool initialize() override;
    void run() override;
    void shutdown() override;
};