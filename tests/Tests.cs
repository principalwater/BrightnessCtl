// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
namespace BrightnessCtl
{
    internal static class Tests
    {
        private static int assertions;
        private static void Check(bool value, string label) { assertions++; if (!value) throw new Exception(label); }
        public static int Main()
        {
            int b, c;
            ColorGain.Calculate(17, 83, 100, out b, out c);
            Check(b == 17 && c == 83, "100% must preserve the original calibration controls");
            ColorGain.Calculate(17, 83, 0, out b, out c);
            Check(b == -50 && c == 0, "0% must have zero gain");
            ColorGain.Calculate(0, 100, -5, out b, out c);
            Check(b == -50 && c == 0, "Clamp negative levels");
            ColorGain.Calculate(0, 100, 120, out b, out c);
            Check(b == 0 && c == 100, "Clamp excessive levels");
            ColorGain.Calculate(0, 100, 60, out b, out c);
            Check(b == -20 && c == 60, "Neutral baseline at 60%");
            int previous = -1;
            for (int level = 0; level <= 100; level++)
            {
                ColorGain.Calculate(17, 83, level, out b, out c);
                Check(c >= previous && c <= 83, "Gain must not increase beyond the baseline"); previous = c;
            }
            var first = new AmdOutput.Output { Key = "connector-a", Name = "Panel" };
            var second = new AmdOutput.Output { Key = "connector-b", Name = "Panel" };
            var outputs = new List<AmdOutput.Output> { first, second };
            Check(AmdOutput.Choose(outputs, "connector-b", "") == second, "Explicit output selection");
            Check(AmdOutput.Choose(outputs, "disconnected", "") == null, "Disconnected target must not dim a different display");
            Check(AmdOutput.Choose(new List<AmdOutput.Output> { first }, "", "") == first, "Single-output default");
            bool ambiguous = false;
            try { AmdOutput.Choose(outputs, "", "Panel"); } catch (InvalidOperationException) { ambiguous = true; }
            Check(ambiguous, "Duplicate panel models require explicit selection");
            uint mods, key;
            Check(Config.ParseHotkey("Ctrl+Alt+Up", out mods, out key) && mods == 3 && key == 38, "Hotkey modifiers");
            Check(!Config.ParseHotkey("NotAKey", out mods, out key), "Reject invalid keys");
            Check(!Config.ParseHotkey("", out mods, out key), "Empty key disables a shortcut");
            Console.WriteLine("PASS: " + assertions + " assertions; no display or user settings modified.");
            return 0;
        }
    }
}
