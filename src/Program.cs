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
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Config.WriteDefaultIfMissing();
            Config.Load();

            if (args.Length > 0 && args[0] == "--watchdog")
                return SoftwareBrightness.Watchdog(args);
            if (args.Length > 0)
                return RunCli(args);

            using (Mutex mtx = new Mutex(false, "Local\\BrightnessCtl.SingleInstance"))
            {
                bool fresh;
                try { fresh = mtx.WaitOne(2000); }
                catch (AbandonedMutexException) { fresh = true; }
                if (!fresh)
                {
                    Log.Write("start: another instance already holds the tray, exiting");
                    return 0;
                }

                Log.Write("start: version=" + VersionInfo.Value);

                // Without these, anything thrown before the message loop kills the
                // process silently - which is what a sign-in launch looked like.
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Write("FATAL unhandled: " + e.ExceptionObject);
                };
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Log.Write("thread exception (continuing): " + e.Exception);
                };
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                try
                {
                    Application.Run(new TrayApp());
                    Log.Write("exit: message loop ended");
                    return 0;
                }
                catch (Exception ex)
                {
                    Log.Write("FATAL during startup: " + ex);
                    MessageBox.Show(ex.Message + Environment.NewLine + "See the local startup.log for details.",
                        "BrightnessCtl", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { mtx.ReleaseMutex(); }
            }
        }

        // A /target:winexe binary owns no console, so borrow the caller's one.
        // Re-opening the standard handle afterwards is what makes the output
        // survive being piped or redirected.
        private static void BindConsole()
        {
            try { Native.AttachConsole(-1); }
            catch { }
            try
            {
                StreamWriter w = new StreamWriter(Console.OpenStandardOutput());
                w.AutoFlush = true;
                Console.SetOut(w);
            }
            catch { }
        }

        private static int RunCli(string[] args)
        {
            BindConsole();
            string a = args[0].Trim();
            string low = a.ToLowerInvariant();
            if (low == "list" || low == "select")
            {
                try
                {
                    if (low == "select" && Native.FindWindow(null, Native.ControlWindow) != IntPtr.Zero)
                    { Console.WriteLine("Exit the resident before selecting another output."); return 1; }
                    AmdOutput.Initialize();
                    try
                    {
                        List<AmdOutput.Output> outputs = AmdOutput.Enumerate();
                        if (low == "list")
                        {
                            foreach (AmdOutput.Output output in outputs) Console.WriteLine(output.ToString());
                            if (outputs.Count == 0) Console.WriteLine("No connected AMD physical displays found.");
                        }
                        else
                        {
                            AmdOutput.Output selected = args.Length == 2 ? AmdOutput.Choose(outputs, args[1], "") : null;
                            if (selected == null) { Console.WriteLine("Use select <output-id> from list."); return 1; }
                            Config.SaveTargetOutput(selected.Key);
                            Console.WriteLine("Selected: " + selected.Name);
                        }
                    }
                    finally { AmdOutput.Close(); }
                    return 0;
                }
                catch (DllNotFoundException) { Console.WriteLine("An AMD display driver with 64-bit ADL is required."); return 3; }
                catch (Exception ex) { Console.WriteLine(ex.Message); return 3; }
            }
            if (low == "--version") { Console.WriteLine("BrightnessCtl " + VersionInfo.Value + " per-monitor output dimming"); return 0; }
            if (low == "-h" || low == "--help" || low == "/?" || low == "help")
            {
                Console.WriteLine("BrightnessCtl " + VersionInfo.Value + " - AMD output brightness; physical backlight held at 100%");
                Console.WriteLine("  BrightnessCtl.exe            start tray app with hotkeys");
                Console.WriteLine("  BrightnessCtl.exe list       list connected AMD physical outputs");
                Console.WriteLine("  BrightnessCtl.exe select ID  choose one output while stopped");
                Console.WriteLine("  BrightnessCtl.exe get        read active software brightness (0-100)");
                Console.WriteLine("  BrightnessCtl.exe 75         set software brightness to 75%");
                Console.WriteLine("  BrightnessCtl.exe +5 / -5  change software brightness");
                Console.WriteLine("  BrightnessCtl.exe info       software level and physical monitor readback");
                Console.WriteLine("  BrightnessCtl.exe rescan     restore dimming after a display change");
                Console.WriteLine("  BrightnessCtl.exe exit       quit and remove software dimming");
                Console.WriteLine("Target: " + Config.TargetOutput + "; hotkey step: " + Config.Step + "%.");
                return 0;
            }

            int command = 0, value = 0;
            bool setting = false;
            if (low == "rescan") command = 3;
            else if (low == "exit") command = 4;
            else if (low != "get" && low != "info")
            {
                if (!int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                { Console.WriteLine("BrightnessCtl: try 0-100, +5, -5, get, info, rescan, exit."); return 1; }
                command = a.StartsWith("+") || a.StartsWith("-") ? 2 : 1;
                value = Math.Max(-100, Math.Min(100, value));
                setting = true;
            }
            IntPtr window = Native.FindWindow(null, Native.ControlWindow);
            if (window == IntPtr.Zero && (setting || command == 3))
            {
                System.Diagnostics.Process.Start(Application.ExecutablePath);
                for (int i = 0; i < 150 && window == IntPtr.Zero; i++)
                {
                    Thread.Sleep(100);
                    window = Native.FindWindow(null, Native.ControlWindow);
                }
            }
            if (window == IntPtr.Zero)
            { Console.WriteLine("BrightnessCtl: software tray app is not running."); return 2; }
            IntPtr result;
            if (Native.SendMessageTimeout(window, Native.WM_BRIGHTNESS,
                new IntPtr(command), new IntPtr(value), 0x0003, 15000, out result) == IntPtr.Zero
                || result.ToInt64() <= 0)
            { Console.WriteLine("BrightnessCtl: resident app did not apply the command."); return 2; }
            int current = result.ToInt32() - 1;
            if (low == "info")
            {
                string outputInfo = AmdOutput.Info();
                Ddc.Refresh();
                Console.WriteLine("Version : " + VersionInfo.Value + " (AMD physical-output RGB gain)");
                Console.WriteLine("Software: " + current + "%");
                Console.WriteLine("Target  : " + outputInfo);
                Console.WriteLine("Step    : " + Config.Step + "%");
                Console.WriteLine("Hardware: " + Ddc.HardwareInfo());
                Console.WriteLine("Settings: " + Config.IniPath);
            }
            else if (command != 4) Console.WriteLine(current.ToString(CultureInfo.InvariantCulture));
            return 0;
        }
    }
}
