#include "PipeClient.h"

#include <algorithm>
#include <chrono>
#include <cstdlib>

namespace quickwarp
{
    namespace
    {
        constexpr wchar_t PipePath[] = L"\\\\.\\pipe\\DSRQuickWarp";
    }

    PipeClient::PipeClient() = default;

    PipeClient::~PipeClient()
    {
        Stop();
    }

    void PipeClient::Start()
    {
        if (_running.exchange(true))
            return;
        _worker = std::thread(&PipeClient::Worker, this);
    }

    void PipeClient::Stop()
    {
        if (!_running.exchange(false))
            return;

        _queueCv.notify_all();
        if (_pipe != INVALID_HANDLE_VALUE)
            CancelIoEx(_pipe, nullptr);
        if (_worker.joinable())
            _worker.join();
        Disconnect();
    }

    void PipeClient::Queue(const std::string& command)
    {
        {
            std::lock_guard<std::mutex> lock(_queueMutex);
            _commands.push_back(command);
        }
        _queueCv.notify_one();
    }

    Snapshot PipeClient::GetSnapshot() const
    {
        std::lock_guard<std::mutex> lock(_stateMutex);
        return _snapshot;
    }

    void PipeClient::Worker()
    {
        bool needsInitialState = true;

        while (_running)
        {
            if (_pipe == INVALID_HANDLE_VALUE)
            {
                if (!Connect())
                {
                    PublishDisconnected("Waiting for QuickWarp host...");
                    for (int i = 0; i < 10 && _running; ++i)
                        std::this_thread::sleep_for(std::chrono::milliseconds(100));
                    continue;
                }
                needsInitialState = true;
            }

            std::string command;
            if (needsInitialState)
            {
                command = "STATE";
                needsInitialState = false;
            }
            else
            {
                std::unique_lock<std::mutex> lock(_queueMutex);
                _queueCv.wait_for(lock, std::chrono::milliseconds(500), [this]
                {
                    return !_running || !_commands.empty();
                });

                if (!_running)
                    break;

                if (_commands.empty())
                {
                    // Poll lightweight host state while idle. This lets the injected
                    // overlay receive completion/timeout status from asynchronous
                    // cross-map warps without needing another user key press.
                    command = "STATE";
                }
                else
                {
                    command = std::move(_commands.front());
                    _commands.pop_front();
                }
            }

            Snapshot next;
            if (!Exchange(command, next))
            {
                PublishDisconnected("QuickWarp host disconnected.");
                Disconnect();
                needsInitialState = true;
                continue;
            }

            next.connected = true;
            {
                std::lock_guard<std::mutex> lock(_stateMutex);
                next.revision = ++_revision;
                _snapshot = std::move(next);
            }
        }
    }

    bool PipeClient::Connect()
    {
        if (!_running)
            return false;

        if (!WaitNamedPipeW(PipePath, 250))
            return false;

        HANDLE pipe = CreateFileW(
            PipePath,
            GENERIC_READ | GENERIC_WRITE,
            0,
            nullptr,
            OPEN_EXISTING,
            0,
            nullptr);

        if (pipe == INVALID_HANDLE_VALUE)
            return false;

        _pipe = pipe;
        return true;
    }

    void PipeClient::Disconnect()
    {
        HANDLE pipe = _pipe;
        _pipe = INVALID_HANDLE_VALUE;
        if (pipe != INVALID_HANDLE_VALUE)
            CloseHandle(pipe);
    }

    bool PipeClient::Exchange(const std::string& command, Snapshot& snapshot)
    {
        if (!WriteLine(command))
            return false;

        snapshot.status = "Ready";

        std::string line;
        while (ReadLine(line))
        {
            if (line == "END")
                return true;

            std::vector<std::string> fields = Split(line, '|');
            if (fields.empty())
                continue;

            if (fields[0] == "RESULT" && fields.size() >= 4)
            {
                snapshot.success = fields[1] == "1";
                snapshot.closeMenu = fields[2] == "1";
                snapshot.status = Decode(fields[3]);
            }
            else if (fields[0] == "QUICK")
            {
                if (fields.size() >= 2 && fields[1] == "-")
                {
                    snapshot.hasQuick = false;
                }
                else
                {
                    WarpPoint point;
                    if (ParsePoint(fields, 1, point))
                    {
                        point.index = -1;
                        snapshot.quick = std::move(point);
                        snapshot.hasQuick = true;
                    }
                }
            }
            else if (fields[0] == "POINT" && fields.size() >= 8)
            {
                WarpPoint point;
                point.index = std::atoi(fields[1].c_str());
                if (ParsePoint(fields, 2, point))
                    snapshot.points.push_back(std::move(point));
            }
        }

        return false;
    }

    bool PipeClient::WriteLine(const std::string& line)
    {
        if (_pipe == INVALID_HANDLE_VALUE)
            return false;

        std::string data = line + "\n";
        const char* cursor = data.data();
        DWORD remaining = static_cast<DWORD>(data.size());
        while (remaining > 0)
        {
            DWORD written = 0;
            if (!WriteFile(_pipe, cursor, remaining, &written, nullptr) || written == 0)
                return false;
            cursor += written;
            remaining -= written;
        }
        return true;
    }

    bool PipeClient::ReadLine(std::string& line)
    {
        line.clear();
        if (_pipe == INVALID_HANDLE_VALUE)
            return false;

        char ch = 0;
        while (_running)
        {
            DWORD read = 0;
            if (!ReadFile(_pipe, &ch, 1, &read, nullptr) || read == 0)
                return false;
            if (ch == '\n')
                return true;
            if (ch != '\r')
                line.push_back(ch);
            if (line.size() > 16384)
                return false;
        }
        return false;
    }

    std::vector<std::string> PipeClient::Split(const std::string& line, char separator)
    {
        std::vector<std::string> result;
        std::size_t start = 0;
        while (start <= line.size())
        {
            std::size_t next = line.find(separator, start);
            if (next == std::string::npos)
            {
                result.push_back(line.substr(start));
                break;
            }
            result.push_back(line.substr(start, next - start));
            start = next + 1;
        }
        return result;
    }

    std::string PipeClient::Decode(const std::string& value)
    {
        std::string result;
        result.reserve(value.size());
        for (std::size_t i = 0; i < value.size(); ++i)
        {
            if (value[i] == '%' && i + 2 < value.size())
            {
                char hex[3] = { value[i + 1], value[i + 2], 0 };
                char* end = nullptr;
                long decoded = std::strtol(hex, &end, 16);
                if (end && *end == '\0')
                {
                    result.push_back(static_cast<char>(decoded));
                    i += 2;
                    continue;
                }
            }
            result.push_back(value[i]);
        }
        return result;
    }

    bool PipeClient::ParsePoint(const std::vector<std::string>& fields, std::size_t offset, WarpPoint& point)
    {
        if (fields.size() < offset + 6)
            return false;

        point.name = Decode(fields[offset]);
        point.areaId = std::atoi(fields[offset + 1].c_str());
        point.x = std::strtof(fields[offset + 2].c_str(), nullptr);
        point.y = std::strtof(fields[offset + 3].c_str(), nullptr);
        point.z = std::strtof(fields[offset + 4].c_str(), nullptr);
        point.angle = std::strtof(fields[offset + 5].c_str(), nullptr);
        return true;
    }

    void PipeClient::PublishDisconnected(const std::string& status)
    {
        std::lock_guard<std::mutex> lock(_stateMutex);
        _snapshot.connected = false;
        _snapshot.success = false;
        _snapshot.closeMenu = false;
        _snapshot.status = status;
        _snapshot.revision = ++_revision;
    }
}
