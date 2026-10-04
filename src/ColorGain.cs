// SPDX-License-Identifier: MIT
using System;
namespace BrightnessCtl
{
    internal static class ColorGain
    {
        public static void Calculate(int basisBrightness, int basisContrast, int percent, out int brightness, out int contrast)
        {
            double gain = Math.Max(0, Math.Min(100, percent)) / 100.0;
            contrast = (int)Math.Round(basisContrast * gain);
            brightness = (int)Math.Round((50 + basisBrightness) * gain - 50);
        }
    }
}
