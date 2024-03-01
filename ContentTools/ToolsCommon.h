// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#pragma once
#include "CommonHeaders.h"

#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <wrl.h>

#ifndef EDITOR_INTERFACE
#define EDITOR_INTERFACE extern "C" __declspec(dllexport)
#endif // !EDITOR_INTERFACE

class progression
{
public:
    using progress_callback = void(*)(s32, s32);

    progression() = default;
    explicit progression(progress_callback callback)
        : _callback{ callback }
    {}

    DISABLE_COPY(progression);

    void callback(u32 value, u32 max_value)
    {
        _value = value;
        _max_value = max_value;
        if (_callback) _callback(value, max_value);
    }

    [[nodiscard]] constexpr u32 max_value() const { return _max_value; }
    [[nodiscard]] constexpr u32 value() const { return _value; }
private:
    progress_callback   _callback{ nullptr };
    u32                 _value{ 0 };
    u32                 _max_value{ 0 };
};

inline bool
file_exists(const char* file)
{
    const DWORD attr{ GetFileAttributesA(file) };
    return attr != INVALID_FILE_ATTRIBUTES && !(attr & FILE_ATTRIBUTE_DIRECTORY);
}

inline std::wstring
to_wstring(const char* cstr)
{
    std::string s{ cstr };
    return { s.begin(), s.end() };
}

inline primal::utl::vector<std::string>
split(std::string s, char delimiter)
{
    size_t start{ 0 };
    size_t end{ 0 };
    std::string substring;
    primal::utl::vector<std::string> strings;

    while ((end = s.find(delimiter, start)) != std::string::npos)
    {
        substring = s.substr(start, end - start);
        start = end + sizeof(char);
        strings.emplace_back(substring);
    }

    strings.emplace_back(s.substr(start));
    return strings;
}