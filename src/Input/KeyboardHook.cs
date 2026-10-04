// SPDX-License-Identifier: MIT
// Copyright (c) 2026 principalwater
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace BrightnessCtl
{
    // Windows removes an unresponsive low-level hook silently. Keep its message
    // pump independent of UI, display-driver calls, modal dialogs and DDC.
    internal sealed class KeyboardHook : IDisposable
    {
        private const uint ReinstallMessage = 0x8003;
        private readonly IntPtr _destination;
        private readonly Thread _thread;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private volatile bool _enabled;
        private volatile bool _active;
        private uint _threadId;
        private IntPtr _hook;
        private Native.LowLevelKeyboardProc _callback;

        public bool IsActive { get { return _active; } }

        public KeyboardHook(IntPtr destination, bool enabled)
        {
            _destination = destination;
            _enabled = enabled;
            _thread = new Thread(Run) { IsBackground = true, Name = "BrightnessCtl input" };
            _thread.Start();
            if (!_ready.WaitOne(5000)) throw new InvalidOperationException("Keyboard input thread did not start.");
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!Native.PostThreadMessage(_threadId, ReinstallMessage, IntPtr.Zero, IntPtr.Zero))
                Log.Write("input: cannot update hook: " + Marshal.GetLastWin32Error());
        }

        private void Run()
        {
            _threadId = Native.GetCurrentThreadId();
            Native.MSG message;
            Native.PeekMessage(out message, IntPtr.Zero, 0, 0, 0); // create thread message queue
            _callback = Callback;
            UIntPtr timer = UIntPtr.Zero;
            try
            {
                Rearm();
                timer = Native.SetTimer(IntPtr.Zero, UIntPtr.Zero, 60000, IntPtr.Zero);
                if (timer == UIntPtr.Zero) Log.Write("input: rearm timer failed: " + Marshal.GetLastWin32Error());
                _ready.Set();
                int result;
                while ((result = Native.GetMessage(out message, IntPtr.Zero, 0, 0)) > 0)
                {
                    if (message.Message == ReinstallMessage || message.Message == 0x0113) Rearm();
                    else { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
                }
                if (result < 0) Log.Write("input: GetMessage failed: " + Marshal.GetLastWin32Error());
            }
            catch (Exception ex) { Log.Write("input: thread failed: " + ex.Message); }
            finally
            {
                _ready.Set();
                if (timer != UIntPtr.Zero) Native.KillTimer(IntPtr.Zero, timer);
                Remove();
                GC.KeepAlive(_callback);
            }
        }

        private void Rearm()
        {
            Remove();
            if (!_enabled) return;
            _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _callback, Native.GetModuleHandle(null), 0);
            _active = _hook != IntPtr.Zero;
            if (!_active) Log.Write("input: hook installation failed: " + Marshal.GetLastWin32Error());
        }

        private void Remove()
        {
            if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _active = false;
        }

        private static bool ModifierHeld()
        {
            foreach (int key in new[] { Native.VK_CONTROL, Native.VK_MENU, Native.VK_SHIFT, Native.VK_LWIN, Native.VK_RWIN })
                if ((Native.GetAsyncKeyState(key) & 0x8000) != 0) return true;
            return false;
        }

        private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && _enabled)
            {
                var key = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                if ((key.vkCode == Native.VK_F1 || key.vkCode == Native.VK_F2) && !ModifierHeld())
                {
                    int message = wParam.ToInt32();
                    if (message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN)
                        Native.PostMessage(_destination, Native.WM_KEYSTEP,
                            new IntPtr(key.vkCode == Native.VK_F2 ? 1 : -1), IntPtr.Zero);
                    if (message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN ||
                        message == Native.WM_KEYUP || message == Native.WM_SYSKEYUP) return new IntPtr(1);
                }
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        public void Dispose()
        {
            _enabled = false;
            if (_thread.IsAlive)
            {
                Native.PostThreadMessage(_threadId, 0x0012, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
                if (!_thread.Join(2000)) Log.Write("input: hook thread did not finish within two seconds");
            }
            if (!_thread.IsAlive) _ready.Dispose();
        }
    }
}
