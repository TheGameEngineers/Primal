// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#if !defined(SHIPPING) || defined(USE_WITH_EDITOR)
#ifdef _WIN64
#include <winsock2.h>
#include <WS2tcpip.h>
#include <windows.h>
#pragma comment(lib, "ws2_32.lib")
#endif

#include <string>

#include "UdpLog.h"
#include "Utilities/IOStream.h"

namespace primal::profiling::udp_log {
namespace {

struct message
{
    std::string message;
    std::string file;
    u32 line;
    u32 type;
    u32 process_id;

    size_t size() const
    {
        return
            sizeof(u32) + message.length() +        // message length + message
            sizeof(u32) + file.length() +           // file length + file name
            sizeof(line) + sizeof(type) + sizeof(process_id);
    }
};

#ifdef _WIN64
SOCKET                  win_socket{ INVALID_SOCKET };
sockaddr_in             socket_address{};

std::thread             reporter_thread;
std::mutex              mutex{};
std::condition_variable msg_cv{};
bool                    is_running{ true };

utl::vector<message>    messages{};
utl::vector<u8>         buffer{};
const u32               process_id{ GetCurrentProcessId() };

bool
init_socket()
{
    assert(win_socket == INVALID_SOCKET);

    WSADATA wsa_data;
    s32 result{ WSAStartup(MAKEWORD(2, 2), &wsa_data) };
    assert(!result);
    if (result)  return false;

    if (win_socket != INVALID_SOCKET)
    {
        closesocket(win_socket);
        win_socket = INVALID_SOCKET;
    }

    if (win_socket == INVALID_SOCKET)
    {
        win_socket = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
        assert(win_socket != INVALID_SOCKET);
        if (win_socket == INVALID_SOCKET) return false;

        // Enable broadcast
        bool enabled = true;
        setsockopt(win_socket, SOL_SOCKET, SO_BROADCAST, (char*)&enabled, sizeof(bool));

        socket_address.sin_family = AF_INET;
        socket_address.sin_port = htons(8080);
        if (inet_pton(AF_INET, "239.0.0.222", &socket_address.sin_addr) != 1)
            return false;

        return true;
    }

    return false;
}

void
close_socket()
{
    if (win_socket != INVALID_SOCKET)
    {
        closesocket(win_socket);
        win_socket = INVALID_SOCKET;
    }
    WSACleanup();
}
#else
// Init socket for other platforms if needed
#endif
} // anonymous namespace

bool
initialize()
{
    if (!init_socket()) return false;
    assert(reporter_thread.joinable() == false);

    reporter_thread = std::thread([]() {
        while (is_running)
        {
            std::unique_lock lock(mutex);
            msg_cv.wait(lock, [] { return !messages.empty() || !is_running; });

            if (!messages.empty())
            {
                for (const auto& msg : messages)
                {
                    constexpr size_t max_udp_payload_size{ 65507 };
                    if (msg.size() > max_udp_payload_size)
                    {
                        OutputDebugStringA("UdpLog: message exceeds the maximum UDP payload; dropping it.\n");
                        continue;
                    }

                    buffer.resize(msg.size());
                    utl::blob_stream_writer writer{ buffer.data(), buffer.size() };
                    writer.write((u32)msg.message.length());
                    writer.write(msg.message.c_str(), msg.message.length());
                    writer.write((u32)msg.file.length());
                    writer.write(msg.file.c_str(), msg.file.length());
                    writer.write(msg.line);
                    writer.write(msg.type);
                    writer.write(process_id);

                    const s32 sent{ sendto(win_socket, (const char*)buffer.data(), (int)buffer.size(),
                        0, (sockaddr*)&socket_address, sizeof(socket_address)) };

                    if (sent == SOCKET_ERROR)
                    {
                        const s32 error{ WSAGetLastError() };
                        OutputDebugStringA(("UdpLog: sendto failed (WSA error " +
                            std::to_string(error) + ").\n").c_str());
                    }
                    else if ((size_t)sent != buffer.size())
                    {
                        OutputDebugStringA("UdpLog: sendto sent an incomplete datagram.\n");
                    }
                }

                messages.clear();
            }
        }
        });
    return true;
}

void
shutdown()
{
    is_running = false;
    msg_cv.notify_all();
    if (reporter_thread.joinable())
        reporter_thread.join();
    close_socket();
}

void
send(u32 type, const char* msg, const char* file, u32 line)
{
    assert(type <= message_type::error);
    assert(msg && file);
    std::lock_guard lock(mutex);
    messages.emplace_back(msg, file, line, type);
    msg_cv.notify_one();
}

} // namespace primal::profiling::udp_log
#endif