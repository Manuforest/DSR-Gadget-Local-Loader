using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DSR_QuickWarp
{
    internal sealed class OverlayForm : Form
    {
        private readonly QuickWarpHost _host;
        private readonly ListBox _points;
        private readonly Label _quick;
        private readonly Label _status;

        internal OverlayForm(QuickWarpHost host)
        {
            _host = host;

            Width = 390;
            Height = 350;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            BackColor = Color.FromArgb(20, 20, 20);
            ForeColor = Color.Gainsboro;
            Opacity = 0.95;
            Padding = new Padding(16);

            Label title = new Label
            {
                Text = "QUICK WARP",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(18, 16)
            };

            _quick = new Label
            {
                AutoSize = false,
                Width = 350,
                Height = 24,
                Location = new Point(18, 48),
                ForeColor = Color.Silver,
                Font = new Font("Segoe UI", 9f)
            };

            _points = new ListBox
            {
                Location = new Point(18, 78),
                Width = 354,
                Height = 190,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Segoe UI", 10f),
                IntegralHeight = false
            };

            Label help = new Label
            {
                Text = "Enter warp   Insert save here   Delete remove   Esc close",
                AutoSize = false,
                Width = 354,
                Height = 22,
                Location = new Point(18, 280),
                ForeColor = Color.DarkGray,
                Font = new Font("Segoe UI", 8.5f)
            };

            _status = new Label
            {
                AutoSize = false,
                Width = 354,
                Height = 28,
                Location = new Point(18, 308),
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI", 8.5f)
            };

            Controls.Add(title);
            Controls.Add(_quick);
            Controls.Add(_points);
            Controls.Add(help);
            Controls.Add(_status);

            Shown += delegate
            {
                if (_points.Items.Count > 0 && _points.SelectedIndex < 0)
                    _points.SelectedIndex = 0;
                _points.Focus();
            };
            Deactivate += delegate { _host.HideOverlay(); };
            KeyDown += OnKeyDown;
            _points.KeyDown += OnKeyDown;
        }

        internal void RefreshPoints()
        {
            int oldIndex = _points.SelectedIndex;
            _points.Items.Clear();

            IList<WarpPoint> points = _host.GetPermanentPoints();
            for (int i = 0; i < points.Count; i++)
                _points.Items.Add((i + 1).ToString("00") + "  " + points[i]);

            if (_points.Items.Count > 0)
                _points.SelectedIndex = Math.Max(0, Math.Min(oldIndex, _points.Items.Count - 1));

            WarpPoint quick = _host.GetQuickPoint();
            _quick.Text = quick == null
                ? "F6 quick save / F7 quick warp: empty"
                : "F6/F7 quick slot: " + quick.AreaId + "  (" + quick.X.ToString("0.0") + ", " + quick.Y.ToString("0.0") + ", " + quick.Z.ToString("0.0") + ")";
        }

        internal void Reposition()
        {
            try
            {
                if (NativeMethods.GetWindowRect(GetGameWindow(), out NativeMethods.RECT rect))
                {
                    Left = Math.Max(rect.Left + 20, rect.Right - Width - 28);
                    Top = rect.Top + 72;
                    return;
                }
            }
            catch
            {
            }

            StartPosition = FormStartPosition.CenterScreen;
        }

        private IntPtr GetGameWindow()
        {
            System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName("DarkSoulsRemastered");
            try
            {
                if (processes.Length > 0)
                    return processes[0].MainWindowHandle;
                return IntPtr.Zero;
            }
            finally
            {
                foreach (System.Diagnostics.Process process in processes)
                    process.Dispose();
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                _host.HideOverlay();
                return;
            }

            if (e.KeyCode == Keys.Insert)
            {
                e.SuppressKeyPress = true;
                SetStatus(_host.SavePermanentPoint());
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                e.SuppressKeyPress = true;
                SetStatus(_host.DeletePermanentPoint(_points.SelectedIndex));
                return;
            }

            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                string result = _host.WarpPermanentPoint(_points.SelectedIndex);
                SetStatus(result);
                if (result == "Warped")
                    _host.HideOverlay();
            }
        }

        private void SetStatus(string text)
        {
            _status.Text = text;
        }
    }
}
