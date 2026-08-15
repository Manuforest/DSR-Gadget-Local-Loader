using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DSR_QuickWarp
{
    internal sealed class QuickWarpHost : Form
    {
        private const int HotkeyQuickSave = 1;
        private const int HotkeyQuickWarp = 2;
        private const int HotkeyMenu = 3;
        private const int HotkeyExit = 4;

        private readonly QuickWarpHook _hook;
        private readonly WarpStore _store;
        private readonly OverlayForm _overlay;

        internal QuickWarpHost()
        {
            _hook = new QuickWarpHook();
            _store = new WarpStore();
            _overlay = new OverlayForm(this);

            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
            Opacity = 0;

            Load += OnLoaded;
            FormClosed += OnClosed;
        }

        private void OnLoaded(object sender, EventArgs e)
        {
            RegisterHotkeys();
            _hook.Start();
            Hide();
        }

        private void OnClosed(object sender, FormClosedEventArgs e)
        {
            _hook.Stop();
            NativeMethods.UnregisterHotKey(Handle, HotkeyQuickSave);
            NativeMethods.UnregisterHotKey(Handle, HotkeyQuickWarp);
            NativeMethods.UnregisterHotKey(Handle, HotkeyMenu);
            NativeMethods.UnregisterHotKey(Handle, HotkeyExit);
        }

        private void RegisterHotkeys()
        {
            NativeMethods.RegisterHotKey(Handle, HotkeyQuickSave, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F6);
            NativeMethods.RegisterHotKey(Handle, HotkeyQuickWarp, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F7);
            NativeMethods.RegisterHotKey(Handle, HotkeyMenu, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F8);
            NativeMethods.RegisterHotKey(Handle, HotkeyExit, NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F8);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();

                if (id == HotkeyExit)
                {
                    Close();
                    return;
                }

                if (id == HotkeyMenu)
                {
                    if (_overlay.Visible)
                        HideOverlay();
                    else if (IsGameForeground())
                        ShowOverlay();
                    return;
                }

                if (!IsGameForeground())
                {
                    base.WndProc(ref m);
                    return;
                }

                if (id == HotkeyQuickSave)
                    SaveQuickPoint();
                else if (id == HotkeyQuickWarp)
                    WarpQuickPoint();
            }

            base.WndProc(ref m);
        }

        private bool IsGameForeground()
        {
            if (_overlay.Visible)
                return true;
            if (!_hook.Hooked || _hook.Process == null)
                return false;

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            uint processId;
            NativeMethods.GetWindowThreadProcessId(foreground, out processId);
            return processId == (uint)_hook.Process.Id;
        }

        private bool TryGetCurrentPoint(string name, out WarpPoint point, out string error)
        {
            point = null;
            error = null;

            if (!_hook.Ready)
            {
                error = "Waiting for Dark Souls Remastered...";
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

        internal void SaveQuickPoint()
        {
            WarpPoint point;
            string error;
            if (!TryGetCurrentPoint("Quick", out point, out error))
            {
                ShowToast(error, true);
                return;
            }

            _store.Database.Quick = point;
            _store.Save();
            ShowToast("Quick point saved", false);
            _overlay.RefreshPoints();
        }

        internal string SavePermanentPoint()
        {
            string name = "Point " + (_store.Database.Points.Count + 1).ToString("00");
            WarpPoint point;
            string error;
            if (!TryGetCurrentPoint(name, out point, out error))
                return error;

            _store.Database.Points.Add(point);
            _store.Save();
            _overlay.RefreshPoints();
            return name + " saved";
        }

        private void WarpQuickPoint()
        {
            if (_store.Database.Quick == null)
            {
                ShowToast("No quick point saved", true);
                return;
            }

            string result = WarpTo(_store.Database.Quick);
            ShowToast(result, result != "Warped");
        }

        internal string WarpPermanentPoint(int index)
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
                    return "Different area: v0.1 blocks cross-area warp.";

                player.Warp(new QuickWarpPlayer.Position(target.X, target.Y, target.Z, target.Angle));
                return "Warped";
            }
            catch (Exception ex)
            {
                return "Warp failed: " + ex.Message;
            }
        }

        internal string DeletePermanentPoint(int index)
        {
            if (index < 0 || index >= _store.Database.Points.Count)
                return "Select a saved point.";

            string name = _store.Database.Points[index].Name;
            _store.Database.Points.RemoveAt(index);
            _store.Save();
            _overlay.RefreshPoints();
            return name + " deleted";
        }

        internal IList<WarpPoint> GetPermanentPoints()
        {
            return _store.Database.Points.AsReadOnly();
        }

        internal WarpPoint GetQuickPoint()
        {
            return _store.Database.Quick;
        }

        internal void ShowOverlay()
        {
            _overlay.RefreshPoints();
            _overlay.Reposition();
            _overlay.Show();
            _overlay.Activate();
        }

        internal void HideOverlay()
        {
            _overlay.Hide();
            RestoreGameFocus();
        }

        internal void RestoreGameFocus()
        {
            if (_hook.Hooked && _hook.Process != null && _hook.Process.MainWindowHandle != IntPtr.Zero)
                NativeMethods.SetForegroundWindow(_hook.Process.MainWindowHandle);
        }

        private void ShowToast(string text, bool isError)
        {
            ToastForm toast = new ToastForm(text, isError);
            toast.PositionFor(_hook);
            toast.Show();
        }
    }
}
