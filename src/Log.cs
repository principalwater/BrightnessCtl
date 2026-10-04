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
    internal static class Log
    {
        private static readonly object Gate = new object();

        public static string FilePath { get { return Path.Combine(Config.Dir, "startup.log"); } }

        public static void Write(string s)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Config.Dir);
                    string p = FilePath;
                    if (File.Exists(p) && new FileInfo(p).Length > 64 * 1024)
                    {
                        string[] all = File.ReadAllLines(p);
                        int start = Math.Max(0, all.Length - 200);
                        string[] keep = new string[all.Length - start];
                        Array.Copy(all, start, keep, 0, keep.Length);
                        File.WriteAllLines(p, keep);
                    }
                    File.AppendAllText(p,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                        + "  " + s + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}
