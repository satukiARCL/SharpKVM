using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using SharpKVM.Config;
using SharpKVM.Native;
using SharpKVM.Protocol;
using SharpKVM.Utils;

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

    // Virtual remote cursor tracking for seamless edge-return
    private int _remoteCursorX;
    private int _remoteCursorY;
    private DateTime _lastSwitchTime = DateTime.MinValue;

    public EdgeDirection ActiveEdge { get; set; } = EdgeDirection.Right;
    public bool IsControllingRemote { get; set; } = false;

    public int RemoteScreenWidth { get; set; } = 1920;
    public int RemoteScreenHeight { get; set; } = 1080;

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
            Logger.Error($"[InputHook] Failed to set hooks. Error: {Marshal.GetLastWin32Error()}");
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

            // Prevent rapid bouncing between screens (300ms cooldown)
            bool canSwitch = (DateTime.UtcNow - _lastSwitchTime).TotalMilliseconds > 300;

            if (!IsControllingRemote)
            {
                if (canSwitch)
                {
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
                                if (x >= currentMonitor.Right - 1 && !IsAnyMonitorContains(x + 2, y))
                                {
                                    hitOuterEdge = true;
                                    targetEntryX = 10;
                                    targetEntryY = y;
                                }
                                break;

                            case EdgeDirection.Left:
                                if (x <= currentMonitor.Left && !IsAnyMonitorContains(x - 2, y))
                                {
                                    hitOuterEdge = true;
                                    targetEntryX = RemoteScreenWidth - 10;
                                    targetEntryY = y;
                                }
                                break;

                            case EdgeDirection.Top:
                                if (y <= currentMonitor.Top && !IsAnyMonitorContains(x, y - 2))
                                {
                                    hitOuterEdge = true;
                                    targetEntryX = x;
                                    targetEntryY = RemoteScreenHeight - 10;
                                }
                                break;

                            case EdgeDirection.Bottom:
                                if (y >= currentMonitor.Bottom - 1 && !IsAnyMonitorContains(x, y + 2))
                                {
                                    hitOuterEdge = true;
                                    targetEntryX = x;
                                    targetEntryY = 10;
                                }
                                break;
                        }

                        if (hitOuterEdge)
                        {
                            IsControllingRemote = true;
                            _lastSwitchTime = DateTime.UtcNow;
                            _remoteCursorX = targetEntryX;
                            _remoteCursorY = targetEntryY;

                            OnRemoteControlStateChanged?.Invoke(true);

                            OnPacketGenerated?.Invoke(new KvmPacket
                            {
                                Type = PacketType.SwitchScreen,
                                Param1 = targetEntryX,
                                Param2 = targetEntryY
                            });

                            _isResettingCursor = true;
                            Win32.SetCursorPos(_anchorX, _anchorY);
                            return (IntPtr)1;
                        }
                    }
                }
            }
            else
            {
                // In remote control mode
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
                        _remoteCursorX += dx;
                        _remoteCursorY += dy;

                        // Check if cursor returns to local machine (Edge Return)
                        bool returnToLocal = false;
                        int returnLocalX = x;
                        int returnLocalY = y;

                        if (canSwitch)
                        {
                            switch (ActiveEdge)
                            {
                                case EdgeDirection.Right:
                                    // Moving leftwards past the remote left edge returns to local right edge
                                    if (_remoteCursorX <= 0)
                                    {
                                        returnToLocal = true;
                                        var m = _monitors.Find(m => m.Contains(x, _remoteCursorY)) ?? _monitors[0];
                                        returnLocalX = m.Right - 20;
                                        returnLocalY = _remoteCursorY;
                                    }
                                    break;

                                case EdgeDirection.Left:
                                    // Moving rightwards past remote right edge returns to local left edge
                                    if (_remoteCursorX >= RemoteScreenWidth)
                                    {
                                        returnToLocal = true;
                                        var m = _monitors.Find(m => m.Contains(x, _remoteCursorY)) ?? _monitors[0];
                                        returnLocalX = m.Left + 20;
                                        returnLocalY = _remoteCursorY;
                                    }
                                    break;

                                case EdgeDirection.Top:
                                    // Moving downwards past remote bottom edge returns to local top edge
                                    if (_remoteCursorY >= RemoteScreenHeight)
                                    {
                                        returnToLocal = true;
                                        returnLocalX = _remoteCursorX;
                                        var m = _monitors.Find(m => m.Contains(_remoteCursorX, y)) ?? _monitors[0];
                                        returnLocalY = m.Top + 20;
                                    }
                                    break;

                                case EdgeDirection.Bottom:
                                    // Moving upwards past remote top edge returns to local bottom edge
                                    if (_remoteCursorY <= 0)
                                    {
                                        returnToLocal = true;
                                        returnLocalX = _remoteCursorX;
                                        var m = _monitors.Find(m => m.Contains(_remoteCursorX, y)) ?? _monitors[0];
                                        returnLocalY = m.Bottom - 20;
                                    }
                                    break;
                            }
                        }

                        if (returnToLocal)
                        {
                            IsControllingRemote = false;
                            _isResettingCursor = false;
                            _lastSwitchTime = DateTime.UtcNow;

                            OnRemoteControlStateChanged?.Invoke(false);
                            Win32.SetCursorPos(returnLocalX, returnLocalY);
                            return (IntPtr)1;
                        }

                        // Send delta to remote
                        OnPacketGenerated?.Invoke(new KvmPacket
                        {
                            Type = PacketType.MouseMove,
                            Param1 = dx,
                            Param2 = dy
                        });

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

            // Escape hotkeys
            if (hookStruct.vkCode == 0x91 /* VK_SCROLL */ || hookStruct.vkCode == 0x13 /* VK_PAUSE */)
            {
                if (isDown && IsControllingRemote)
                {
                    IsControllingRemote = false;
                    _isResettingCursor = false;
                    _lastSwitchTime = DateTime.UtcNow;
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
