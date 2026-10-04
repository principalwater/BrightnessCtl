// SPDX-License-Identifier: MIT
// Copyright (c) 2026 principalwater
// ADL interop declarations adapted from AMD SDK headers; see THIRD_PARTY_NOTICES.md.
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
    internal static class AmdOutput
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr Allocate(int size);
        private static readonly Allocate Allocator = delegate(int size) { return Marshal.AllocHGlobal(size); };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct Adapter
        {
            public int Size, Index;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Udid;
            public int Bus, Device, Function, Vendor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Display;
            public int Present, Exists;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Path;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string PathExt;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Pnp;
            public int OsIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayId
        {
            public int LogicalDisplay, PhysicalDisplay, LogicalAdapter, PhysicalAdapter;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DisplayInfo
        {
            public DisplayId Id;
            public int Controller;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Manufacturer;
            public int Type, Output, Connector, Mask, Value;
        }

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL_Main_Control_Create(Allocate allocate, int present);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL_Main_Control_Destroy();
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL_Adapter_NumberOfAdapters_Get(out int count);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL_Adapter_AdapterInfo_Get(IntPtr buffer, int size);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL_Display_DisplayInfo_Get(int adapter, out int count, out IntPtr buffer, int force);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL_Display_Color_Get(int adapter, int display, int type,
            out int current, out int defaultValue, out int min, out int max, out int step);
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL_Display_Color_Set(int adapter, int display, int type, int value);

        internal sealed class Output
        {
            public int Adapter, Display;
            public string Name, Key, WindowsDevice;
            public bool Mapped;
            public override string ToString() { return Key + "  " + Name + (Mapped ? " (active)" : " (unmapped)"); }
        }

        public static void Initialize()
        {
            Check(ADL_Main_Control_Create(Allocator, 1), "initialize AMD display controls");
        }

        public static void Close() { ADL_Main_Control_Destroy(); }

        private static void Check(int result, string operation)
        {
            if (result != 0) throw new InvalidOperationException("AMD: cannot " + operation + " (code " + result + ")");
        }

        public static List<Output> Enumerate()
        {
            int count;
            Check(ADL_Adapter_NumberOfAdapters_Get(out count), "enumerate adapters");
            int size = Marshal.SizeOf(typeof(Adapter));
            IntPtr buffer = Marshal.AllocHGlobal(count * size);
            try
            {
                for (int i = 0; i < count; i++) Marshal.WriteInt32(buffer, i * size, size);
                Check(ADL_Adapter_AdapterInfo_Get(buffer, count * size), "read adapters");
                Dictionary<int, Adapter> adapters = new Dictionary<int, Adapter>();
                for (int i = 0; i < count; i++)
                {
                    Adapter a = (Adapter)Marshal.PtrToStructure(IntPtr.Add(buffer, i * size), typeof(Adapter));
                    adapters[a.Index] = a;
                }
                Dictionary<string, Output> outputs = new Dictionary<string, Output>();
                foreach (Adapter adapter in adapters.Values)
                {
                    if (adapter.Present == 0) continue;
                    int displays;
                    IntPtr displayBuffer;
                    if (ADL_Display_DisplayInfo_Get(adapter.Index, out displays, out displayBuffer, 0) != 0) continue;
                    try
                    {
                        int displaySize = Marshal.SizeOf(typeof(DisplayInfo));
                        for (int j = 0; j < displays; j++)
                        {
                            DisplayInfo display = (DisplayInfo)Marshal.PtrToStructure(
                                IntPtr.Add(displayBuffer, j * displaySize), typeof(DisplayInfo));
                            if ((display.Value & 1) == 0 || string.IsNullOrEmpty(display.Name)) continue;
                            string key = adapter.Bus + ":" + adapter.Device + ":" + adapter.Function
                                + ":" + display.Id.PhysicalDisplay + ":" + display.Name;
                            if (outputs.ContainsKey(key)) continue;
                            Adapter logical;
                            string windowsDevice = adapters.TryGetValue(display.Id.LogicalAdapter, out logical)
                                ? logical.Display : adapter.Display;
                            outputs.Add(key, new Output { Adapter = adapter.Index,
                                Display = display.Id.LogicalDisplay, Name = display.Name, Key = key,
                                WindowsDevice = windowsDevice, Mapped = (display.Value & 2) != 0 });
                        }
                    }
                    finally { if (displayBuffer != IntPtr.Zero) Marshal.FreeHGlobal(displayBuffer); }
                }
                List<Output> result = new List<Output>(outputs.Values);
                result.Sort(delegate(Output a, Output b) { return string.CompareOrdinal(a.Key, b.Key); });
                return result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        // Keep this decision separate from native enumeration so ambiguity,
        // legacy migration and disconnection are covered without a real GPU.
        internal static Output Choose(List<Output> outputs, string key, string legacyName)
        {
            if (!string.IsNullOrEmpty(key))
                return outputs.Find(delegate(Output o) { return string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase); });
            List<Output> candidates = string.IsNullOrEmpty(legacyName) ? outputs :
                outputs.FindAll(delegate(Output o) { return string.Equals(o.Name, legacyName, StringComparison.OrdinalIgnoreCase); });
            if (candidates.Count > 1)
                throw new InvalidOperationException("Multiple physical displays. Run BrightnessCtl.exe list, then select <output-id>.");
            return candidates.Count == 1 ? candidates[0] : null;
        }

        public static Output Find()
        {
            Output found = Choose(Enumerate(), Config.TargetOutput, Config.TargetMonitor);
            Config.ActiveWindowsDevice = found != null && found.Mapped ? found.WindowsDevice : "";
            if (found != null && string.IsNullOrEmpty(Config.TargetOutput)) Config.SaveTargetOutput(found.Key);
            return found;
        }

        public static int Get(Output output, int type)
        {
            int value, defaultValue, min, max, step;
            Check(ADL_Display_Color_Get(output.Adapter, output.Display, type,
                out value, out defaultValue, out min, out max, out step), "read " + output.Name + " color controls");
            return value;
        }

        public static void Set(Output output, int brightness, int contrast)
        {
            // Contrast and its centering compensation together implement an
            // RGB output gain. Physical DDC brightness is never used here.
            Check(ADL_Display_Color_Set(output.Adapter, output.Display, 2, contrast), "set output contrast");
            Check(ADL_Display_Color_Set(output.Adapter, output.Display, 1, brightness), "set output brightness");
            if (Get(output, 1) != brightness || Get(output, 2) != contrast)
                throw new InvalidOperationException("AMD did not retain the requested output color controls.");
        }

        public static string Info()
        {
            Initialize();
            try
            {
                Output output = Find();
                return output == null ? (string.IsNullOrEmpty(Config.TargetOutput) ? "No connected AMD physical output" : "Selected output is disconnected (level saved for reconnection)")
                    : output.Name + " [AMD output brightness=" + Get(output, 1) + ", contrast=" + Get(output, 2) + "]";
            }
            finally { Close(); }
        }
    }
}
