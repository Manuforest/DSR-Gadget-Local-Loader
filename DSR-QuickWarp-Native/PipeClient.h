#pragma once

#include <Windows.h>
#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <deque>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace quickwarp
{
    struct WarpPoint
    {
        int index = -1;
        std::string name;
        int areaId = 0;
        float x = 0.0f;
        float y = 0.0f;
        float z = 0.0f;
        float angle = 0.0f;
    };

    struct Snapshot
    {
        bool connected = false;
        bool success = true;
        bool closeMenu = false;
        bool hasQuick = false;
        std::string status = "Connecting to QuickWarp host...";
        WarpPoint quick;
        std::vector<WarpPoint> points;
        std::uint64_t revision = 0;
    };

    class PipeClient
    {
    public:
        PipeClient();
        ~PipeClient();

        void Start();
        void Stop();
        void Queue(const std::string& command);
        Snapshot GetSnapshot() const;

    private:
        void Worker();
        bool Connect();
        void Disconnect();
        bool Exchange(const std::string& command, Snapshot& snapshot);
        bool WriteLine(const std::string& line);
        bool ReadLine(std::string& line);
        static std::vector<std::string> Split(const std::string& line, char separator);
        static std::string Decode(const std::string& value);
        static bool ParsePoint(const std::vector<std::string>& fields, std::size_t offset, WarpPoint& point);
        void PublishDisconnected(const std::string& status);

        mutable std::mutex _stateMutex;
        Snapshot _snapshot;
        std::uint64_t _revision = 0;

        std::mutex _queueMutex;
        std::condition_variable _queueCv;
        std::deque<std::string> _commands;

        std::thread _worker;
        std::atomic<bool> _running{ false };
        HANDLE _pipe = INVALID_HANDLE_VALUE;
    };
}
