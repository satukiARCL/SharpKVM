using System;
using System.Runtime.InteropServices;
using SharpKVM.Native;
using SharpKVM.Protocol;

namespace SharpKVM.Input;

public static class InputSimulator
{
    public static void MoveMouse(int x, int y)
    {
        Win32.SetCursorPos(x, y);
    }

    public static void MouseButtonEvent(MouseButton button, bool isDown)
    {
        uint flag = button switch
        {
            MouseButton.Left => isDown ? Win32.MOUSEEVENTF_LEFTDOWN : Win32.MOUSEEVENTF_LEFTUP,
            MouseButton.Right => isDown ? Win32.MOUSEEVENTF_RIGHTDOWN : Win32.MOUSEEVENTF_RIGHTUP,
            MouseButton.Middle => isDown ? Win32.MOUSEEVENTF_MIDDLEDOWN : Win32.MOUSEEVENTF_MIDDLEUP,
            _ => 0
        };

        if (flag == 0) return;

        var input = new Win32.INPUT
        {
            type = Win32.INPUT_MOUSE,
            u = new Win32.InputUnion
            {
                mi = new Win32.MOUSEINPUT
                {
                    dwFlags = flag,
                    time = 0,
                    dwExtraInfo = (IntPtr)0x7777 // Loopback/Echo prevention marker
                }
            }
        };

        Win32.SendInput(1, [input], Marshal.SizeOf<Win32.INPUT>());
    }

    public static void MouseWheel(int delta)
    {
        var input = new Win32.INPUT
        {
            type = Win32.INPUT_MOUSE,
            u = new Win32.InputUnion
            {
                mi = new Win32.MOUSEINPUT
                {
                    dwFlags = Win32.MOUSEEVENTF_WHEEL,
                    mouseData = (uint)delta,
                    time = 0,
                    dwExtraInfo = (IntPtr)0x7777
                }
            }
        };

        Win32.SendInput(1, [input], Marshal.SizeOf<Win32.INPUT>());
    }

    public static void KeyEvent(ushort vkCode, ushort scanCode, bool isDown, uint flags)
    {
        uint dwFlags = 0;
        if (!isDown) dwFlags |= Win32.KEYEVENTF_KEYUP;
        if ((flags & 1) != 0) dwFlags |= Win32.KEYEVENTF_EXTENDEDKEY;

        var input = new Win32.INPUT
        {
            type = Win32.INPUT_KEYBOARD,
            u = new Win32.InputUnion
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = vkCode,
                    wScan = scanCode,
                    dwFlags = dwFlags,
                    time = 0,
                    dwExtraInfo = (IntPtr)0x7777
                }
            }
        };

        Win32.SendInput(1, [input], Marshal.SizeOf<Win32.INPUT>());
    }
}
