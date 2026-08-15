using System;
using System.Drawing;
using System.Windows.Forms;

namespace DSR_QuickWarp
{
    internal sealed class ToastForm : Form
    {
        private readonly Timer _timer;

        internal ToastForm(string text, bool isError)
        {
            Width = 300;
            Height = 54;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = isError ? Color.FromArgb(55, 24, 24) : Color.FromArgb(24, 24, 24);
            ForeColor = Color.White;
            Opacity = 0.92;

            Label label = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = text,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(label);

            _timer = new Timer { Interval = 1400 };
            _timer.Tick += delegate
            {
                _timer.Stop();
                Close();
            };
            Shown += delegate { _timer.Start(); };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        internal void PositionFor(QuickWarpHook hook)
        {
            if (hook.Hooked && hook.Process != null && NativeMethods.GetWindowRect(hook.Process.MainWindowHandle, out NativeMethods.RECT rect))
            {
                Left = rect.Left + ((rect.Right - rect.Left) - Width) / 2;
                Top = rect.Top + 45;
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
            }
        }
    }
}
