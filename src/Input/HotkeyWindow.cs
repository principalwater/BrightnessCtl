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
    internal sealed class HotkeyWindow : NativeWindow
    {
        public event Action<int> Pressed;
        public event Action<IntPtr> RawInput;
        public event Action TaskbarCreated;
        public event Action<int> KeyStep;
        public Func<int, int, int> BrightnessCommand;

        private readonly uint _taskbarCreatedMsg;

        public HotkeyWindow()
        {
            // Explorer broadcasts this when the notification area appears -
            // at sign-in it can arrive after we do, and again if Explorer restarts.
            _taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");
            CreateHandle(new CreateParams { Caption = Native.ControlWindow });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_KEYSTEP && KeyStep != null)
            {
                KeyStep(m.WParam.ToInt32());
                return;
            }
            if (m.Msg == Native.WM_BRIGHTNESS && BrightnessCommand != null)
            {
                m.Result = new IntPtr(BrightnessCommand(m.WParam.ToInt32(), m.LParam.ToInt32()));
                return;
            }
            if (m.Msg == Native.WM_HOTKEY && Pressed != null)
                Pressed((int)m.WParam);
            else if (m.Msg == Native.WM_INPUT && RawInput != null)
                RawInput(m.LParam);
            else if (_taskbarCreatedMsg != 0 && m.Msg == (int)_taskbarCreatedMsg && TaskbarCreated != null)
                TaskbarCreated();
            base.WndProc(ref m);
        }
    }
}
