// SPDX-License-Identifier: MIT
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace BrightnessCtl
{
    // Opt-in interactive-desktop regression check. It captures its own injected
    // F1 events; it never creates a dimmer or changes brightness/configuration.
    internal static class InputTests
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBOARD { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct INPUT { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KEYBOARD Keyboard; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] input, int size);
        private sealed class Receiver : NativeWindow
        {
            public int Count;
            public Receiver() { CreateHandle(new CreateParams { Caption = "BrightnessCtl input regression" }); }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == Native.WM_KEYSTEP) { Count++; return; }
                base.WndProc(ref message);
            }
        }

        private static void Inject()
        {
            var down = new INPUT { Type = 1, Keyboard = new KEYBOARD { Key = Native.VK_F1 } };
            var up = new INPUT { Type = 1, Keyboard = new KEYBOARD { Key = Native.VK_F1, Flags = 2 } };
            if (SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(INPUT))) != 2)
                throw new Exception("SendInput failed: " + Marshal.GetLastWin32Error());
        }

        [STAThread]
        public static int Main()
        {
            var receiver = new Receiver();
            try
            {
                using (var hook = new KeyboardHook(receiver.Handle, true))
                {
                    if (!hook.IsActive) throw new Exception("Hook did not install.");
                    Exception injectionFailure = null;
                    var injector = new Thread(delegate()
                    {
                        try
                        {
                            Thread.Sleep(100);
                            Inject();
                            Thread.Sleep(1200); // exceeds Windows' maximum hook timeout
                            Inject();
                        }
                        catch (Exception ex) { injectionFailure = ex; }
                    });
                    injector.Start();
                    Thread.Sleep(2400); // deliberately block the receiver's UI thread
                    injector.Join();
                    if (injectionFailure != null) throw injectionFailure;
                    Application.DoEvents();
                    if (receiver.Count != 2) throw new Exception("Lost input while UI blocked: " + receiver.Count);
                    Inject();
                    Thread.Sleep(100);
                    Application.DoEvents();
                    if (receiver.Count != 3) throw new Exception("Hook no longer captures after stalled UI.");
                }
                Console.WriteLine("PASS: hook captures before, during and after a 2.4-second UI stall; no brightness changed.");
                return 0;
            }
            finally { receiver.DestroyHandle(); }
        }
    }
}
