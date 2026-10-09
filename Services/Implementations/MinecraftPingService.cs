using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class MinecraftPingService : IMinecraftPingService
{
    private const int DefaultConnectTimeoutMs = 2500;
    private const int DefaultReadTimeoutMs = 2500;

    public async Task<ServerPingResult> PingServerAsync(string serverAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serverAddress))
        {
            return new ServerPingResult(null, null, null);
        }

        string host = serverAddress.Trim();
        int port = 25565;

        int colonIndex = host.LastIndexOf(':');
        if (colonIndex > 0 && int.TryParse(host.AsSpan(colonIndex + 1), out int parsedPort))
        {
            port = parsedPort;
            host = host.Substring(0, colonIndex);
        }

        using var tcp = new TcpClient();
        tcp.ReceiveTimeout = DefaultReadTimeoutMs;
        tcp.SendTimeout = DefaultReadTimeoutMs;

        int pingMs = -1;
        var sw = Stopwatch.StartNew();

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DefaultConnectTimeoutMs);

            await tcp.ConnectAsync(host, port, timeoutCts.Token);
            sw.Stop();
            pingMs = (int)sw.ElapsedMilliseconds;
        }
        catch
        {
            return new ServerPingResult(null, null, null);
        }

        int? playersOnline = null;
        int? playersMax = null;

        try
        {
            using var readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readTimeoutCts.CancelAfter(DefaultReadTimeoutMs);

            using var stream = tcp.GetStream();
            stream.ReadTimeout = DefaultReadTimeoutMs;
            stream.WriteTimeout = DefaultReadTimeoutMs;

            // 1. Handshake Packet (ID 0x00)
            byte[] handshakeData = CreateHandshakePacket(host, port);
            await stream.WriteAsync(handshakeData, 0, handshakeData.Length, readTimeoutCts.Token);

            // 2. Status Request Packet (ID 0x00, empty body)
            byte[] statusRequest = new byte[] { 0x01, 0x00 };
            await stream.WriteAsync(statusRequest, 0, statusRequest.Length, readTimeoutCts.Token);
            await stream.FlushAsync(readTimeoutCts.Token);

            // 3. Response: Packet Length (VarInt) + Packet ID (VarInt) + JSON String Length (VarInt) + UTF-8 JSON
            int packetLength = await ReadVarIntAsync(stream, readTimeoutCts.Token);
            if (packetLength > 0)
            {
                int packetId = await ReadVarIntAsync(stream, readTimeoutCts.Token);
                if (packetId == 0x00)
                {
                    int jsonLength = await ReadVarIntAsync(stream, readTimeoutCts.Token);
                    if (jsonLength > 0 && jsonLength <= 65536)
                    {
                        byte[] buffer = new byte[jsonLength];
                        int totalRead = 0;
                        while (totalRead < jsonLength)
                        {
                            int read = await stream.ReadAsync(buffer, totalRead, jsonLength - totalRead, readTimeoutCts.Token);
                            if (read <= 0) break;
                            totalRead += read;
                        }

                        if (totalRead == jsonLength)
                        {
                            string json = Encoding.UTF8.GetString(buffer);
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("players", out var playersProp))
                            {
                                if (playersProp.TryGetProperty("online", out var onlineProp))
                                {
                                    playersOnline = onlineProp.GetInt32();
                                }
                                if (playersProp.TryGetProperty("max", out var maxProp))
                                {
                                    playersMax = maxProp.GetInt32();
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // SLP не ответил, но TCP ping замерен
        }

        return new ServerPingResult(pingMs, playersOnline, playersMax);
    }

    private static byte[] CreateHandshakePacket(string host, int port)
    {
        using var ms = new MemoryStream();
        // Packet ID 0x00
        WriteVarInt(ms, 0x00);
        // Protocol version 763 (Minecraft 1.20.1)
        WriteVarInt(ms, 763);
        // Server address
        byte[] hostBytes = Encoding.UTF8.GetBytes(host);
        WriteVarInt(ms, hostBytes.Length);
        ms.Write(hostBytes, 0, hostBytes.Length);
        // Port (ushort, big-endian)
        ms.WriteByte((byte)((port >> 8) & 0xFF));
        ms.WriteByte((byte)(port & 0xFF));
        // Next State: 1 (status)
        WriteVarInt(ms, 1);

        byte[] body = ms.ToArray();
        using var packetMs = new MemoryStream();
        WriteVarInt(packetMs, body.Length);
        packetMs.Write(body, 0, body.Length);
        return packetMs.ToArray();
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        while ((value & -128) != 0)
        {
            stream.WriteByte((byte)((value & 127) | 128));
            value = (int)((uint)value >> 7);
        }
        stream.WriteByte((byte)value);
    }

    private static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken ct)
    {
        int numRead = 0;
        int result = 0;
        byte[] readBuffer = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(readBuffer, 0, 1, ct);
            if (read <= 0)
            {
                throw new EndOfStreamException();
            }

            byte current = readBuffer[0];
            int value = current & 127;
            result |= value << (7 * numRead);

            numRead++;
            if (numRead > 5)
            {
                throw new InvalidOperationException("VarInt is too big");
            }

            if ((current & 128) == 0)
            {
                break;
            }
        }

        return result;
    }
}
