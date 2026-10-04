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
    internal static class Ddc
    {
        private static readonly object Gate = new object();
        private static readonly List<Native.PHYSICAL_MONITOR[]> Blocks = new List<Native.PHYSICAL_MONITOR[]>();
        private static readonly List<Target> Targets = new List<Target>();

        public static int Count
        {
            get { lock (Gate) { return Targets.Count; } }
        }

        public static string Describe()
        {
            lock (Gate)
            {
                if (Targets.Count == 0) return "no DDC/CI-capable monitor found";
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < Targets.Count; i++)
                {
                    if (i > 0) sb.Append("; ");
                    sb.Append(Targets[i].Desc);
                    sb.Append(" [raw ");
                    sb.Append(Targets[i].Min);
                    sb.Append("-");
                    sb.Append(Targets[i].Max);
                    sb.Append("]");
                }
                return sb.ToString();
            }
        }

        private static void ReleaseLocked()
        {
            foreach (Native.PHYSICAL_MONITOR[] b in Blocks)
            {
                try { Native.DestroyPhysicalMonitors((uint)b.Length, b); }
                catch { }
            }
            Blocks.Clear();
            Targets.Clear();
        }

        public static void Refresh()
        {
            lock (Gate)
            {
                ReleaseLocked();

                List<IntPtr> handles = new List<IntPtr>();
                Native.MonitorEnumProc cb = delegate(IntPtr h, IntPtr hdc, IntPtr r, IntPtr d)
                {
                    handles.Add(h);
                    return true;
                };
                Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
                GC.KeepAlive(cb);

                foreach (IntPtr h in handles)
                {
                    if (!Native.IsTargetMonitor(h)) continue;
                    uint n = 0;
                    if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(h, ref n) || n == 0) continue;

                    Native.PHYSICAL_MONITOR[] arr = new Native.PHYSICAL_MONITOR[n];
                    if (!Native.GetPhysicalMonitorsFromHMONITOR(h, n, arr)) continue;
                    Blocks.Add(arr);

                    foreach (Native.PHYSICAL_MONITOR pm in arr)
                    {
                        uint mn = 0, cu = 0, mx = 0;
                        if (TryGet(pm.hPhysicalMonitor, ref mn, ref cu, ref mx) && mx > mn)
                        {
                            Target t = new Target();
                            t.Handle = pm.hPhysicalMonitor;
                            t.Desc = pm.szPhysicalMonitorDescription;
                            t.Min = mn;
                            t.Max = mx;
                            Targets.Add(t);
                        }
                    }
                }
            }
        }

        // DDC/CI rides on a slow I2C link that drops the odd message, so a few
        // retries turn a flaky channel into a dependable one.
        private static bool TryGet(IntPtr h, ref uint mn, ref uint cu, ref uint mx)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (Native.GetMonitorBrightness(h, ref mn, ref cu, ref mx)) return true;
                Thread.Sleep(40);
            }
            return false;
        }

        private static bool TrySet(Target t, uint raw)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (Native.SetMonitorBrightness(t.Handle, raw)) return true;
                Thread.Sleep(40);
            }
            // Some firmware refuses the high-level call but honours raw VCP 0x10.
            return Native.SetVCPFeature(t.Handle, 0x10, raw);
        }

        public static int Get()
        {
            lock (Gate)
            {
                if (Targets.Count == 0) return -1;
                Target t = Targets[0];
                uint mn = 0, cu = 0, mx = 0;
                if (!TryGet(t.Handle, ref mn, ref cu, ref mx)) return -1;
                if (mx <= mn) return -1;
                return (int)Math.Round((cu - (double)mn) * 100.0 / (mx - mn));
            }
        }

        // User brightness never reaches DDC/CI. Only the maximum backlight
        // value is written, and every physical monitor is read back separately.
        public static bool EnsureMaximum()
        {
            lock (Gate)
            {
                if (Targets.Count == 0) return false;
                bool all = true;
                foreach (Target t in Targets)
                {
                    uint mn = 0, cu = 0, mx = 0;
                    bool ok = TryGet(t.Handle, ref mn, ref cu, ref mx) && mx > mn;
                    if (ok && cu == mx) continue;
                    uint maximum = ok ? mx : t.Max;
                    ok = TrySet(t, maximum);
                    if (ok)
                    {
                        Thread.Sleep(80);
                        ok = TryGet(t.Handle, ref mn, ref cu, ref mx) && cu == mx;
                    }
                    if (!ok) all = false;
                }
                return all;
            }
        }

        public static string HardwareInfo()
        {
            lock (Gate)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Target t in Targets)
                {
                    uint mn = 0, cu = 0, mx = 0;
                    if (sb.Length > 0) sb.Append("; ");
                    sb.Append(t.Desc + ": ");
                    if (TryGet(t.Handle, ref mn, ref cu, ref mx) && mx > mn)
                        sb.Append(Math.Round((cu - (double)mn) * 100 / (mx - mn)) + "% (raw " + cu + "/" + mx + ")");
                    else sb.Append("unavailable");
                }
                return sb.Length == 0 ? "DDC/CI unavailable" : sb.ToString();
            }
        }
    }
}
