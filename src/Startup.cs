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
    internal static class Startup
    {
        private const string Key = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string Name = "BrightnessCtl";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key, false))
                {
                    if (k == null) return false;
                    return k.GetValue(Name) != null;
                }
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key, true))
                {
                    if (k == null) return;
                    if (on)
                        k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
                    else
                        k.DeleteValue(Name, false);
                }
            }
            catch { }
        }
    }
}
