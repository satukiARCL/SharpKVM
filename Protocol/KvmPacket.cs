using System;
using System.IO;

namespace SharpKVM.Protocol;

public enum PacketType : byte
{
    Ping = 0,
    Pong = 1,
    MouseMove = 2,
    MouseDown = 3,
    MouseUp = 4,
    MouseWheel = 5,
    KeyDown = 6,
    KeyUp = 7,
    SwitchScreen = 8,
    HandshakeDisplayInfo = 9,
}

public enum MouseButton : byte
{
    Left = 1,
    Right = 2,
    Middle = 3,
}

public readonly struct KvmPacket
{
    public PacketType Type { get; init; }
    public int Param1 { get; init; }
    public int Param2 { get; init; }
    public int Param3 { get; init; }

    public static byte[] Serialize(PacketType type, int p1 = 0, int p2 = 0, int p3 = 0)
    {
        // 13 bytes fixed packet: [Type (1B)][Param1 (4B)][Param2 (4B)][Param3 (4B)]
        byte[] buffer = new byte[13];
        buffer[0] = (byte)type;
        BitConverter.TryWriteBytes(buffer.AsSpan(1, 4), p1);
        BitConverter.TryWriteBytes(buffer.AsSpan(5, 4), p2);
        BitConverter.TryWriteBytes(buffer.AsSpan(9, 4), p3);
        return buffer;
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> span, out KvmPacket packet)
    {
        if (span.Length < 13)
        {
            packet = default;
            return false;
        }

        packet = new KvmPacket
        {
            Type = (PacketType)span[0],
            Param1 = BitConverter.ToInt32(span.Slice(1, 4)),
            Param2 = BitConverter.ToInt32(span.Slice(5, 4)),
            Param3 = BitConverter.ToInt32(span.Slice(9, 4)),
        };
        return true;
    }
}
