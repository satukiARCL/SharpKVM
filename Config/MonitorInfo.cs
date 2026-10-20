using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpKVM.Native;

namespace SharpKVM.Config;

public class MonitorInfo
{
    public string Name { get; set; } = "";
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsPrimary { get; set; }

    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public bool Contains(int x, int y)
    {
        return x >= Left && x < Right && y >= Top && y < Bottom;
    }

    public static List<MonitorInfo> GetAllMonitors()
    {
        var monitors = new List<MonitorInfo>();

        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref Win32.RECT lprcMonitor, IntPtr dwData) =>
        {
            var mi = new Win32.MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf<Win32.MONITORINFOEX>();
            if (Win32.GetMonitorInfo(hMonitor, ref mi))
            {
                monitors.Add(new MonitorInfo
                {
                    Name = mi.szDevice.Replace("\0", "").Trim(),
                    Left = mi.rcMonitor.left,
                    Top = mi.rcMonitor.top,
                    Width = mi.rcMonitor.right - mi.rcMonitor.left,
                    Height = mi.rcMonitor.bottom - mi.rcMonitor.top,
                    IsPrimary = (mi.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0
                });
            }
            return true;
        }, IntPtr.Zero);

        return monitors;
    }
}
