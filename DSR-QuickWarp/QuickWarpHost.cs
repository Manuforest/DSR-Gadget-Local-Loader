using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using PropertyHook;

namespace DSR_QuickWarp
{
    internal sealed class QuickWarpHost : Form
    {
        private sealed class PendingWarp
        {
            internal WarpPoint Target;
            internal int MapGroup;
            internal DateTime StartedUtc;
            internal DateTime? MapReadySinceUtc;
        }

        private readonly object _gate = new object();
        private readonly QuickWarpHook _hook;
        private readonly WarpStore _store;
        private readonly PipeBridge _pipe;
        private readonly Timer _injectTimer;
        private readonly Timer _warpTimer;
        private int _injectedProcessId = -1;
        private string _lastInjectError;
        private bool _gameWasHooked;
        private PendingWarp _pendingWarp;
        private string _backgroundStatus = "Ready";
        private bool _backgroundSuccess = true;
        private bool _backgroundCloseMenu;
        private DateTime _backgroundStatusUntilUtc = DateTime.MinValue;

        internal QuickWarpHost()
        {
            _hook = new QuickWarpHook();
            _store = new WarpStore();
            _pipe = new PipeBridge(HandlePipeCommand);

            _injectTimer = new Timer { Interval = 750 };
            _injectTimer.Tick += OnInjectTick;

            _warpTimer = new Timer { Interval = 100 };
            _warpTimer.Tick += OnWarpTick;

            _hook.OnHooked += OnGameHooked;
            _hook.OnUnhooked += OnGameUnhooked;

            Text = "DSR QuickWarp Host";
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            Left = -32000;
            Top = -32000;
            Width = 1;
            Height = 1;
            Opacity = 0;

            Load += OnLoaded;
            FormClosed += OnClosed;
        }

        private void OnLoaded(object sender, EventArgs e)
        {
            _pipe.Start();
            _hook.Start();
            _injectTimer.Start();
            _warpTimer.Start();
            Hide();
        }

        private void OnClosed(object sender, FormClosedEventArgs e)
        {
            _injectTimer.Stop();
            _warpTimer.Stop();
            _hook.OnHooked -= OnGameHooked;
            _hook.OnUnhooked -= OnGameUnhooked;
            _pipe.Stop();
            _hook.Stop();
        }

        private void OnGameHooked(object sender, PHEventArgs e)
        {
            _gameWasHooked = true;
        }

        private void OnGameUnhooked(object sender, PHEventArgs e)
        {
            if (!_gameWasHooked || IsDisposed || Disposing)
                return;

            try
            {
                if (IsHandleCreated)
                    BeginInvoke((Action)Close);
            }
            catch (InvalidOperationException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void OnInjectTick(object sender, EventArgs e)
        {
            // Fallback in addition to OnUnhooked: once QuickWarp has attached to a
            // game process, it should never linger after that game process is gone.
            if (_gameWasHooked && !_hook.Hooked)
            {
                Close();
                return;
            }

            if (!_hook.Hooked || _hook.Process == null || _hook.Process.HasExited)
            {
                _injectedProcessId = -1;
                return;
            }

            int processId = _hook.Process.Id;
            if (_injectedProcessId == processId)
                return;

            string overlayPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DSR QuickWarp Overlay.dll");
            if (!File.Exists(overlayPath))
            {
                LogOnce("Overlay DLL not found: " + overlayPath);
                return;
            }

            string error;
            if (OverlayInjector.Inject(_hook.Process, overlayPath, out error))
            {
                _injectedProcessId = processId;
                _lastInjectError = null;
                Log("Injected overlay into DarkSoulsRemastered.exe (PID " + processId + ").");
            }
            else
            {
                LogOnce("Overlay injection failed: " + error);
            }
        }

        private void OnWarpTick(object sender, EventArgs e)
        {
            lock (_gate)
            {
                if (_pendingWarp == null)
                    return;

                DateTime now = DateTime.UtcNow;
                if ((now - _pendingWarp.StartedUtc).TotalSeconds > 20)
                {
                    _pendingWarp = null;
                    SetBackgroundStatus("Cross-map warp timed out.", false, false, 4);
                    return;
                }

                if (!_hook.Ready)
                    return;

                try
                {
                    QuickWarpPlayer player = _hook.GetPlayer();
                    if (!player.Ready)
                        return;

                    if (player.MapGroup != _pendingWarp.MapGroup)
                    {
                        _pendingWarp.MapReadySinceUtc = null;
                        return;
                    }

                    if (!_pendingWarp.MapReadySinceUtc.HasValue)
                    {
                        _pendingWarp.MapReadySinceUtc = now;
                        return;
                    }

                    // Give the target map a few frames after PlayerIns reappears before
                    // writing the final coordinates. This prevents landing during load setup.
                    if ((now - _pendingWarp.MapReadySinceUtc.Value).TotalMilliseconds < 450)
                        return;

                    WarpPoint target = _pendingWarp.Target;
                    player.Warp(new QuickWarpPlayer.Position(target.X, target.Y, target.Z, target.Angle));
                    _pendingWarp = null;
                    SetBackgroundStatus("Warped", true, true, 3);
                }
                catch (Exception ex)
                {
                    Log("Pending cross-map warp check failed: " + ex.Message);
                }
            }
        }

        private string HandlePipeCommand(string command)
        {
            lock (_gate)
            {
                bool success = true;
                bool closeMenu = false;
                string message = "Ready";

                try
                {
                    string[] parts = (command ?? string.Empty).Trim().Split('|');
                    string op = parts.Length == 0 ? string.Empty : parts[0].ToUpperInvariant();

                    switch (op)
                    {
                        case "STATE":
                        case "PING":
                            GetBackgroundStatus(out success, out closeMenu, out message);
                            break;

                        case "SAVE_QUICK":
                            message = SaveQuickPoint();
                            success = message == "Quick point saved";
                            break;

                        case "WARP_QUICK":
                            message = WarpQuickPoint();
                            success = IsWarpAccepted(message);
                            closeMenu = success;
                            break;

                        case "SAVE":
                            message = SavePermanentPoint();
                            success = message.EndsWith(" saved", StringComparison.Ordinal);
                            break;

                        case "WARP":
                        {
                            int index;
                            if (parts.Length < 2 || !int.TryParse(parts[1], out index))
                            {
                                success = false;
                                message = "Invalid saved-point index.";
                            }
                            else
                            {
                                message = WarpPermanentPoint(index);
                                success = IsWarpAccepted(message);
                                closeMenu = success;
                            }
                            break;
                        }

                        case "DELETE":
                        {
                            int index;
                            if (parts.Length < 2 || !int.TryParse(parts[1], out index))
                            {
                                success = false;
                                message = "Invalid saved-point index.";
                            }
                            else
                            {
                                message = DeletePermanentPoint(index);
                                success = message.EndsWith(" deleted", StringComparison.Ordinal);
                            }
                            break;
                        }

                        default:
                            success = false;
                            message = "Unknown command: " + op;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    success = false;
                    message = "Host error: " + ex.Message;
                }

                return BuildSnapshot(success, closeMenu, message);
            }
        }

        private static bool IsWarpAccepted(string message)
        {
            return message == "Warped" || message == "Loading target map...";
        }

        private bool TryGetCurrentPoint(string name, out WarpPoint point, out string error)
        {
            point = null;
            error = null;

            if (!_hook.Ready)
            {
                error = "Game/player is not ready.";
                return false;
            }

            try
            {
                QuickWarpPlayer player = _hook.GetPlayer();
                if (!player.Ready)
                {
                    error = "Player is not loaded yet.";
                    return false;
                }

                QuickWarpPlayer.Position pos = player.GetPosition();
                point = new WarpPoint
                {
                    Name = name,
                    AreaId = player.AreaId,
                    MapGroup = player.MapGroup,
                    X = pos.X,
                    Y = pos.Y,
                    Z = pos.Z,
                    Angle = pos.Angle,
                    SavedAt = DateTime.Now.ToString("s")
                };

                if (point.MapGroup <= 0)
                {
                    error = "This location has no supported map group.";
                    point = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "Save failed: " + ex.Message;
                return false;
            }
        }

        private string SaveQuickPoint()
        {
            WarpPoint point;
            string error;
            if (!TryGetCurrentPoint("Quick", out point, out error))
                return error;

            _store.Database.Quick = point;
            _store.Save();
            return "Quick point saved";
        }

        private string SavePermanentPoint()
        {
            string name = "Point " + (_store.Database.Points.Count + 1).ToString("00");
            WarpPoint point;
            string error;
            if (!TryGetCurrentPoint(name, out point, out error))
                return error;

            _store.Database.Points.Add(point);
            _store.Save();
            return name + " saved";
        }

        private string WarpQuickPoint()
        {
            if (_store.Database.Quick == null)
                return "No quick point saved.";
            return WarpTo(_store.Database.Quick);
        }

        private string WarpPermanentPoint(int index)
        {
            if (index < 0 || index >= _store.Database.Points.Count)
                return "Select a saved point.";
            return WarpTo(_store.Database.Points[index]);
        }

        private string WarpTo(WarpPoint target)
        {
            if (_pendingWarp != null)
                return "Another cross-map warp is already in progress.";

            if (!_hook.Ready)
                return "Game/player is not ready.";

            try
            {
                QuickWarpPlayer player = _hook.GetPlayer();
                if (!player.Ready)
                    return "Player is not loaded yet.";

                int targetGroup = WarpMap.GetGroup(target);
                if (targetGroup <= 0)
                    return "Saved point has no supported map group.";

                // AreaIDs can differ inside one already-loaded map. In that case the
                // stable v0.1 coordinate warp is enough; no loading transition is needed.
                if (player.MapGroup == targetGroup)
                {
                    player.Warp(new QuickWarpPlayer.Position(target.X, target.Y, target.Z, target.Angle));
                    SetBackgroundStatus("Warped", true, true, 2);
                    return "Warped";
                }

                int anchorBonfire;
                if (!WarpMap.TryGetAnchorBonfire(targetGroup, out anchorBonfire))
                    return "No safe loading anchor is known for this map.";

                string error;
                if (!_hook.TryBonfireWarp(anchorBonfire, out error))
                    return error;

                _pendingWarp = new PendingWarp
                {
                    Target = target,
                    MapGroup = targetGroup,
                    StartedUtc = DateTime.UtcNow,
                    MapReadySinceUtc = null
                };
                SetBackgroundStatus("Loading target map...", true, true, 25);
                return "Loading target map...";
            }
            catch (Exception ex)
            {
                return "Warp failed: " + ex.Message;
            }
        }

        private string DeletePermanentPoint(int index)
        {
            if (index < 0 || index >= _store.Database.Points.Count)
                return "Select a saved point.";

            string name = _store.Database.Points[index].Name;
            _store.Database.Points.RemoveAt(index);
            _store.Save();
            return name + " deleted";
        }

        private void SetBackgroundStatus(string message, bool success, bool closeMenu, int seconds)
        {
            _backgroundStatus = message;
            _backgroundSuccess = success;
            _backgroundCloseMenu = closeMenu;
            _backgroundStatusUntilUtc = DateTime.UtcNow.AddSeconds(seconds);
        }

        private void GetBackgroundStatus(out bool success, out bool closeMenu, out string message)
        {
            if (_backgroundStatusUntilUtc > DateTime.UtcNow)
            {
                success = _backgroundSuccess;
                closeMenu = _backgroundCloseMenu;
                message = _backgroundStatus;
            }
            else
            {
                success = true;
                closeMenu = false;
                message = "Ready";
            }
        }

        private string BuildSnapshot(bool success, bool closeMenu, string message)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("RESULT|")
                .Append(success ? "1" : "0").Append('|')
                .Append(closeMenu ? "1" : "0").Append('|')
                .Append(Encode(message)).Append('\n');

            if (_store.Database.Quick == null)
            {
                builder.Append("QUICK|-\n");
            }
            else
            {
                builder.Append("QUICK|");
                AppendPoint(builder, _store.Database.Quick);
            }

            for (int i = 0; i < _store.Database.Points.Count; i++)
            {
                builder.Append("POINT|").Append(i).Append('|');
                AppendPoint(builder, _store.Database.Points[i]);
            }

            builder.Append("END\n");
            return builder.ToString();
        }

        private static void AppendPoint(StringBuilder builder, WarpPoint point)
        {
            builder.Append(Encode(point.Name)).Append('|')
                .Append(point.AreaId.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(point.X.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(point.Y.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(point.Z.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(point.Angle.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        private static string Encode(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private void LogOnce(string text)
        {
            if (string.Equals(_lastInjectError, text, StringComparison.Ordinal))
                return;
            _lastInjectError = text;
            Log(text);
        }

        private static void Log(string text)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "quickwarp.log");
                File.AppendAllText(path, DateTime.Now.ToString("s") + "  " + text + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
