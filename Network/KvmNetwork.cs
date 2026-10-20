using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SharpKVM.Input;
using SharpKVM.Protocol;

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

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[Server] Listening on 0.0.0.0:{_port} (TCP_NODELAY)...");
        Console.ResetColor();

        while (_isRunning && !ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                Console.WriteLine($"[Server] Remote client connected from {client.Client.RemoteEndPoint}");

                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server Error] {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            byte[] buffer = new byte[13]; // Fixed 13 bytes packet size

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

        Console.WriteLine($"[Client] Connecting to {_host}:{_port}...");
        await _client.ConnectAsync(_host, _port, ct);
        _stream = _client.GetStream();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Client] Connected successfully to {_host}:{_port}!");
        Console.ResetColor();

        _ = SendLoopAsync(ct);
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
            Console.WriteLine($"[Client Send Error] {ex.Message}");
        }
    }

    public void Disconnect()
    {
        _stream?.Dispose();
        _client?.Dispose();
    }
}
