using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace DSR_QuickWarp
{
    internal sealed class QuickWarpHost : Form
    {
        private readonly object _gate = new object();
        private readonly QuickWarpHook _hook;
        private readonly WarpStore _store;
        private readonly PipeBridge _pipe;
        private readonly Timer _injectTimer;
        private int _injectedProcessId = -1;
        private string _lastInjectError;

        internal QuickWarpHost()
        {
            _hook = new QuickWarpHook();
            _store = new WarpStore();
            _pipe = new PipeBridge(HandlePipeCommand);
            _injectTimer = new Timer { Interval = 750 };
            _injectTimer.Tick += OnInjectTick;

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
            Hide();
        }

        private void OnClosed(object sender, FormClosedEventArgs e)
        {
            _injectTimer.Stop();
            _pipe.Stop();
            _hook.Stop();
        }

        private void OnInjectTick(object sender, EventArgs e)
        {
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
                            break;

                        case "SAVE_QUICK":
                            message = SaveQuickPoint();
                            success = message == "Quick point saved";
                            break;

                        case "WARP_QUICK":
                            message = WarpQuickPoint();
                            success = message == "Warped";
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
                                success = message == "Warped";
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
                    X = pos.X,
                    Y = pos.Y,
                    Z = pos.Z,
                    Angle = pos.Angle,
                    SavedAt = DateTime.Now.ToString("s")
                };
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
            if (!_hook.Ready)
                return "Game/player is not ready.";

            try
            {
                QuickWarpPlayer player = _hook.GetPlayer();
                if (!player.Ready)
                    return "Player is not loaded yet.";

                if (player.AreaId != target.AreaId)
                    return "Different area: cross-area warp is not enabled yet.";

                player.Warp(new QuickWarpPlayer.Position(target.X, target.Y, target.Z, target.Angle));
                return "Warped";
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
                AppendPoint(builder, _store.Database.Quick, false, -1);
            }

            for (int i = 0; i < _store.Database.Points.Count; i++)
            {
                builder.Append("POINT|").Append(i).Append('|');
                AppendPoint(builder, _store.Database.Points[i], true, i);
            }

            builder.Append("END\n");
            return builder.ToString();
        }

        private static void AppendPoint(StringBuilder builder, WarpPoint point, bool indexAlreadyWritten, int index)
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
