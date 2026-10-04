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
    internal static class Config
    {
        public static int Step = 5;
        public static string TargetMonitor = ""; // Legacy config migration only.
        public static string TargetOutput = "";
        public static volatile string ActiveWindowsDevice = "";
        public static string TargetMonitorId = ""; // Optional legacy DDC filter.
        public static string KeyUp = "Ctrl+Alt+Up";
        public static string KeyDown = "Ctrl+Alt+Down";
        public static string KeyMax = "Ctrl+Alt+PageUp";
        public static string KeyMin = "Ctrl+Alt+PageDown";
        public static bool RestoreOnResume = true;

        // F1/F2 interception is opt-in so normal Help keys remain available.
        // Existing local configurations retain their own choice.
        public static bool GrabF1F2 = false;

        public static string Dir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BrightnessCtl");
            }
        }

        public static string IniPath { get { return Path.Combine(Dir, "config.ini"); } }
        public static string StatePath { get { return Path.Combine(Dir, "software.txt"); } }

        public static string ColorStatePath { get { return Path.Combine(Dir, "output-color-lease.txt"); } }

        public static void Load()
        {
            try
            {
                if (!File.Exists(IniPath)) return;
                foreach (string line in File.ReadAllLines(IniPath))
                {
                    string s = line.Trim();
                    if (s.Length == 0 || s.StartsWith("#") || s.StartsWith(";")) continue;
                    int eq = s.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = s.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = s.Substring(eq + 1).Trim();
                    if (k == "step")
                    {
                        int iv;
                        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv) && iv > 0 && iv <= 50)
                            Step = iv;
                    }
                    else if (k == "targetoutput") TargetOutput = v;
                    else if (k == "targetmonitor" && v.Length > 0) TargetMonitor = v;
                    else if (k == "targetmonitorid" && v.Length > 0) TargetMonitorId = v;
                    else if (k == "up") KeyUp = v;
                    else if (k == "down") KeyDown = v;
                    else if (k == "max") KeyMax = v;
                    else if (k == "min") KeyMin = v;
                    else if (k == "restoreonresume") RestoreOnResume = (v == "1" || v.ToLowerInvariant() == "true");
                    else if (k == "grabf1f2") GrabF1F2 = (v == "1" || v.ToLowerInvariant() == "true");
                }
            }
            catch { }
        }

        public static void WriteDefaultIfMissing()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(IniPath)) return;
                string[] lines = new string[] {
                    "# BrightnessCtl settings. Restart the app after editing.",
                    "# Hotkey format: Ctrl / Alt / Shift / Win joined by a plus sign,",
                    "# followed by a key name (Up, Down, F1, PageUp, Oemplus, ...).",
                    "# Leave a value empty to disable that hotkey.",
                    "",
                    "step=5",
                    "# With multiple AMD displays use list, then select <output-id>.",
                    "targetOutput=",
                    "up=Ctrl+Alt+Up",
                    "down=Ctrl+Alt+Down",
                    "max=Ctrl+Alt+PageUp",
                    "min=Ctrl+Alt+PageDown",
                    "",
                    "# Keep software dimming after waking; 0 resets software to 100%.",
                    "restoreOnResume=1",
                    "",
                    "# Optional bare F1/F2 and supported HID brightness keys.",
                    "grabF1F2=0",
                    ""
                };
                File.WriteAllText(IniPath, string.Join(Environment.NewLine, lines));
            }
            catch { }
        }

        public static void SaveTargetOutput(string key)
        {
            List<string> lines = new List<string>(File.ReadAllLines(IniPath));
            bool replaced = false;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].TrimStart().StartsWith("targetOutput=", StringComparison.OrdinalIgnoreCase))
                { lines[i] = "targetOutput=" + key; replaced = true; }
            if (!replaced) lines.Add("targetOutput=" + key);
            string temp = IniPath + ".new";
            File.WriteAllLines(temp, lines.ToArray());
            File.Replace(temp, IniPath, null);
            TargetOutput = key;
        }

        public static void SaveLast(int pct)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(StatePath, pct.ToString(CultureInfo.InvariantCulture));
            }
            catch { }
        }

        public static int LoadLast()
        {
            try
            {
                if (!File.Exists(StatePath)) return -1;
                int v;
                if (int.TryParse(File.ReadAllText(StatePath).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    return v;
            }
            catch { }
            return -1;
        }

        public static bool ParseHotkey(string s, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;
            if (string.IsNullOrEmpty(s)) return false;
            foreach (string part in s.Split('+'))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                string lp = p.ToLowerInvariant();
                if (lp == "ctrl" || lp == "control") mods |= Native.MOD_CONTROL;
                else if (lp == "alt") mods |= Native.MOD_ALT;
                else if (lp == "shift") mods |= Native.MOD_SHIFT;
                else if (lp == "win") mods |= Native.MOD_WIN;
                else
                {
                    try { vk = (uint)(Keys)Enum.Parse(typeof(Keys), p, true); }
                    catch { return false; }
                }
            }
            return vk != 0;
        }
    }
}
