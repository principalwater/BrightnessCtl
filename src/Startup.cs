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

        // Prefer the matching installed task so a short-lived command/tool job
        // does not own the long-lived resident and its recovery helper.
        public static void StartResident()
        {
            try
            {
                var query = new System.Diagnostics.ProcessStartInfo("schtasks.exe", "/Query /TN BrightnessCtl /XML");
                query.UseShellExecute = false;
                query.CreateNoWindow = true;
                query.RedirectStandardOutput = true;
                query.RedirectStandardError = true;
                using (var process = System.Diagnostics.Process.Start(query))
                {
                    string xml = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                    {
                        var document = new System.Xml.XmlDocument();
                        document.XmlResolver = null;
                        document.LoadXml(xml);
                        var command = document.SelectSingleNode("//*[local-name()='Exec']/*[local-name()='Command']");
                        var arguments = document.SelectSingleNode("//*[local-name()='Exec']/*[local-name()='Arguments']");
                        if (command != null && (arguments == null || string.IsNullOrWhiteSpace(arguments.InnerText)) &&
                            string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(command.InnerText.Trim('"'))),
                                Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                        {
                            var run = new System.Diagnostics.ProcessStartInfo("schtasks.exe", "/Run /TN BrightnessCtl");
                            run.UseShellExecute = false;
                            run.CreateNoWindow = true;
                            run.RedirectStandardOutput = true;
                            run.RedirectStandardError = true;
                            using (var task = System.Diagnostics.Process.Start(run))
                            {
                                task.StandardOutput.ReadToEnd();
                                task.WaitForExit();
                                if (task.ExitCode == 0) return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("start: task launch unavailable: " + ex.Message); }
            var start = new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + Application.ExecutablePath + "\"");
            start.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            System.Diagnostics.Process.Start(start);
        }

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
