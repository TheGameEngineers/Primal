// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include <thread>
#include <chrono>
#include <string>

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

#if _WIN64
#include <Windows.h>
class time_it
{
public:
    using clock = std::chrono::high_resolution_clock;
    using time_stamp = std::chrono::steady_clock::time_point;

    // Average frame time per second.
    constexpr float dt_avg() const { return _dt_avg * 1e-6f; }

    void begin()
    {
        _start = clock::now();
    }

    void end()
    {
        auto dt = clock::now() - _start;
        _us_avg += ((float)std::chrono::duration_cast<std::chrono::microseconds>(dt).count() - _us_avg) / (float)_counter;
        ++_counter;
        _dt_avg = _us_avg;

        if (std::chrono::duration_cast<std::chrono::seconds>(clock::now() - _seconds).count() >= 1)
        {
            OutputDebugStringA("Avg. frame (ms): ");
            OutputDebugStringA(std::to_string(_us_avg * 0.001f).c_str());
            OutputDebugStringA((" " + std::to_string(_counter)).c_str());
            OutputDebugStringA(" fps");
            OutputDebugStringA("\n");
            _us_avg = 0.f;
            _counter = 1;
            _seconds = clock::now();
        }
    }

private:
    float       _dt_avg{ 16.7f };
    float       _us_avg{ 0.f };
    int         _counter{ 1 };
    time_stamp  _start;
    time_stamp  _seconds{ clock::now() };
};
#endif // _WIN64
