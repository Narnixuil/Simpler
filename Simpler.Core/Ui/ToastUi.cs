using System;
using System.Drawing;
using System.Windows.Forms;

namespace Simpler.Core.Ui;

public static class ToastUi
{
    public static void ShowToast(string message, int durationMs = 3000)
    {
        Context.StaRunner.Run(() =>
        {
            var toast = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                TopMost = true,
                Width = 320,
                Height = 60,
                BackColor = Color.FromArgb(30, 30, 30),
                ShowInTaskbar = false
            };
            var label = new Label
            {
                Text = message,
                ForeColor = Color.White,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            toast.Controls.Add(label);
            var screen = Screen.PrimaryScreen!.WorkingArea;
            toast.Left = screen.Right - toast.Width - 20;
            toast.Top = screen.Bottom - toast.Height - 20;
            toast.Shown += async (_, _) =>
            {
                await System.Threading.Tasks.Task.Delay(durationMs);
                toast.Close();
            };
            Application.Run(toast);
        });
    }
}
