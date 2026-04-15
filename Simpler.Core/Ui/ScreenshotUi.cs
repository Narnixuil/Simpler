using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Simpler.Core.Ui;

public static class ScreenshotUi
{
    public static Rectangle? SelectScreenRegion()
    {
        using var f = new ScreenRegionSelector();
        return f.ShowDialog() == DialogResult.OK ? f.Selected : (Rectangle?)null;
    }

    public static Rectangle? SelectImageRegion(Bitmap source)
    {
        using var f = new ImageRegionSelector(source);
        return f.ShowDialog() == DialogResult.OK ? f.Selected : (Rectangle?)null;
    }

    private static Rectangle NormalizeRect(Point a, Point b)
    {
        int x1 = Math.Min(a.X, b.X);
        int y1 = Math.Min(a.Y, b.Y);
        int x2 = Math.Max(a.X, b.X);
        int y2 = Math.Max(a.Y, b.Y);
        return new Rectangle(x1, y1, x2 - x1, y2 - y1);
    }

    private static Rectangle ClampRect(Rectangle r, Rectangle bounds)
    {
        int x = Math.Max(bounds.X, Math.Min(r.X, bounds.Right - 1));
        int y = Math.Max(bounds.Y, Math.Min(r.Y, bounds.Bottom - 1));
        int right = Math.Min(r.Right, bounds.Right);
        int bottom = Math.Min(r.Bottom, bounds.Bottom);
        int w = Math.Max(0, right - x);
        int h = Math.Max(0, bottom - y);
        return new Rectangle(x, y, w, h);
    }

    private static void ForceForeground(Form form)
    {
        try
        {
            form.TopMost = true;
            form.BringToFront();
            form.Activate();
            BringWindowToTop(form.Handle);
            SetForegroundWindow(form.Handle);
        }
        catch { }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    private sealed class ScreenRegionSelector : Form
    {
        private Point _start;
        private Rectangle _selection;
        private bool _dragging;
        private readonly Bitmap _background;
        private readonly Rectangle _virtualScreen;

        public Rectangle Selected { get; private set; }

        public ScreenRegionSelector()
        {
            FormBorderStyle = FormBorderStyle.None;
            Shown += (_, _) => ForceForeground(this);
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;

            _virtualScreen = SystemInformation.VirtualScreen;
            Bounds = _virtualScreen;

            _background = new Bitmap(_virtualScreen.Width, _virtualScreen.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(_background);
            g.CopyFromScreen(_virtualScreen.Left, _virtualScreen.Top, 0, 0, _virtualScreen.Size, CopyPixelOperation.SourceCopy);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImage(_background, 0, 0);
            using var dimBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
            g.FillRectangle(dimBrush, 0, 0, Width, Height);
            if (_selection.Width > 0 && _selection.Height > 0)
            {
                var state = g.Save();
                g.SetClip(_selection);
                g.DrawImage(_background, 0, 0);
                g.Restore(state);
                using var pen = new Pen(Color.Lime, 2);
                g.DrawRectangle(pen, _selection);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _start = e.Location;
            _selection = new Rectangle(_start, new Size(0, 0));
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging) return;
            _selection = NormalizeRect(_start, e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            _selection = NormalizeRect(_start, e.Location);
            if (_selection.Width > 1 && _selection.Height > 1)
            {
                Selected = new Rectangle(_selection.X + _virtualScreen.Left, _selection.Y + _virtualScreen.Top, _selection.Width, _selection.Height);
                DialogResult = DialogResult.OK;
            }
            else
            {
                DialogResult = DialogResult.Cancel;
            }
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Escape) return;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _background.Dispose();
            base.OnFormClosed(e);
        }
    }

    private sealed class ImageRegionSelector : Form
    {
        private readonly Bitmap _image;
        private float _scale;
        private Rectangle _selection;
        private bool _dragging;
        private Point _start;

        public Rectangle Selected { get; private set; }

        public ImageRegionSelector(Bitmap source)
        {
            FormBorderStyle = FormBorderStyle.None;
            Shown += (_, _) => ForceForeground(this);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            BackColor = Color.Black;

            _image = (Bitmap)source.Clone();
            var wa = Screen.PrimaryScreen!.WorkingArea;
            float sx = (wa.Width - 40f) / _image.Width;
            float sy = (wa.Height - 40f) / _image.Height;
            _scale = Math.Min(1f, Math.Min(sx, sy));
            int w = Math.Max(1, (int)Math.Round(_image.Width * _scale));
            int h = Math.Max(1, (int)Math.Round(_image.Height * _scale));
            ClientSize = new Size(w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(_image, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
            using var overlay = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
            g.FillRectangle(overlay, ClientRectangle);
            if (_selection.Width > 0 && _selection.Height > 0)
            {
                var state = g.Save();
                g.SetClip(_selection);
                g.DrawImage(_image, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
                g.Restore(state);
                using var pen = new Pen(Color.Lime, 2);
                g.DrawRectangle(pen, _selection);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _start = e.Location;
            _selection = new Rectangle(_start, new Size(0, 0));
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging) return;
            _selection = NormalizeRect(_start, e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            _selection = NormalizeRect(_start, e.Location);
            if (_selection.Width > 1 && _selection.Height > 1)
            {
                var mapped = new Rectangle(
                    (int)Math.Round(_selection.X / _scale),
                    (int)Math.Round(_selection.Y / _scale),
                    (int)Math.Round(_selection.Width / _scale),
                    (int)Math.Round(_selection.Height / _scale));
                mapped = ClampRect(mapped, new Rectangle(0, 0, _image.Width, _image.Height));
                if (mapped.Width > 1 && mapped.Height > 1)
                {
                    Selected = mapped;
                    DialogResult = DialogResult.OK;
                }
                else
                {
                    DialogResult = DialogResult.Cancel;
                }
            }
            else
            {
                DialogResult = DialogResult.Cancel;
            }
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Escape) return;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _image.Dispose();
            base.OnFormClosed(e);
        }
    }
}
