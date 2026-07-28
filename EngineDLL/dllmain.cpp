// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma comment(lib, "engine.lib")

// DllMain.cpp : Defines the entry point for the DLL application.
#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <crtdbg.h>

#if _DEBUG
BOOL APIENTRY DllMain( HMODULE,
                       DWORD  ul_reason_for_call,
                       LPVOID)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        _CrtSetDbgFlag(_CRTDBG_ALLOC_MEM_DF | _CRTDBG_LEAK_CHECK_DF);
        // When a leak is detected, call _CrtSetBreakAlloc(N) with leak number N to
        // break at leak site.
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
#endif

