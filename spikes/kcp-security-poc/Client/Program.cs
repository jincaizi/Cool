using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace KcpSecurityPoc;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return Run(new Args(args));
        }
        catch (NoiseException ex)
        {
            Console.Error.WriteLine("noise_error=" + ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int Run(Args args)
    {
        if (args.Has("--gen-keys"))
        {
            KeyFiles.GenerateSet(args.Require("--gen-keys"), args.Get("--name") ?? "client");
            return 0;
        }

        string host = args.Get("--host") ?? "127.0.0.1";
        int port = args.GetInt("--port", 47102);
        string mode = args.Get("--mode") ?? "main";
        string keyDir = args.Get("--key-dir") ?? "keys";
        string serverPublicPath = args.Get("--server-pub") ?? Path.Combine(keyDir, "server.pub");
        string resultPath = args.Get("--result") ?? "client-result.json";
        int receiveTimeoutMs = args.GetInt("--receive-timeout-ms", 3000);
        string transport = args.Get("--transport") ?? "udp";

        if (transport == "tcp")
        {
            return RunTcp(host, port, mode, keyDir, serverPublicPath, resultPath, receiveTimeoutMs);
        }

        byte[] clientPrivate = KeyFiles.Read(Path.Combine(keyDir, "client.key"));
        byte[] pinnedServerPublic = KeyFiles.Read(serverPublicPath);
        byte[] prologue = PocProtocol.Prologue(host, port);
        byte[] associatedData = PocProtocol.AssociatedData();

        IPAddress address = Resolve(host);
        var serverEndpoint = new IPEndPoint(address, port);

        using var socket = new UdpClient();
        socket.Client.ReceiveTimeout = receiveTimeoutMs;

        var summary = new Dictionary<string, object?>
        {
            ["role"] = "initiator",
            ["mode"] = mode,
            ["protocol"] = BuildInfo.Protocol,
            ["library"] = BuildInfo.Library,
            ["library_commit"] = BuildInfo.LibraryCommit,
            ["host"] = host,
            ["port"] = port,
            ["pinned_server_fingerprint"] = KeyFiles.Fingerprint(pinnedServerPublic),
            ["handshake_completed"] = false,
            ["server_identity_ok"] = false,
            ["roundtrip_ok"] = false,
            ["tampered_sent"] = 0,
            ["replay_sent"] = 0,
            ["unexpected_response"] = false,
        };

        var total = Stopwatch.StartNew();
        using var session = new NoiseSession(initiator: true);
        session.SetLocalStaticKey(clientPrivate);
        session.SetRemoteStaticKey(pinnedServerPublic);
        session.SetPrologue(prologue);
        session.Start();

        byte[] msg1 = session.WriteHandshakeMessage();
        byte[]? msg2 = null;
        for (int attempt = 0; attempt < 40 && msg2 is null; attempt++)
        {
            socket.Send(msg1, msg1.Length, serverEndpoint);
            socket.Client.ReceiveTimeout = 250;
            msg2 = TryReceive(socket);
        }

        if (msg2 is null)
        {
            throw new TimeoutException("timed out waiting for handshake_msg2");
        }

        socket.Client.ReceiveTimeout = receiveTimeoutMs;
        session.ReadHandshakeMessage(msg2);

        byte[] learnedServerPublic = session.GetRemoteStaticKey();
        bool identityOk = NoiseSession.FixedTimeEquals(learnedServerPublic, pinnedServerPublic);
        summary["server_identity_ok"] = identityOk;
        summary["learned_server_fingerprint"] = KeyFiles.Fingerprint(learnedServerPublic);

        if (!identityOk)
        {
            summary["handshake_completed"] = false;
            summary["error"] = "server static key mismatch";
            summary["elapsed_ms"] = total.Elapsed.TotalMilliseconds;
            JsonOutput.Line(new
            {
                @event = "server_identity_rejected",
                learned = KeyFiles.Fingerprint(learnedServerPublic),
                pinned = KeyFiles.Fingerprint(pinnedServerPublic),
            });
            JsonOutput.WriteFile(resultPath, summary);
            return 3;
        }

        if (mode == "wrong-identity")
        {
            // The caller supplied a wrong pin; a matching key means the test is void.
            summary["error"] = "wrong-identity mode unexpectedly matched the pinned key";
            summary["elapsed_ms"] = total.Elapsed.TotalMilliseconds;
            JsonOutput.WriteFile(resultPath, summary);
            return 4;
        }

        byte[] msg3 = session.WriteHandshakeMessage();
        socket.Send(msg3, msg3.Length, serverEndpoint);
        session.Split();
        summary["handshake_completed"] = true;
        summary["handshake_ms"] = total.Elapsed.TotalMilliseconds;
        JsonOutput.Line(new
        {
            @event = "handshake_complete",
            elapsed_ms = total.Elapsed.TotalMilliseconds,
            server_fingerprint = KeyFiles.Fingerprint(learnedServerPublic),
        });

        byte[] payload = Encoding.UTF8.GetBytes("PING:" + Guid.NewGuid().ToString("N"));
        byte[] first = session.Encrypt(payload, associatedData);
        socket.Send(first, first.Length, serverEndpoint);

        byte[] echo = Receive(socket, "aead_echo");
        bool roundtripOk = session.TryDecrypt(echo, associatedData, out byte[] echoed, out int echoError)
            && echoed.AsSpan().SequenceEqual(payload);
        summary["roundtrip_ok"] = roundtripOk;
        JsonOutput.Line(new
        {
            @event = "aead_roundtrip",
            ok = roundtripOk,
            error = roundtripOk ? null : "0x" + echoError.ToString("x4") + " " + NoiseInterop.ErrorText(echoError),
            payload_len = payload.Length,
        });

        // Tamper: fresh ciphertext with one MAC byte flipped.
        byte[] tampered = session.Encrypt(Encoding.UTF8.GetBytes("TAMPER"), associatedData);
        tampered[^1] ^= 0xFF;
        socket.Send(tampered, tampered.Length, serverEndpoint);
        summary["tampered_sent"] = 1;

        // Replay: the exact first ciphertext (nonce 0) after the server advanced to nonce 1.
        socket.Send(first, first.Length, serverEndpoint);
        summary["replay_sent"] = 1;

        socket.Client.ReceiveTimeout = 500;
        // Expected: rejected packets produce no response.
        bool unexpected = TryReceive(socket) is not null;
        summary["unexpected_response"] = unexpected;
        summary["elapsed_ms"] = total.Elapsed.TotalMilliseconds;
        JsonOutput.Line(new
        {
            @event = "rejection_probes_sent",
            tampered = true,
            replayed = true,
            unexpected_response = unexpected,
        });
        JsonOutput.WriteFile(resultPath, summary);

        return roundtripOk && !unexpected ? 0 : 6;
    }

    private static int RunTcp(
        string host, int port, string mode, string keyDir, string serverPublicPath,
        string resultPath, int receiveTimeoutMs)
    {
        byte[] clientPrivate = KeyFiles.Read(Path.Combine(keyDir, "client.key"));
        byte[] pinnedServerPublic = KeyFiles.Read(serverPublicPath);
        byte[] prologue = PocProtocol.Prologue(host, port);
        byte[] associatedData = PocProtocol.AssociatedData();

        var summary = new Dictionary<string, object?>
        {
            ["role"] = "initiator",
            ["mode"] = mode,
            ["transport"] = "tcp",
            ["protocol"] = BuildInfo.Protocol,
            ["library"] = BuildInfo.Library,
            ["library_commit"] = BuildInfo.LibraryCommit,
            ["host"] = host,
            ["port"] = port,
            ["pinned_server_fingerprint"] = KeyFiles.Fingerprint(pinnedServerPublic),
            ["handshake_completed"] = false,
            ["server_identity_ok"] = false,
            ["roundtrip_ok"] = false,
            ["tampered_sent"] = 0,
            ["replay_sent"] = 0,
            ["unexpected_response"] = false,
        };

        var total = Stopwatch.StartNew();
        using var client = new TcpClient();
        client.Connect(Resolve(host), port);
        using NetworkStream stream = client.GetStream();
        stream.ReadTimeout = receiveTimeoutMs;

        using var session = new NoiseSession(initiator: true);
        session.SetLocalStaticKey(clientPrivate);
        session.SetRemoteStaticKey(pinnedServerPublic);
        session.SetPrologue(prologue);
        session.Start();

        WriteFrame(stream, session.WriteHandshakeMessage());
        session.ReadHandshakeMessage(ReadFrame(stream));

        byte[] learnedServerPublic = session.GetRemoteStaticKey();
        bool identityOk = NoiseSession.FixedTimeEquals(learnedServerPublic, pinnedServerPublic);
        summary["server_identity_ok"] = identityOk;
        summary["learned_server_fingerprint"] = KeyFiles.Fingerprint(learnedServerPublic);
        if (!identityOk)
        {
            summary["error"] = "server static key mismatch";
            summary["elapsed_ms"] = total.Elapsed.TotalMilliseconds;
            JsonOutput.Line(new
            {
                @event = "server_identity_rejected",
                learned = KeyFiles.Fingerprint(learnedServerPublic),
                pinned = KeyFiles.Fingerprint(pinnedServerPublic),
            });
            JsonOutput.WriteFile(resultPath, summary);
            return 3;
        }

        if (mode == "wrong-identity")
        {
            summary["error"] = "wrong-identity mode unexpectedly matched the pinned key";
            summary["elapsed_ms"] = total.Elapsed.TotalMilliseconds;
            JsonOutput.WriteFile(resultPath, summary);
            return 4;
        }

        WriteFrame(stream, session.WriteHandshakeMessage());
        session.Split();
        summary["handshake_completed"] = true;
        summary["handshake_ms"] = total.Elapsed.TotalMilliseconds;
        JsonOutput.Line(new
        {
            @event = "handshake_complete",
            transport = "tcp",
            elapsed_ms = total.Elapsed.TotalMilliseconds,
            server_fingerprint = KeyFiles.Fingerprint(learnedServerPublic),
        });

        byte[] payload = Encoding.UTF8.GetBytes("PING:" + Guid.NewGuid().ToString("N"));
        byte[] first = session.Encrypt(payload, associatedData);
        WriteFrame(stream, first);

        byte[] echo = ReadFrame(stream);
        bool roundtripOk = session.TryDecrypt(echo, associatedData, out byte[] echoed, out int echoError)
            && echoed.AsSpan().SequenceEqual(payload);
        summary["roundtrip_ok"] = roundtripOk;
        JsonOutput.Line(new
        {
            @event = "aead_roundtrip",
            ok = roundtripOk,
            error = roundtripOk ? null : "0x" + echoError.ToString("x4") + " " + NoiseInterop.ErrorText(echoError),
            payload_len = payload.Length,
        });

        byte[] tampered = session.Encrypt(Encoding.UTF8.GetBytes("TAMPER"), associatedData);
        tampered[^1] ^= 0xFF;
        WriteFrame(stream, tampered);
        summary["tampered_sent"] = 1;

        WriteFrame(stream, first);
        summary["replay_sent"] = 1;

        stream.ReadTimeout = 500;
        bool unexpected = false;
        try
        {
            _ = ReadFrame(stream);
            unexpected = true;
        }
        catch (IOException)
        {
            // Expected: rejected frames produce no response.
        }

        summary["unexpected_response"] = unexpected;
        summary["elapsed_ms"] = total.Elapsed.TotalMilliseconds;
        JsonOutput.Line(new
        {
            @event = "rejection_probes_sent",
            tampered = true,
            replayed = true,
            unexpected_response = unexpected,
        });
        JsonOutput.WriteFile(resultPath, summary);

        return roundtripOk && !unexpected ? 0 : 6;
    }

    private static void WriteFrame(NetworkStream stream, byte[] payload)
    {
        byte[] header =
        {
            (byte)(payload.Length >> 24),
            (byte)(payload.Length >> 16),
            (byte)(payload.Length >> 8),
            (byte)payload.Length,
        };
        stream.Write(header, 0, header.Length);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    private static byte[] ReadFrame(NetworkStream stream)
    {
        byte[] header = ReadExactly(stream, 4);
        int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
        return ReadExactly(stream, length);
    }

    private static byte[] ReadExactly(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }

        return buffer;
    }

    private static IPEndPoint AnyEndpoint = new(IPAddress.Any, 0);

    private static byte[]? TryReceive(UdpClient socket)
    {
        try
        {
            return socket.Receive(ref AnyEndpoint);
        }
        catch (SocketException ex) when (
            ex.SocketErrorCode is SocketError.TimedOut or SocketError.ConnectionReset)
        {
            return null;
        }
    }

    private static byte[] Receive(UdpClient socket, string stage)
    {
        return TryReceive(socket) ?? throw new TimeoutException("timed out waiting for " + stage);
    }

    private static IPAddress Resolve(string host)
    {
        if (IPAddress.TryParse(host, out IPAddress? address))
        {
            return address;
        }

        IPAddress[] addresses = Dns.GetHostAddresses(host);
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException("cannot resolve " + host);
        }

        return addresses[0];
    }
}
