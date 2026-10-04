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
    internal sealed class TrayApp : ApplicationContext
    {
        private const int IdUp = 1, IdDown = 2, IdMax = 3, IdMin = 4;

        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly HotkeyWindow _hk = new HotkeyWindow();
        private readonly Osd _osd = new Osd();
        private readonly List<int> _registered = new List<int>();
        private readonly List<string> _failed = new List<string>();
        private int _current = -1;
        private Icon _icon;

        private KeyboardHook _keyboard;
        private Control _marshal;                      // hops work onto the UI thread

        private bool _rawInputOk;
        private int _pendingSteps;
        private DateTime _lastApply = DateTime.MinValue;
        private readonly WinTimer _coalesce = new WinTimer();

        private bool _trayOk;
        private int _trayTries;
        private readonly WinTimer _trayRetry = new WinTimer();
        private readonly SoftwareBrightness _dimmer;
        private int _hardwareBusy;
        private bool _hardwareWarning;
        private bool _exiting;
        private int _maintenanceTicks;
        private readonly WinTimer _ddcRetry = new WinTimer();

        public TrayApp()
        {
            Log.Write("TrayApp: constructing");

            _current = Config.LoadLast();
            if (_current < 0 || _current > 100) _current = 100;
            _dimmer = new SoftwareBrightness(_current);
            _hk.BrightnessCommand = OnBrightnessCommand;

            _tray.Text = "BrightnessCtl";
            UpdateIcon();
            BuildMenu();
            EnsureTray();
            _tray.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    _osd.ShowValue(_current);
                }
            };

            _hk.Pressed += OnHotkey;
            _hk.RawInput += OnRawInput;
            _hk.TaskbarCreated += delegate
            {
                Log.Write("tray: TaskbarCreated received, re-adding icon");
                _trayOk = false;
                EnsureTray();
            };
            RegisterAll();
            RegisterRawInput();
            Log.Write("input: hotkeys=" + _registered.Count
                + " failed=" + _failed.Count
                + " rawInput=" + _rawInputOk);

            // Restore the physical output gain after Windows display events;
            // enforce the target panel's backlight maximum on a worker thread.
            _ddcRetry.Interval = 2000;
            _ddcRetry.Tick += delegate
            {
                _dimmer.Maintain();
                if (++_maintenanceTicks % 5 == 0) QueueHardwareMaximum(false);
            };
            _ddcRetry.Start();

            // Coalesce keyboard auto-repeat to keep the OSD smooth.
            _coalesce.Interval = 130;
            _coalesce.Tick += delegate { FlushPending(); };

            _marshal = new Control();
            IntPtr forceHandle = _marshal.Handle;
            GC.KeepAlive(forceHandle);
            _hk.KeyStep += Queue;
            _keyboard = new KeyboardHook(_hk.Handle, Config.GrabF1F2);
            Log.Write("input: dedicated hook thread active=" + _keyboard.IsActive);

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            QueueHardwareMaximum(true);

            // No balloon when DDC is simply not ready yet at sign-in - _ddcRetry
            // handles that quietly. Only a real hotkey clash is worth a warning.
            if (_failed.Count > 0)
            {
                Balloon("These hotkeys are taken by another app: "
                    + string.Join(", ", _failed.ToArray()), ToolTipIcon.Warning);
            }

            Log.Write("TrayApp: ready");
        }

        private void RegisterAll()
        {
            _failed.Clear();
            TryRegister(IdUp, Config.KeyUp);
            TryRegister(IdDown, Config.KeyDown);
            TryRegister(IdMax, Config.KeyMax);
            TryRegister(IdMin, Config.KeyMin);
        }

        private void TryRegister(int id, string combo)
        {
            uint mods, vk;
            if (!Config.ParseHotkey(combo, out mods, out vk)) return;
            if (Native.RegisterHotKey(_hk.Handle, id, mods | Native.MOD_NOREPEAT, vk))
                _registered.Add(id);
            else
                _failed.Add(combo);
        }

        private void UnregisterAll()
        {
            foreach (int id in _registered) Native.UnregisterHotKey(_hk.Handle, id);
            _registered.Clear();
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            if (!_exiting && _marshal != null && !_marshal.IsDisposed)
                _marshal.BeginInvoke((MethodInvoker)delegate { if (!_exiting) Rebind(); });
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume || _exiting || _marshal == null || _marshal.IsDisposed) return;
            _marshal.BeginInvoke((MethodInvoker)delegate
            {
                if (_exiting) return;
                if (!Config.RestoreOnResume) Apply(100, false);
                Rebind();
            });
        }

        private void QueueHardwareMaximum(bool refresh)
        {
            if (_exiting || Interlocked.CompareExchange(ref _hardwareBusy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (refresh || Ddc.Count == 0) Ddc.Refresh();
                    bool ok = Ddc.EnsureMaximum();
                    if (!ok && Ddc.Count > 0)
                    {
                        Ddc.Refresh();
                        ok = Ddc.EnsureMaximum();
                    }
                    if (refresh || _hardwareWarning != !ok)
                        Log.Write("hardware: maximum=" + ok + " [" + Ddc.HardwareInfo() + "]");
                    _hardwareWarning = !ok;
                }
                catch (Exception ex) { Log.Write("hardware: " + ex.Message); }
                finally { Interlocked.Exchange(ref _hardwareBusy, 0); }
            });
        }

        private void Rebind()
        {
            _dimmer.Refresh();
            _dimmer.Set(_current);
            UpdateIcon();
            QueueHardwareMaximum(true);
        }

        // A successful response means the resident process has applied the
        // software level. CLI commands never start an independent dimmer.
        private int OnBrightnessCommand(int command, int value)
        {
            if (command == 0) { _dimmer.Maintain(); return _current + 1; }
            if (command == 1 || command == 2)
            {
                _pendingSteps = 0;
                _coalesce.Stop();
                int target = command == 2 ? _current + value : value;
                return Apply(target, true) ? _current + 1 : 0;
            }
            if (command == 3) { Rebind(); return _current + 1; }
            if (command == 4)
            {
                _marshal.BeginInvoke((MethodInvoker)ExitApp);
                return 1;
            }
            return 0;
        }

        // Keyboards in "Mac mode" send F1/F2 as HID Consumer Page brightness
        // usages instead of key codes. Those never become keyboard events, so
        // no hook can see them - Raw Input is the only place they surface.
        // Adding a notification icon fails while Explorer is still coming up,
        // which is exactly when a sign-in launch happens. Retry instead of dying.
        private void EnsureTray()
        {
            if (_trayOk) return;
            try
            {
                _tray.Visible = true;
                _trayOk = true;
                _trayRetry.Stop();
                Log.Write("tray: icon added after " + _trayTries + " retries");
            }
            catch (Exception ex)
            {
                Log.Write("tray: cannot add icon yet (" + ex.Message + "), will retry");
                if (!_trayRetry.Enabled)
                {
                    _trayRetry.Interval = 3000;
                    _trayRetry.Tick += delegate
                    {
                        _trayTries++;
                        if (_trayTries > 40) // ~2 minutes
                        {
                            _trayRetry.Stop();
                            Log.Write("tray: giving up on the icon; hotkeys stay active");
                            return;
                        }
                        EnsureTray();
                    };
                    _trayRetry.Start();
                }
            }
        }

        private void Balloon(string text, ToolTipIcon icon)
        {
            if (!_trayOk) return;
            try { _tray.ShowBalloonTip(6000, "BrightnessCtl", text, icon); }
            catch { }
        }

        private void RegisterRawInput()
        {
            Native.RAWINPUTDEVICE[] rid = new Native.RAWINPUTDEVICE[1];
            rid[0].usUsagePage = Native.USAGE_PAGE_CONSUMER;
            rid[0].usUsage = Native.USAGE_CONSUMER_CONTROL;
            rid[0].dwFlags = Native.RIDEV_INPUTSINK; // deliver even when unfocused
            rid[0].hwndTarget = _hk.Handle;
            _rawInputOk = Native.RegisterRawInputDevices(rid, 1,
                (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)));
        }

        private void OnRawInput(IntPtr hRaw)
        {
            if (!Config.GrabF1F2) return;

            uint headerSize = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTHEADER));
            uint size = 0;
            if (Native.GetRawInputData(hRaw, Native.RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0) return;
            if (size == 0 || size > 4096) return;

            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Native.GetRawInputData(hRaw, Native.RID_INPUT, buf, ref size, headerSize) != size) return;

                Native.RAWINPUTHEADER h = (Native.RAWINPUTHEADER)
                    Marshal.PtrToStructure(buf, typeof(Native.RAWINPUTHEADER));
                if (h.dwType != Native.RIM_TYPEHID) return;

                int off = (int)headerSize;
                int sizeHid = Marshal.ReadInt32(buf, off);
                int count = Marshal.ReadInt32(buf, off + 4);
                int dataOff = off + 8;
                if (sizeHid < 3 || count < 1) return;

                // Legacy report layout: [reportId][usage lo][usage hi].
                // Other HID report layouts are not supported by this decoder.
                for (int r = 0; r < count; r++)
                {
                    int p = dataOff + r * sizeHid;
                    if (p + 2 >= size) break;
                    int usage = Marshal.ReadByte(buf, p + 1) | (Marshal.ReadByte(buf, p + 2) << 8);
                    if (usage == Native.USAGE_BRIGHTNESS_UP) Queue(+1);
                    else if (usage == Native.USAGE_BRIGHTNESS_DOWN) Queue(-1);
                }
            }
            catch { }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private void Queue(int steps)
        {
            _pendingSteps += steps;
            if ((DateTime.UtcNow - _lastApply).TotalMilliseconds >= _coalesce.Interval)
            {
                FlushPending();
            }
            else
            {
                _coalesce.Stop();
                _coalesce.Start();
            }
        }

        private void FlushPending()
        {
            _coalesce.Stop();
            int steps = _pendingSteps;
            _pendingSteps = 0;
            if (steps == 0) return;
            _lastApply = DateTime.UtcNow;
            Nudge(steps * Config.Step);
        }

        private int ReadCurrentOrRecover() { return _current; }

        private void Nudge(int delta)
        {
            int next = ReadCurrentOrRecover() + delta;
            if (next < 0) next = 0;
            if (next > 100) next = 100;
            Apply(next);
        }

        private void OnHotkey(int id)
        {
            if (id == IdUp) Nudge(Config.Step);
            else if (id == IdDown) Nudge(-Config.Step);
            else if (id == IdMax) Apply(100);
            else if (id == IdMin) Apply(0);
        }

        private void Apply(int pct) { Apply(pct, true); }

        private bool Apply(int pct, bool showOsd)
        {
            pct = Math.Max(0, Math.Min(100, pct));
            try
            {
                _dimmer.Set(pct);
                _current = pct;
                Config.SaveLast(pct);
                UpdateIcon();
                if (showOsd) _osd.ShowValue(pct);
                QueueHardwareMaximum(false);
                Log.Write("software: " + pct + "%");
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("software: apply failed: " + ex.Message);
                return false;
            }
        }

        private void UpdateIcon()
        {
            Icon old = _icon;
            _icon = MakeIcon(_current);
            _tray.Icon = _icon;
            _tray.Text = "BrightnessCtl - software " + _current + "% | hardware 100% target";
            if (old != null)
            {
                IntPtr h = old.Handle;
                old.Dispose();
                Native.DestroyIcon(h);
            }
        }

        private static Icon MakeIcon(int pct)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    int fill = 90 + (int)(165 * (pct / 100.0));
                    Color c = Color.FromArgb(255, fill, (int)(fill * 0.82), 70);
                    using (SolidBrush b = new SolidBrush(c))
                    using (Pen p = new Pen(c, 2.6f))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        g.FillEllipse(b, 11, 11, 10, 10);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4.0;
                            g.DrawLine(p,
                                16 + (float)(Math.Cos(a) * 9), 16 + (float)(Math.Sin(a) * 9),
                                16 + (float)(Math.Cos(a) * 14), 16 + (float)(Math.Sin(a) * 14));
                        }
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        private void BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            int[] presets = new int[] { 100, 75, 50, 25, 10, 0 };
            foreach (int p in presets)
            {
                int captured = p;
                ToolStripMenuItem mi = new ToolStripMenuItem(p + "%");
                mi.Click += delegate { Apply(captured); };
                menu.Items.Add(mi);
            }

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem info = new ToolStripMenuItem("Monitor info...");
            info.Click += delegate
            {
                MessageBox.Show(
                    "Target: " + Config.TargetOutput + Environment.NewLine +
                    "Step: " + Config.Step + "%" + Environment.NewLine +
                    "Detected: " + Ddc.Describe() + Environment.NewLine +
                    "Software: " + _current + "%" + Environment.NewLine +
                    "Hardware: " + Ddc.HardwareInfo() + Environment.NewLine + Environment.NewLine +
                    "Hotkeys:" + Environment.NewLine +
                    "  " + Config.KeyUp + "  - brighter" + Environment.NewLine +
                    "  " + Config.KeyDown + "  - dimmer" + Environment.NewLine +
                    "  " + Config.KeyMax + "  - 100%" + Environment.NewLine +
                    "  " + Config.KeyMin + "  - 0%" + Environment.NewLine +
                    "  F1 / F2  - dimmer / brighter" +
                        (Config.GrabF1F2 ? "" : "  (off)") + Environment.NewLine + Environment.NewLine +
                    "Key hook active:  " + (_keyboard != null && _keyboard.IsActive ? "yes" : "no") + Environment.NewLine +
                    "HID brightness keys: " + (_rawInputOk ? "listening" : "no") + Environment.NewLine +
                    "Settings file:" + Environment.NewLine + Config.IniPath,
                    "BrightnessCtl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            menu.Items.Add(info);

            ToolStripMenuItem cfg = new ToolStripMenuItem("Edit settings");
            cfg.Click += delegate
            {
                Config.WriteDefaultIfMissing();
                try { System.Diagnostics.Process.Start("notepad.exe", Config.IniPath); }
                catch { }
            };
            menu.Items.Add(cfg);

            ToolStripMenuItem rescan = new ToolStripMenuItem("Rescan monitors");
            rescan.Click += delegate { Rebind(); };
            menu.Items.Add(rescan);

            ToolStripMenuItem grab = new ToolStripMenuItem("Use F1 / F2 for brightness");
            grab.Checked = Config.GrabF1F2;
            grab.Click += delegate
            {
                Config.GrabF1F2 = !Config.GrabF1F2;
                grab.Checked = Config.GrabF1F2;
                _keyboard.SetEnabled(Config.GrabF1F2);
            };
            menu.Items.Add(grab);

            ToolStripMenuItem startup = new ToolStripMenuItem("Run at sign-in");
            startup.Checked = Startup.IsEnabled();
            startup.Click += delegate
            {
                bool now = !Startup.IsEnabled();
                Startup.Set(now);
                startup.Checked = Startup.IsEnabled();
            };
            menu.Items.Add(startup);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem quit = new ToolStripMenuItem("Exit");
            quit.Click += delegate { ExitApp(); };
            menu.Items.Add(quit);

            _tray.ContextMenuStrip = menu;
        }

        private void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _dimmer.Dispose();
            _osd.Dispose();
            _coalesce.Stop();
            _trayRetry.Stop();
            _ddcRetry.Stop();
            Log.Write("exit: user requested");
            _keyboard.Dispose();
            UnregisterAll();
            if (_marshal != null) _marshal.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _hk.DestroyHandle();
            ExitThread();
        }
    }
}
