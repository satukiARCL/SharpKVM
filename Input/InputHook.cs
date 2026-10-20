using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using SharpKVM.Config;
using SharpKVM.Native;
using SharpKVM.Protocol;

namespace SharpKVM.Input;

public class InputHook : IDisposable
{
    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private Win32.LowLevelProc? _mouseProc;
    private Win32.LowLevelProc? _keyboardProc;
    private Thread? _hookThread;
    private volatile bool _isRunning;

    private List<MonitorInfo> _monitors = new();

    private int _anchorX;
    private int _anchorY;
    private bool _isResettingCursor = false;

    public EdgeDirection ActiveEdge { get; set; } = EdgeDirection.Right;
    public bool IsControllingRemote { get; set; } = false;

    public Action<KvmPacket>? OnPacketGenerated;
    public Action<bool>? OnRemoteControlStateChanged;

    public void Start()
    {
        _monitors = MonitorInfo.GetAllMonitors();
        if (_monitors.Count == 0)
        {
            _monitors.Add(new MonitorInfo
            {
                Name = "Default",
                Left = 0,
                Top = 0,
                Width = Win32.GetSystemMetrics(Win32.SM_CXSCREEN),
                Height = Win32.GetSystemMetrics(Win32.SM_CYSCREEN),
                IsPrimary = true
            });
        }

        // Use primary monitor center as anchor
        var prim = _monitors.Find(m => m.IsPrimary) ?? _monitors[0];
        _anchorX = prim.Left + prim.Width / 2;
        _anchorY = prim.Top + prim.Height / 2;

        _isRunning = true;
        _hookThread = new Thread(HookThreadLoop)
        {
            IsBackground = true,
            Name = "InputHookThread"
        };
        _hookThread.Start();
    }

    private void HookThreadLoop()
    {
        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var moduleHandle = Win32.GetModuleHandle(curModule?.ModuleName);

        _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
        _keyboardHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);

        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
        {
            return;
        }

        while (_isRunning && Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }

        if (_mouseHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_keyboardHook);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);

            if (hookStruct.dwExtraInfo == (IntPtr)0x7777)
            {
                return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            }

            int msg = (int)wParam;
            int x = hookStruct.pt.x;
            int y = hookStruct.pt.y;

            if (!IsControllingRemote)
            {
                // Edge detection
                MonitorInfo? currentMonitor = null;
                foreach (var mon in _monitors)
                {
                    if (mon.Contains(x, y))
                    {
                        currentMonitor = mon;
                        break;
                    }
                }

                if (currentMonitor != null)
                {
                    bool hitOuterEdge = false;
                    int targetEntryX = 0;
                    int targetEntryY = 0;

                    switch (ActiveEdge)
                    {
                        case EdgeDirection.Right:
                            if (x >= currentMonitor.Right - 1)
                            {
                                if (!IsAnyMonitorContains(x + 2, y))
                                {
                                    hitOuterEdge = true;
                                    targetEntryX = 10; // Enter from left edge of remote screen
                                    targetEntryY = y;
                                }
                            }
                            break;

                        case EdgeDirection.Left:
                            if (x <= currentMonitor.Left)
                            {
                                if (!IsAnyMonitorContains(x - 2, y))
                                {
                                    hitOuterEdge = true;
                                    targetEntryX = 1910; // Enter from right edge of remote screen
                                    targetEntryY = y;
                                }
                            }
                            break;

                        case EdgeDirection.Top:
                            if (y <= currentMonitor.Top)
                            {
                                if (!IsAnyMonitorContains(x, y - 2))
                                {
                                    hitOuterEdge = true;
                                    targetEntryX = x;
                                    targetEntryY = 1070; // Enter from bottom edge of remote screen
                                }
                            }
                            break;

                        case EdgeDirection.Bottom:
                            if (y >= currentMonitor.Bottom - 1)
                            {
                                if (!IsAnyMonitorContains(x, y + 2))
                                {
                                    hitOuterEdge = true;
                                    targetEntryX = x;
                                    targetEntryY = 10; // Enter from top edge of remote screen
                                }
                            }
                            break;
                    }

                    if (hitOuterEdge)
                    {
                        IsControllingRemote = true;
                        OnRemoteControlStateChanged?.Invoke(true);

                        // Tell remote PC where cursor enters
                        OnPacketGenerated?.Invoke(new KvmPacket
                        {
                            Type = PacketType.SwitchScreen,
                            Param1 = targetEntryX,
                            Param2 = targetEntryY
                        });

                        // Position local cursor to anchor and start relative delta tracking
                        _isResettingCursor = true;
                        Win32.SetCursorPos(_anchorX, _anchorY);
                        return (IntPtr)1;
                    }
                }
            }
            else
            {
                // In remote control mode: Relative Delta calculation
                if (msg == Win32.WM_MOUSEMOVE)
                {
                    if (_isResettingCursor)
                    {
                        if (x == _anchorX && y == _anchorY)
                        {
                            _isResettingCursor = false;
                            return (IntPtr)1;
                        }
                    }

                    int dx = x - _anchorX;
                    int dy = y - _anchorY;

                    if (dx != 0 || dy != 0)
                    {
                        OnPacketGenerated?.Invoke(new KvmPacket
                        {
                            Type = PacketType.MouseMove,
                            Param1 = dx,
                            Param2 = dy
                        });

                        // Reset back to center anchor for next delta
                        _isResettingCursor = true;
                        Win32.SetCursorPos(_anchorX, _anchorY);
                    }

                    return (IntPtr)1;
                }
                else
                {
                    switch (msg)
                    {
                        case Win32.WM_LBUTTONDOWN:
                            OnPacketGenerated?.Invoke(new KvmPacket { Type = PacketType.MouseDown, Param1 = (int)MouseButton.Left });
                            break;
                        case Win32.WM_LBUTTONUP:
                            OnPacketGenerated?.Invoke(new KvmPacket { Type = PacketType.MouseUp, Param1 = (int)MouseButton.Left });
                            break;
                        case Win32.WM_RBUTTONDOWN:
                            OnPacketGenerated?.Invoke(new KvmPacket { Type = PacketType.MouseDown, Param1 = (int)MouseButton.Right });
                            break;
                        case Win32.WM_RBUTTONUP:
                            OnPacketGenerated?.Invoke(new KvmPacket { Type = PacketType.MouseUp, Param1 = (int)MouseButton.Right });
                            break;
                        case Win32.WM_MBUTTONDOWN:
                            OnPacketGenerated?.Invoke(new KvmPacket { Type = PacketType.MouseDown, Param1 = (int)MouseButton.Middle });
                            break;
                        case Win32.WM_MBUTTONUP:
                            OnPacketGenerated?.Invoke(new KvmPacket { Type = PacketType.MouseUp, Param1 = (int)MouseButton.Middle });
                            break;
                        case Win32.WM_MOUSEWHEEL:
                            short delta = (short)((hookStruct.mouseData >> 16) & 0xffff);
                            OnPacketGenerated?.Invoke(new KvmPacket { Type = PacketType.MouseWheel, Param1 = delta });
                            break;
                    }

                    return (IntPtr)1;
                }
            }
        }

        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool IsAnyMonitorContains(int px, int py)
    {
        foreach (var m in _monitors)
        {
            if (m.Contains(px, py)) return true;
        }
        return false;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);

            if (hookStruct.dwExtraInfo == (IntPtr)0x7777)
            {
                return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            int msg = (int)wParam;
            bool isDown = (msg == Win32.WM_KEYDOWN || msg == Win32.WM_SYSKEYDOWN);

            // Escape keys: ScrollLock or Pause
            if (hookStruct.vkCode == 0x91 /* VK_SCROLL */ || hookStruct.vkCode == 0x13 /* VK_PAUSE */)
            {
                if (isDown && IsControllingRemote)
                {
                    IsControllingRemote = false;
                    _isResettingCursor = false;
                    OnRemoteControlStateChanged?.Invoke(false);
                    return (IntPtr)1;
                }
            }

            if (IsControllingRemote)
            {
                OnPacketGenerated?.Invoke(new KvmPacket
                {
                    Type = isDown ? PacketType.KeyDown : PacketType.KeyUp,
                    Param1 = (int)hookStruct.vkCode,
                    Param2 = (int)hookStruct.scanCode,
                    Param3 = (int)hookStruct.flags
                });

                return (IntPtr)1;
            }
        }

        return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    public void Stop()
    {
        _isRunning = false;
        Win32.PostQuitMessage(0);
    }

    public void Dispose()
    {
        Stop();
    }
}
