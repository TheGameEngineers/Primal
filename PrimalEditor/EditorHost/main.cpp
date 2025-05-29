// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.

#include <stdint.h>
#include <assert.h>
#include <crtdbg.h>

#include <nethost.h>
#include <hostfxr.h>
#include <coreclr_delegates.h>
#include <Windows.h>

#pragma comment(lib, "nethost.lib")

extern "C" { __declspec(dllexport) extern const UINT D3D12SDKVersion = 615; }
extern "C" { __declspec(dllexport) extern const char* D3D12SDKPath = u8".\\D3D12\\"; }

namespace {

HMODULE hostfxr_lib{ nullptr };
// Using the nethost library, discover the location of hostfxr
bool
load_hostfxr()
{
    wchar_t buffer[MAX_PATH]{};
    size_t buffer_size{ sizeof(buffer) / sizeof(wchar_t) };
    int32_t rc{ get_hostfxr_path(buffer, &buffer_size, nullptr) };
    if (rc != 0)
        return false;

    hostfxr_lib = LoadLibraryW(buffer);
    assert(hostfxr_lib);
    return hostfxr_lib != nullptr;
}

int32_t
run_app(const int argc, const wchar_t** argv)
{
    if (!load_hostfxr()) return 0x80008082; // StatusCode::CoreHostLibLoadFailure

    void* context_handle{ nullptr };
    auto init = (hostfxr_initialize_for_dotnet_command_line_fn)GetProcAddress(hostfxr_lib, "hostfxr_initialize_for_dotnet_command_line");
    if (!init || init(argc, argv, nullptr, &context_handle) || !context_handle) return 0x80008082;

    auto run = (hostfxr_run_app_fn)GetProcAddress(hostfxr_lib, "hostfxr_run_app");
    if (!run) return 0x80008082;

    return run(context_handle);
}
} // anonymous namespace

int WINAPI WinMain(HINSTANCE, HINSTANCE, LPSTR, int)
{
#if defined(DEBUG) || defined(_DEBUG)
    _CrtSetDbgFlag(_CRTDBG_ALLOC_MEM_DF | _CRTDBG_LEAK_CHECK_DF);
    // When a leak is detected, call _CrtSetBreakAlloc(N) with leak number N to
    // break at leak site.
#endif

    constexpr int max_args{ 100 };
    int argc{ 0 };
    LPWSTR* args{ CommandLineToArgvW(GetCommandLineW(), &argc) };
    if (!argc || argc > max_args || !args) return 0x80008081; // StatusCode::InvalidArgFailure

    const wchar_t* argv[max_args]{};
    argv[0] = L"PrimalEditor.dll";

    for (size_t i{ 1 }; i < argc; ++i)
    {
        argv[i] = args[i];
    }

    const int32_t rc{ run_app(argc, &argv[0]) };
    LocalFree(args);
    FreeLibrary(hostfxr_lib);
    return rc;
}
