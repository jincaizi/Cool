using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

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
            KeyFiles.GenerateSet(args.Require("--gen-keys"), args.Get("--name") ?? "server");
            return 0;
        }

        string hostname = args.Get("--hostname") ?? "127.0.0.1";
        int port = args.GetInt("--port", 47102);
        string transport = args.Get("--transport") ?? "udp";
        string keyDir = args.Get("--key-dir") ?? "keys";
        string clientPublicPath = args.Get("--client-pub") ?? Path.Combine(keyDir, "client.pub");
        string resultPath = args.Get("--result") ?? "server-result.json";
        int handshakeTimeoutMs = args.GetInt("--handshake-timeout-ms", 3000);
        int idleTimeoutMs = args.GetInt("--idle-timeout-ms", 2000);

        if (transport == "tcp")
        {
            return RunTcp(hostname, port, keyDir, clientPublicPath, resultPath,
                handshakeTimeoutMs, idleTimeoutMs);
        }

        byte[] serverPrivate = KeyFiles.Read(Path.Combine(keyDir, "server.key"));
        byte[] expectedClientPublic = KeyFiles.Read(clientPublicPath);
        byte[] prologue = PocProtocol.Prologue(hostname, port);
        byte[] associatedData = PocProtocol.AssociatedData();

        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        socket.Client.ReceiveTimeout = handshakeTimeoutMs;

        var summary = new Dictionary<string, object?>
        {
            ["role"] = "responder",
            ["transport"] = "udp",
            ["protocol"] = BuildInfo.Protocol,
            ["library"] = BuildInfo.Library,
            ["library_commit"] = BuildInfo.LibraryCommit,
            ["hostname"] = hostname,
            ["port"] = port,
            ["handshake_completed"] = false,
            ["client_static_ok"] = false,
            ["decrypt_ok"] = 0,
            ["decrypt_failures"] = 0,
            ["error_codes"] = new List<string>(),
            ["client_static_fingerprint"] = null,
        };

        JsonOutput.Line(new
        {
            @event = "listening",
            port,
            hostname,
            protocol = BuildInfo.Protocol,
            library = BuildInfo.Library,
            library_commit = BuildInfo.LibraryCommit,
        });

        var total = Stopwatch.StartNew();
        using var session = new NoiseSession(initiator: false);
        session.SetLocalStaticKey(serverPrivate);
        session.SetPrologue(prologue);
        session.Start();

        var remote = new IPEndPoint(IPAddress.Any, 0);
        try
        {
            byte[] msg1 = Receive(socket, ref remote);
            session.ReadHandshakeMessage(msg1);

            byte[] msg2 = session.WriteHandshakeMessage();
            socket.Send(msg2, msg2.Length, remote);

            byte[] msg3 = Receive(socket, ref remote);
            session.ReadHandshakeMessage(msg3);
        }
        catch (SocketException ex) when (
            ex.SocketErrorCode is SocketError.TimedOut or SocketError.ConnectionReset)
        {
            summary["error"] = "handshake timeout or reset";
            summary["elapsed_ms"] = total.Elapsed.TotalMilliseconds;
            JsonOutput.Line(new { @event = "handshake_timeout" });
            JsonOutput.WriteFile(resultPath, summary);
            return 2;
        }

        byte[] clientStatic = session.GetRemoteStaticKey();
        bool clientStaticOk = NoiseSession.FixedTimeEquals(clientStatic, expectedClientPublic);
        summary["client_static_ok"] = clientStaticOk;
        summary["client_static_fingerprint"] = KeyFiles.Fingerprint(clientStatic);
        if (!clientStaticOk)
        {
            summary["error"] = "client static key mismatch";
            JsonOutput.Line(new
            {
                @event = "client_identity_rejected",
                actual = KeyFiles.Fingerprint(clientStatic),
                expected = KeyFiles.Fingerprint(expectedClientPublic),
            });
            JsonOutput.WriteFile(resultPath, summary);
            return 4;
        }

        session.Split();
        summary["handshake_completed"] = true;
        summary["handshake_ms"] = total.Elapsed.TotalMilliseconds;
        JsonOutput.Line(new
        {
            @event = "handshake_complete",
            elapsed_ms = total.Elapsed.TotalMilliseconds,
            client_static_fingerprint = KeyFiles.Fingerprint(clientStatic),
        });

        socket.Client.ReceiveTimeout = idleTimeoutMs;
        int decryptOk = 0;
        int decryptFailures = 0;
        var errorCodes = new List<string>();

        while (true)
        {
            byte[] packet;
            try
            {
                packet = Receive(socket, ref remote);
            }
            catch (SocketException ex) when (
                ex.SocketErrorCode is SocketError.TimedOut or SocketError.ConnectionReset)
            {
                break;
            }

            if (session.TryDecrypt(packet, associatedData, out byte[] plaintext, out int error))
            {
                decryptOk++;
                JsonOutput.Line(new { @event = "decrypt_ok", len = plaintext.Length, index = decryptOk - 1 });
                byte[] echo = session.Encrypt(plaintext, associatedData);
                socket.Send(echo, echo.Length, remote);
            }
            else
            {
                decryptFailures++;
                string code = "0x" + error.ToString("x4") + " " + NoiseInterop.ErrorText(error);
                errorCodes.Add(code);
                JsonOutput.Line(new { @event = "decrypt_rejected", error = code });
            }
        }

        summary["decrypt_ok"] = decryptOk;
        summary["decrypt_failures"] = decryptFailures;
        summary["error_codes"] = errorCodes;
        summary["elapsed_ms"] = total.Elapsed.TotalMilliseconds;
        JsonOutput.WriteFile(resultPath, summary);

        return decryptOk > 0 ? 0 : 5;
    }

    /// <summary>
    /// TCP framing mode: only used for the Android emulator leg, where the
    /// emulator's guest UDP routing is unavailable and adb reverse provides a
    /// TCP path to the host. Noise itself is transport-agnostic; the UDP path
    /// remains the production-relevant one and is exercised by run-poc.sh.
    /// </summary>
    private static int RunTcp(
        string hostname, int port, string keyDir, string clientPublicPath,
        string resultPath, int handshakeTimeoutMs, int idleTimeoutMs)
    {
        byte[] serverPrivate = KeyFiles.Read(Path.Combine(keyDir, "server.key"));
        byte[] expectedClientPublic = KeyFiles.Read(clientPublicPath);
        byte[] prologue = PocProtocol.Prologue(hostname, port);
        byte[] associatedData = PocProtocol.AssociatedData();

        var summary = new Dictionary<string, object?>
        {
            ["role"] = "responder",
            ["transport"] = "tcp",
            ["protocol"] = BuildInfo.Protocol,
            ["library"] = BuildInfo.Library,
            ["library_commit"] = BuildInfo.LibraryCommit,
            ["hostname"] = hostname,
            ["port"] = port,
            ["handshake_completed"] = false,
            ["client_static_ok"] = false,
            ["decrypt_ok"] = 0,
            ["decrypt_failures"] = 0,
            ["error_codes"] = new List<string>(),
            ["client_static_fingerprint"] = null,
        };

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        JsonOutput.Line(new
        {
            @event = "listening",
            transport = "tcp",
            port,
            hostname,
            protocol = BuildInfo.Protocol,
            library = BuildInfo.Library,
            library_commit = BuildInfo.LibraryCommit,
        });

        var total = Stopwatch.StartNew();
        using TcpClient client = listener.AcceptTcpClient();
        listener.Stop();
        using NetworkStream stream = client.GetStream();
        stream.ReadTimeout = handshakeTimeoutMs;

        using var session = new NoiseSession(initiator: false);
        session.SetLocalStaticKey(serverPrivate);
        session.SetPrologue(prologue);
        session.Start();

        try
        {
            byte[] msg1 = ReadFrame(stream);
            session.ReadHandshakeMessage(msg1);
            WriteFrame(stream, session.WriteHandshakeMessage());
            byte[] msg3 = ReadFrame(stream);
            session.ReadHandshakeMessage(msg3);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            summary["error"] = "handshake timeout or disconnect";
            summary["elapsed_ms"] = total.Elapsed.TotalMilliseconds;
            JsonOutput.Line(new { @event = "handshake_timeout" });
            JsonOutput.WriteFile(resultPath, summary);
            return 2;
        }

        byte[] clientStatic = session.GetRemoteStaticKey();
        bool clientStaticOk = NoiseSession.FixedTimeEquals(clientStatic, expectedClientPublic);
        summary["client_static_ok"] = clientStaticOk;
        summary["client_static_fingerprint"] = KeyFiles.Fingerprint(clientStatic);
        if (!clientStaticOk)
        {
            summary["error"] = "client static key mismatch";
            JsonOutput.WriteFile(resultPath, summary);
            return 4;
        }

        session.Split();
        summary["handshake_completed"] = true;
        summary["handshake_ms"] = total.Elapsed.TotalMilliseconds;
        JsonOutput.Line(new
        {
            @event = "handshake_complete",
            transport = "tcp",
            elapsed_ms = total.Elapsed.TotalMilliseconds,
            client_static_fingerprint = KeyFiles.Fingerprint(clientStatic),
        });

        stream.ReadTimeout = idleTimeoutMs;
        int decryptOk = 0;
        int decryptFailures = 0;
        var errorCodes = new List<string>();

        while (true)
        {
            byte[] frame;
            try
            {
                frame = ReadFrame(stream);
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException)
            {
                break;
            }

            if (session.TryDecrypt(frame, associatedData, out byte[] plaintext, out int error))
            {
                decryptOk++;
                JsonOutput.Line(new { @event = "decrypt_ok", len = plaintext.Length, index = decryptOk - 1 });
                WriteFrame(stream, session.Encrypt(plaintext, associatedData));
            }
            else
            {
                decryptFailures++;
                string code = "0x" + error.ToString("x4") + " " + NoiseInterop.ErrorText(error);
                errorCodes.Add(code);
                JsonOutput.Line(new { @event = "decrypt_rejected", error = code });
            }
        }

        summary["decrypt_ok"] = decryptOk;
        summary["decrypt_failures"] = decryptFailures;
        summary["error_codes"] = errorCodes;
        summary["elapsed_ms"] = total.Elapsed.TotalMilliseconds;
        JsonOutput.WriteFile(resultPath, summary);

        return decryptOk > 0 ? 0 : 5;
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

    private static byte[] Receive(UdpClient socket, ref IPEndPoint remote)
    {
        return socket.Receive(ref remote);
    }
}
