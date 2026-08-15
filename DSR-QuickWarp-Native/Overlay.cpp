#include "Overlay.h"
#include "PipeClient.h"

#include <Windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <algorithm>
#include <atomic>
#include <cstdint>
#include <string>

#include <MinHook.h>
#include <imgui.h>
#include <backends/imgui_impl_dx11.h>
#include <backends/imgui_impl_win32.h>

namespace quickwarp
{
    namespace
    {
        using PresentFn = HRESULT(__stdcall*)(IDXGISwapChain*, UINT, UINT);
        using ResizeBuffersFn = HRESULT(__stdcall*)(IDXGISwapChain*, UINT, UINT, UINT, DXGI_FORMAT, UINT);

        PresentFn gOriginalPresent = nullptr;
        ResizeBuffersFn gOriginalResizeBuffers = nullptr;
        ID3D11Device* gDevice = nullptr;
        ID3D11DeviceContext* gContext = nullptr;
        ID3D11RenderTargetView* gRenderTarget = nullptr;
        HWND gWindow = nullptr;
        WNDPROC gOriginalWndProc = nullptr;
        std::atomic<bool> gInitialized{ false };
        std::atomic<bool> gHooksInstalled{ false };
        bool gVisible = false;
        int gSelected = 0;
        PipeClient gPipe;
        std::uint64_t gLastRevision = 0;
        std::string gStatus = "Connecting to QuickWarp host...";
        bool gStatusSuccess = true;
        ULONGLONG gToastUntil = 0;

        void NativeLog(const char* text)
        {
            OutputDebugStringA("[DSR QuickWarp] ");
            OutputDebugStringA(text);
            OutputDebugStringA("\n");
        }

        void ReleaseRenderTarget()
        {
            if (gRenderTarget)
            {
                gRenderTarget->Release();
                gRenderTarget = nullptr;
            }
        }

        void CreateRenderTarget(IDXGISwapChain* swapChain)
        {
            if (!gDevice || gRenderTarget)
                return;

            ID3D11Texture2D* backBuffer = nullptr;
            if (SUCCEEDED(swapChain->GetBuffer(0, IID_PPV_ARGS(&backBuffer))) && backBuffer)
            {
                gDevice->CreateRenderTargetView(backBuffer, nullptr, &gRenderTarget);
                backBuffer->Release();
            }
        }

        void UpdateSnapshotState()
        {
            Snapshot snapshot = gPipe.GetSnapshot();
            if (snapshot.revision == gLastRevision)
                return;

            gLastRevision = snapshot.revision;
            if (snapshot.status != "Ready" || !snapshot.connected)
            {
                gStatus = snapshot.status;
                gStatusSuccess = snapshot.success;
                gToastUntil = GetTickCount64() + 2200;
            }

            if (snapshot.closeMenu && snapshot.success)
                gVisible = false;

            if (snapshot.points.empty())
                gSelected = 0;
            else
                gSelected = std::clamp(gSelected, 0, static_cast<int>(snapshot.points.size()) - 1);
        }

        void DrawToast()
        {
            if (gVisible || gStatus.empty() || GetTickCount64() >= gToastUntil)
                return;

            ImGuiViewport* viewport = ImGui::GetMainViewport();
            ImVec2 pos(viewport->WorkPos.x + viewport->WorkSize.x * 0.5f, viewport->WorkPos.y + 38.0f);
            ImGui::SetNextWindowPos(pos, ImGuiCond_Always, ImVec2(0.5f, 0.0f));
            ImGui::SetNextWindowBgAlpha(0.88f);
            ImGuiWindowFlags flags = ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_AlwaysAutoResize |
                ImGuiWindowFlags_NoSavedSettings | ImGuiWindowFlags_NoFocusOnAppearing | ImGuiWindowFlags_NoNav;
            ImGui::Begin("##QuickWarpToast", nullptr, flags);
            if (gStatusSuccess)
                ImGui::TextUnformatted(gStatus.c_str());
            else
                ImGui::TextColored(ImVec4(1.0f, 0.48f, 0.42f, 1.0f), "%s", gStatus.c_str());
            ImGui::End();
        }

        void DrawMenu()
        {
            if (!gVisible)
                return;

            Snapshot snapshot = gPipe.GetSnapshot();
            ImGuiViewport* viewport = ImGui::GetMainViewport();
            ImVec2 position(
                viewport->WorkPos.x + viewport->WorkSize.x - 438.0f,
                viewport->WorkPos.y + 70.0f);
            ImGui::SetNextWindowPos(position, ImGuiCond_Always);
            ImGui::SetNextWindowSize(ImVec2(410.0f, 360.0f), ImGuiCond_Always);

            ImGuiWindowFlags flags = ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoCollapse |
                ImGuiWindowFlags_NoSavedSettings;
            if (!ImGui::Begin("QUICK WARP", &gVisible, flags))
            {
                ImGui::End();
                return;
            }

            if (snapshot.connected)
                ImGui::TextDisabled("Host connected");
            else
                ImGui::TextColored(ImVec4(1.0f, 0.48f, 0.42f, 1.0f), "Host disconnected");

            if (snapshot.hasQuick)
            {
                ImGui::Text("Quick: Area %d  (%.1f, %.1f, %.1f)",
                    snapshot.quick.areaId, snapshot.quick.x, snapshot.quick.y, snapshot.quick.z);
            }
            else
            {
                ImGui::TextDisabled("Quick slot: empty");
            }
            ImGui::TextDisabled("F6 save quick   F7 warp quick");
            ImGui::Separator();

            ImGui::BeginChild("##SavedPoints", ImVec2(0.0f, 205.0f), true);
            if (snapshot.points.empty())
            {
                ImGui::TextDisabled("No saved points. Press Insert to save here.");
            }
            else
            {
                for (std::size_t i = 0; i < snapshot.points.size(); ++i)
                {
                    const WarpPoint& point = snapshot.points[i];
                    std::string label = std::to_string(i + 1) + ".  " + point.name +
                        "  [Area " + std::to_string(point.areaId) + "]##point" + std::to_string(i);
                    bool selected = static_cast<int>(i) == gSelected;
                    if (ImGui::Selectable(label.c_str(), selected))
                        gSelected = static_cast<int>(i);
                    if (selected)
                        ImGui::SetItemDefaultFocus();
                }
            }
            ImGui::EndChild();

            ImGui::TextDisabled("Up/Down select   Enter warp   Insert save   Delete remove   Esc close");
            ImGui::TextDisabled("Cross-map: game map load + precise saved position");
            if (!gStatus.empty() && gStatus != "Ready")
            {
                if (gStatusSuccess)
                    ImGui::Text("%s", gStatus.c_str());
                else
                    ImGui::TextColored(ImVec4(1.0f, 0.48f, 0.42f, 1.0f), "%s", gStatus.c_str());
            }

            ImGui::End();
        }

        LRESULT CALLBACK HookWndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
        {
            if (gInitialized)
                ImGui_ImplWin32_WndProcHandler(hwnd, message, wParam, lParam);

            if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
            {
                bool repeated = (lParam & (1LL << 30)) != 0;
                int key = static_cast<int>(wParam);

                if (key == VK_F8 && !repeated)
                {
                    gVisible = !gVisible;
                    if (gVisible)
                        gPipe.Queue("STATE");
                    return 0;
                }
                if (key == VK_F6 && !repeated)
                {
                    gPipe.Queue("SAVE_QUICK");
                    return 0;
                }
                if (key == VK_F7 && !repeated)
                {
                    gPipe.Queue("WARP_QUICK");
                    return 0;
                }

                if (gVisible)
                {
                    Snapshot snapshot = gPipe.GetSnapshot();
                    if (key == VK_ESCAPE && !repeated)
                    {
                        gVisible = false;
                        return 0;
                    }
                    if (key == VK_INSERT && !repeated)
                    {
                        gPipe.Queue("SAVE");
                        return 0;
                    }
                    if (key == VK_DELETE && !repeated)
                    {
                        if (!snapshot.points.empty())
                            gPipe.Queue("DELETE|" + std::to_string(gSelected));
                        return 0;
                    }
                    if (key == VK_RETURN && !repeated)
                    {
                        if (!snapshot.points.empty())
                            gPipe.Queue("WARP|" + std::to_string(gSelected));
                        return 0;
                    }
                    if (key == VK_UP)
                    {
                        if (!snapshot.points.empty())
                            gSelected = std::max(0, gSelected - 1);
                        return 0;
                    }
                    if (key == VK_DOWN)
                    {
                        if (!snapshot.points.empty())
                            gSelected = std::min(static_cast<int>(snapshot.points.size()) - 1, gSelected + 1);
                        return 0;
                    }
                    return 0;
                }
            }

            if (gVisible && gInitialized)
            {
                ImGuiIO& io = ImGui::GetIO();
                bool mouseMessage = message >= WM_MOUSEFIRST && message <= WM_MOUSELAST;
                bool keyboardMessage = message >= WM_KEYFIRST && message <= WM_KEYLAST;
                if ((mouseMessage && io.WantCaptureMouse) || (keyboardMessage && io.WantCaptureKeyboard))
                    return 0;
            }

            return CallWindowProcW(gOriginalWndProc, hwnd, message, wParam, lParam);
        }

        bool SetupImGui(IDXGISwapChain* swapChain)
        {
            if (gInitialized)
                return true;

            if (FAILED(swapChain->GetDevice(IID_PPV_ARGS(&gDevice))) || !gDevice)
                return false;
            gDevice->GetImmediateContext(&gContext);

            DXGI_SWAP_CHAIN_DESC desc{};
            if (FAILED(swapChain->GetDesc(&desc)))
                return false;
            gWindow = desc.OutputWindow;

            IMGUI_CHECKVERSION();
            ImGui::CreateContext();
            ImGuiIO& io = ImGui::GetIO();
            io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;
            io.IniFilename = nullptr;
            ImGui::StyleColorsDark();
            ImGuiStyle& style = ImGui::GetStyle();
            style.WindowRounding = 5.0f;
            style.FrameRounding = 3.0f;
            style.WindowPadding = ImVec2(14.0f, 12.0f);

            if (!ImGui_ImplWin32_Init(gWindow) || !ImGui_ImplDX11_Init(gDevice, gContext))
                return false;

            CreateRenderTarget(swapChain);
            gOriginalWndProc = reinterpret_cast<WNDPROC>(
                SetWindowLongPtrW(gWindow, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(HookWndProc)));
            if (!gOriginalWndProc)
                return false;

            gInitialized = true;
            NativeLog("ImGui initialized.");
            return true;
        }

        HRESULT __stdcall HookPresent(IDXGISwapChain* swapChain, UINT syncInterval, UINT flags)
        {
            if (!gInitialized)
                SetupImGui(swapChain);

            if (gInitialized)
            {
                if (!gRenderTarget)
                    CreateRenderTarget(swapChain);

                UpdateSnapshotState();
                ImGui_ImplDX11_NewFrame();
                ImGui_ImplWin32_NewFrame();
                ImGui::NewFrame();
                ImGui::GetIO().MouseDrawCursor = gVisible;

                DrawMenu();
                DrawToast();

                ImGui::Render();
                if (gRenderTarget)
                    gContext->OMSetRenderTargets(1, &gRenderTarget, nullptr);
                ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
            }

            return gOriginalPresent(swapChain, syncInterval, flags);
        }

        HRESULT __stdcall HookResizeBuffers(
            IDXGISwapChain* swapChain,
            UINT bufferCount,
            UINT width,
            UINT height,
            DXGI_FORMAT newFormat,
            UINT swapChainFlags)
        {
            ReleaseRenderTarget();
            HRESULT result = gOriginalResizeBuffers(
                swapChain, bufferCount, width, height, newFormat, swapChainFlags);
            if (SUCCEEDED(result) && gInitialized)
                CreateRenderTarget(swapChain);
            return result;
        }

        bool CreateDummySwapChain(IDXGISwapChain** swapChain)
        {
            const wchar_t* className = L"DSRQuickWarpDummyWindow";
            WNDCLASSEXW wc{};
            wc.cbSize = sizeof(wc);
            wc.lpfnWndProc = DefWindowProcW;
            wc.hInstance = GetModuleHandleW(nullptr);
            wc.lpszClassName = className;
            RegisterClassExW(&wc);

            HWND window = CreateWindowExW(
                0, className, L"", WS_OVERLAPPEDWINDOW,
                0, 0, 100, 100, nullptr, nullptr, wc.hInstance, nullptr);
            if (!window)
                return false;

            DXGI_SWAP_CHAIN_DESC desc{};
            desc.BufferCount = 1;
            desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
            desc.OutputWindow = window;
            desc.SampleDesc.Count = 1;
            desc.Windowed = TRUE;
            desc.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

            ID3D11Device* device = nullptr;
            ID3D11DeviceContext* context = nullptr;
            D3D_FEATURE_LEVEL featureLevel{};
            HRESULT result = D3D11CreateDeviceAndSwapChain(
                nullptr,
                D3D_DRIVER_TYPE_HARDWARE,
                nullptr,
                0,
                nullptr,
                0,
                D3D11_SDK_VERSION,
                &desc,
                swapChain,
                &device,
                &featureLevel,
                &context);

            if (context)
                context->Release();
            if (device)
                device->Release();
            DestroyWindow(window);
            UnregisterClassW(className, wc.hInstance);
            return SUCCEEDED(result) && *swapChain;
        }

        bool InstallHooks()
        {
            IDXGISwapChain* dummySwapChain = nullptr;
            if (!CreateDummySwapChain(&dummySwapChain))
            {
                NativeLog("Could not create dummy swap chain.");
                return false;
            }

            void** vtable = *reinterpret_cast<void***>(dummySwapChain);
            void* presentTarget = vtable[8];
            void* resizeTarget = vtable[13];

            MH_STATUS initStatus = MH_Initialize();
            if (initStatus != MH_OK && initStatus != MH_ERROR_ALREADY_INITIALIZED)
            {
                dummySwapChain->Release();
                NativeLog("MH_Initialize failed.");
                return false;
            }

            bool ok = MH_CreateHook(presentTarget, &HookPresent, reinterpret_cast<void**>(&gOriginalPresent)) == MH_OK
                && MH_CreateHook(resizeTarget, &HookResizeBuffers, reinterpret_cast<void**>(&gOriginalResizeBuffers)) == MH_OK
                && MH_EnableHook(MH_ALL_HOOKS) == MH_OK;

            dummySwapChain->Release();
            if (!ok)
            {
                NativeLog("MinHook setup failed.");
                return false;
            }

            gHooksInstalled = true;
            NativeLog("DX11 hooks installed.");
            return true;
        }
    }

    void StartOverlay()
    {
        gPipe.Start();
        InstallHooks();
    }
}
