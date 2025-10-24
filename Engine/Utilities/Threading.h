// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "CommonHeaders.h"

namespace primal::utl {

#if _WIN64
class ticket_mutex {
public:
    ticket_mutex() = default;
    DISABLE_COPY_AND_MOVE(ticket_mutex);

    void lock()
    {
        const u64 ticket{ (u64)_InterlockedExchangeAdd64((__int64 volatile*)&_ticket, 1) };
        while (_serving != ticket) { _mm_pause(); }
    }

    void unlock()
    {
        _InterlockedExchangeAdd64((__int64 volatile*)&_serving, 1);
    }

private:
    u64 volatile _ticket{ 0 };
    u64 volatile _serving{ 0 };
};
#else
class ticket_mutex {
public:
    ticket_mutex() = default;
    DISABLE_COPY_AND_MOVE(ticket_mutex);

    void lock()
    {
        const u64 ticket{ _ticket.fetch_add(1, std::memory_order_relaxed) };
        while (_serving != ticket) { _mm_pause(); }
    }

    void unlock()
    {
        _serving.fetch_add(1, std::memory_order_relaxed);
    }

private:
    std::atomic<u64> _ticket{ 0 };
    std::atomic<u64> _serving{ 0 };
};
#endif
}
