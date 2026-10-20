using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SharpKVM.Config;
using SharpKVM.Protocol;
using SharpKVM.Utils;

namespace SharpKVM.Network;

public class KvmNetworkServer
{
    private readonly int _port;
    private TcpListener? _listener;
    private volatile bool _isRunning;

    public Action<KvmPacket>? OnPacketReceived;

    public KvmNetworkServer(int port = 24800)
    {
        _port = port;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _isRunning = true;

        Logger.Info($"[Server] Listening on 0.0.0.0:{_port}...");

        while (_isRunning && !ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                Logger.Info($"[Server] Remote client connected from {client.Client.RemoteEndPoint}");

                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error("[Server Error]", ex);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            // 1. Send Local Display Info Handshake to connected Client
            try
            {
                var monitors = MonitorInfo.GetAllMonitors();
                int totalW = 1920;
                int totalH = 1080;
                int count = monitors.Count;

                if (count > 0)
                {
                    int minX = monitors.Min(m => m.Left);
                    int minY = monitors.Min(m => m.Top);
                    int maxX = monitors.Max(m => m.Right);
                    int maxY = monitors.Max(m => m.Bottom);
                    totalW = maxX - minX;
                    totalH = maxY - minY;
                }

                byte[] handshake = KvmPacket.Serialize(PacketType.HandshakeDisplayInfo, totalW, totalH, count);
                await stream.WriteAsync(handshake, ct);
                Logger.Info($"[Server] Sent display handshake: {totalW}x{totalH} ({count} monitors)");
            }
            catch (Exception ex)
            {
                Logger.Error("[Server Handshake Error]", ex);
            }

            // 2. Receive loop
            byte[] buffer = new byte[13];
            while (!ct.IsCancellationRequested)
            {
                int bytesRead = 0;
                while (bytesRead < 13)
                {
                    int read = await stream.ReadAsync(buffer.AsMemory(bytesRead, 13 - bytesRead), ct);
                    if (read == 0) return; // Disconnected
                    bytesRead += read;
                }

                if (KvmPacket.TryDeserialize(buffer, out var packet))
                {
                    OnPacketReceived?.Invoke(packet);
                }
            }
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _listener?.Stop();
    }
}

public class KvmNetworkClient
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly Channel<byte[]> _sendQueue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public Action<KvmPacket>? OnPacketReceived;
    public bool IsConnected => _client != null && _client.Connected;

    public KvmNetworkClient(string host, int port = 24800)
    {
        _host = host;
        _port = port;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        _client = new TcpClient
        {
            NoDelay = true
        };

        Logger.Info($"[Client] Connecting to {_host}:{_port}...");
        await _client.ConnectAsync(_host, _port, ct);
        _stream = _client.GetStream();

        Logger.Info($"[Client] Connected successfully to {_host}:{_port}!");

        _ = SendLoopAsync(ct);
        _ = ReceiveLoopAsync(ct);
    }

    public void QueuePacket(KvmPacket packet)
    {
        byte[] data = KvmPacket.Serialize(packet.Type, packet.Param1, packet.Param2, packet.Param3);
        _sendQueue.Writer.TryWrite(data);
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            var reader = _sendQueue.Reader;
            while (await reader.WaitToReadAsync(ct))
            {
                while (reader.TryRead(out var data))
                {
                    if (_stream != null)
                    {
                        await _stream.WriteAsync(data, ct);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error("[Client Send Error]", ex);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            byte[] buffer = new byte[13];
            while (!ct.IsCancellationRequested && _stream != null)
            {
                int bytesRead = 0;
                while (bytesRead < 13)
                {
                    int read = await _stream.ReadAsync(buffer.AsMemory(bytesRead, 13 - bytesRead), ct);
                    if (read == 0) return; // Disconnected
                    bytesRead += read;
                }

                if (KvmPacket.TryDeserialize(buffer, out var packet))
                {
                    OnPacketReceived?.Invoke(packet);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error("[Client Receive Error]", ex);
        }
    }

    public void Disconnect()
    {
        _stream?.Dispose();
        _client?.Dispose();
    }
}
