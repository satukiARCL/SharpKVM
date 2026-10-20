using System;
using System.IO;
using System.Text.Json;

namespace SharpKVM.Config;

public enum EdgeDirection
{
    Right,
    Left,
    Top,
    Bottom
}

public class DisplayLayoutConfig
{
    public string RemoteHost { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 24800;

    public double LocalCanvasX { get; set; } = 150;
    public double LocalCanvasY { get; set; } = 120;

    public double RemoteCanvasX { get; set; } = 350;
    public double RemoteCanvasY { get; set; } = 120;

    public EdgeDirection CurrentEdge { get; set; } = EdgeDirection.Right;

    // Virtual space boundary bounds for remote screen
    public double RemoteVirtualMinX { get; set; } = 1920;
    public double RemoteVirtualMaxX { get; set; } = 3840;
    public double RemoteVirtualMinY { get; set; } = 0;
    public double RemoteVirtualMaxY { get; set; } = 1080;

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SharpKVM",
        "layout_config.json");

    public static DisplayLayoutConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<DisplayLayoutConfig>(json) ?? new DisplayLayoutConfig();
            }
        }
        catch { }
        return new DisplayLayoutConfig();
    }

    public void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }

    public void RecalculateEdge()
    {
        double dx = RemoteCanvasX - LocalCanvasX;
        double dy = RemoteCanvasY - LocalCanvasY;

        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            CurrentEdge = dx >= 0 ? EdgeDirection.Right : EdgeDirection.Left;
        }
        else
        {
            CurrentEdge = dy >= 0 ? EdgeDirection.Bottom : EdgeDirection.Top;
        }
    }
}
