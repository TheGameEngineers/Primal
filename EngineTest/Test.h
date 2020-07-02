// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once

class test
{
public:
    virtual bool initialize() = 0;
    virtual void run() = 0;
    virtual void shutdown() = 0;
};
