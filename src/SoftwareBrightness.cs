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
    internal sealed class SoftwareBrightness : IDisposable
    {
        private AmdOutput.Output _output;
        private string _key;
        private int _baselineBrightness, _baselineContrast;
        private int _expectedBrightness, _expectedContrast;
        private bool _initialized;
        private bool _watchdogStarted;
        public int Current { get; private set; }

        public SoftwareBrightness(int value)
        {
            AmdOutput.Initialize();
            _initialized = true;
            Current = Math.Max(0, Math.Min(100, value));
            try
            {
                Refresh();
                if (_output == null && string.IsNullOrEmpty(Config.TargetOutput))
                    throw new InvalidOperationException("No AMD physical display found. Run BrightnessCtl.exe list.");
            }
            catch { Dispose(); throw; }
        }

        // The lease keeps the original driver settings across an unexpected
        // exit. It is written before changing scanout, so a restart never treats
        // an already dimmed output as the baseline and multiplies dimming.
        private void SaveLease(int brightness, int contrast)
        {
            System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess();
            string[] lines = { process.Id.ToString(CultureInfo.InvariantCulture),
                process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), _key,
                _baselineBrightness.ToString(CultureInfo.InvariantCulture),
                _baselineContrast.ToString(CultureInfo.InvariantCulture),
                brightness.ToString(CultureInfo.InvariantCulture), contrast.ToString(CultureInfo.InvariantCulture) };
            Directory.CreateDirectory(Config.Dir);
            string temp = Config.ColorStatePath + ".new";
            File.WriteAllLines(temp, lines);
            if (File.Exists(Config.ColorStatePath)) File.Replace(temp, Config.ColorStatePath, null);
            else File.Move(temp, Config.ColorStatePath);
            if (!_watchdogStarted)
            {
                System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath,
                    "--watchdog " + lines[0] + " " + lines[1]);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                System.Diagnostics.Process.Start(start);
                _watchdogStarted = true;
            }
        }

        private void Bind(AmdOutput.Output output)
        {
            if (output == null) { _output = null; return; }
            if (_key != output.Key)
            {
                int originalBrightness = AmdOutput.Get(output, 1);
                int originalContrast = AmdOutput.Get(output, 2);
                if (File.Exists(Config.ColorStatePath))
                {
                    string[] state = File.ReadAllLines(Config.ColorStatePath);
                    if (state.Length == 7 && state[2] == output.Key)
                    {
                        originalBrightness = int.Parse(state[3], CultureInfo.InvariantCulture);
                        originalContrast = int.Parse(state[4], CultureInfo.InvariantCulture);
                    }
                }
                _key = output.Key;
                _baselineBrightness = originalBrightness;
                _baselineContrast = originalContrast;
                _expectedBrightness = originalBrightness;
                _expectedContrast = originalContrast;
                Log.Write("software backend: AMD connector " + output.Name + " key=" + _key);
            }
            _output = output;
        }

        public void Set(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            Bind(AmdOutput.Find());
            if (_output != null)
            {
                int contrast, brightness;
                ColorGain.Calculate(_baselineBrightness, _baselineContrast, percent, out brightness, out contrast);
                SaveLease(brightness, contrast);
                try { AmdOutput.Set(_output, brightness, contrast); }
                catch
                {
                    try { AmdOutput.Set(_output, _expectedBrightness, _expectedContrast); }
                    catch (Exception ex) { Log.Write("software: rollback failed: " + ex.Message); }
                    throw;
                }
                _expectedBrightness = brightness;
                _expectedContrast = contrast;
            }
            Current = percent;
        }

        public void Refresh() { Set(Current); }

        public void Maintain()
        {
            if (!_initialized) return;
            AmdOutput.Output found = AmdOutput.Find();
            if (found == null) { _output = null; return; }
            if (_output == null || found.Key != _key)
            {
                Bind(found);
                Set(Current);
                return;
            }
            _output = found;
            if (AmdOutput.Get(_output, 1) != _expectedBrightness || AmdOutput.Get(_output, 2) != _expectedContrast)
            {
                AmdOutput.Set(_output, _expectedBrightness, _expectedContrast);
                Log.Write("software: restored " + _output.Name + " output gain, level=" + Current + "%");
            }
        }

        public void Dispose()
        {
            if (!_initialized) return;
            try
            {
                AmdOutput.Output found = AmdOutput.Find();
                if (found != null && found.Key == _key)
                {
                    AmdOutput.Set(found, _baselineBrightness, _baselineContrast);
                    if (File.Exists(Config.ColorStatePath)) File.Delete(Config.ColorStatePath);
                }
            }
            catch (Exception ex) { Log.Write("software: restore on exit failed: " + ex.Message); }
            finally { AmdOutput.Close(); _initialized = false; }
        }

        public static int Watchdog(string[] args)
        {
            if (args.Length != 3) return 1;
            int owner;
            long started;
            if (!int.TryParse(args[1], out owner) || !long.TryParse(args[2], out started)) return 1;
            try
            {
                System.Diagnostics.Process parent = System.Diagnostics.Process.GetProcessById(owner);
                if (parent.StartTime.ToUniversalTime().Ticks != started) return 1;
                parent.WaitForExit();
            }
            catch (ArgumentException) { }
            // A replacement resident may already own the mutex and lease. In
            // that case it has recovered the baseline; the old watchdog exits.
            using (Mutex mutex = new Mutex(false, "Local\\BrightnessCtl.SingleInstance"))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(500); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) return 0;
                try
                {
                    if (!File.Exists(Config.ColorStatePath)) return 0;
                    string[] state = File.ReadAllLines(Config.ColorStatePath);
                    if (state.Length != 7 || state[0] != args[1] || state[1] != args[2]) return 0;
                    AmdOutput.Initialize();
                    try
                    {
                        AmdOutput.Output output = AmdOutput.Find();
                        if (output != null && output.Key == state[2])
                        {
                            AmdOutput.Set(output, int.Parse(state[3], CultureInfo.InvariantCulture),
                                int.Parse(state[4], CultureInfo.InvariantCulture));
                            File.Delete(Config.ColorStatePath);
                            Log.Write("watchdog: original physical-output color controls restored");
                        }
                    }
                    finally { AmdOutput.Close(); }
                }
                catch (Exception ex) { Log.Write("watchdog: recovery failed: " + ex.Message); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }
    }
}
