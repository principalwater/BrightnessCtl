// SPDX-License-Identifier: MIT
// Copyright (c) 2026 principalwater
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using WinTimer = System.Windows.Forms.Timer;

namespace BrightnessCtl
{
    internal sealed class Osd : Form
    {
        private int _value;
        private readonly WinTimer _hold = new WinTimer();
        private readonly WinTimer _fade = new WinTimer();

        public Osd()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(280, 92);
            BackColor = Color.FromArgb(18, 18, 20);
            Opacity = 0;
            DoubleBuffered = true;

            _hold.Interval = 1100;
            _hold.Tick += delegate { _hold.Stop(); _fade.Start(); };

            _fade.Interval = 30;
            _fade.Tick += delegate
            {
                if (Opacity <= 0.06)
                {
                    _fade.Stop();
                    Opacity = 0;
                    Hide();
                }
                else Opacity -= 0.06;
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        public void ShowValue(int pct)
        {
            _value = pct;
            Screen target = null;
            foreach (Screen screen in Screen.AllScreens)
                if (Native.IsTargetDevice(screen.DeviceName)) { target = screen; break; }
            if (target == null) return;
            Rectangle wa = target.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - 110);
            _hold.Stop();
            _fade.Stop();
            Opacity = 0.92;
            if (!Visible) Show();
            Invalidate();
            _hold.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (GraphicsPath path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 14))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(24, 24, 27)))
            using (Pen edge = new Pen(Color.FromArgb(70, 70, 78)))
            {
                g.FillPath(bg, path);
                g.DrawPath(edge, path);
            }

            DrawSun(g, new Point(34, 34), 11, Color.FromArgb(245, 200, 90));

            using (Font f = new Font("Segoe UI", 17F, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 240, 245)))
            {
                string txt = _value.ToString(CultureInfo.InvariantCulture) + "%";
                SizeF sz = g.MeasureString(txt, f);
                g.DrawString(txt, f, b, Width - sz.Width - 20, 20);
            }

            int barX = 20, barY = 62, barW = Width - 40, barH = 8;
            using (SolidBrush track = new SolidBrush(Color.FromArgb(58, 58, 64)))
            using (GraphicsPath tp = Rounded(new Rectangle(barX, barY, barW, barH), barH / 2))
                g.FillPath(track, tp);

            int fillW = (int)Math.Round(barW * (_value / 100.0));
            if (fillW > barH)
            {
                using (LinearGradientBrush fill = new LinearGradientBrush(
                    new Rectangle(barX, barY, Math.Max(fillW, 2), barH),
                    Color.FromArgb(250, 214, 120), Color.FromArgb(245, 178, 60), 0f))
                using (GraphicsPath fp = Rounded(new Rectangle(barX, barY, fillW, barH), barH / 2))
                    g.FillPath(fill, fp);
            }
        }

        private static void DrawSun(Graphics g, Point c, int r, Color col)
        {
            using (SolidBrush b = new SolidBrush(col))
            using (Pen p = new Pen(col, 2f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                g.FillEllipse(b, c.X - r / 2, c.Y - r / 2, r, r);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4.0;
                    int x1 = c.X + (int)(Math.Cos(a) * (r * 0.85));
                    int y1 = c.Y + (int)(Math.Sin(a) * (r * 0.85));
                    int x2 = c.X + (int)(Math.Cos(a) * (r * 1.35));
                    int y2 = c.Y + (int)(Math.Sin(a) * (r * 1.35));
                    g.DrawLine(p, x1, y1, x2, y2);
                }
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0 || d > r.Width || d > r.Height)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
