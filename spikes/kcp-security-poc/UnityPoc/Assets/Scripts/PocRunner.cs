using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

namespace KcpPocUnity
{
    /// <summary>
    /// Minimal Unity 2022.3 Android IL2CPP client for the ADR-0003 POC.
    /// It is bootstrapped without a scene object, performs one Noise XX
    /// handshake and one AEAD roundtrip against the .NET 8 server, logs the
    /// result for logcat, and quits.
    /// </summary>
    public sealed class PocRunner : MonoBehaviour
    {
        // The emulator guest has no working UDP route to the host in this
        // environment, so the Unity leg uses TCP framed Noise messages through
        // `adb reverse tcp:47120 tcp:47120`. The .NET POC (run-poc.sh) exercises
        // the UDP transport; Noise itself is transport-agnostic.
        private const string Host = "127.0.0.1";
        private const int Port = 47120;
        private const string Tag = "[KcpPoc] ";

        // POC-only X25519 static keys, loaded from Resources/poc_keys.txt so the
        // same throwaway key set is used by the .NET server and this client.
        private string _clientPrivateHex;
        private string _serverPublicHex;

        private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("kcp-poc-ad-v1");

        private volatile bool _done;
        private bool _quitRequested;
        private float _startedAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            GameObject runner = new GameObject("KcpPocRunner");
            UnityEngine.Object.DontDestroyOnLoad(runner);
            runner.AddComponent<PocRunner>();
        }

        private void Start()
        {
            _startedAt = Time.realtimeSinceStartup;
            try
            {
                LoadKeys();
            }
            catch (Exception ex)
            {
                Fail(ex.ToString());
                _done = true;
                return;
            }

            Log("start arch=" + RuntimeInformation.ProcessArchitecture +
                " pointer_bits=" + (IntPtr.Size * 8) +
                " unity=" + Application.unityVersion +
                " protocol=" + NoiseNative.ProtocolName);
            Thread thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Start();
        }

        private void LoadKeys()
        {
            TextAsset asset = Resources.Load<TextAsset>("poc_keys");
            if (asset == null)
            {
                throw new InvalidOperationException("missing Resources/poc_keys.txt");
            }

            foreach (string rawLine in asset.text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("client_private=", StringComparison.Ordinal))
                {
                    _clientPrivateHex = line.Substring("client_private=".Length);
                }
                else if (line.StartsWith("server_public=", StringComparison.Ordinal))
                {
                    _serverPublicHex = line.Substring("server_public=".Length);
                }
            }

            if (string.IsNullOrEmpty(_clientPrivateHex) || string.IsNullOrEmpty(_serverPublicHex))
            {
                throw new InvalidOperationException("poc_keys.txt is incomplete");
            }
        }

        private void Update()
        {
            if (!_done && Time.realtimeSinceStartup - _startedAt > 90f)
            {
                _done = true;
                Log("FAIL overall=timeout");
            }

            if (_done && !_quitRequested)
            {
                _quitRequested = true;
                Log("quit");
                Application.Quit();
            }
        }

        private void Run()
        {
            Stopwatch total = Stopwatch.StartNew();
            try
            {
                byte[] prologue = Encoding.UTF8.GetBytes(
                    "kcp-session-security-poc/v1|" + Host + ":" + Port);

                using (TcpClient client = Connect())
                using (NetworkStream stream = client.GetStream())
                using (NoiseSession session = new NoiseSession(true))
                {
                    stream.ReadTimeout = 10000;
                    stream.WriteTimeout = 10000;

                    session.SetLocalStaticKey(PocCrypto.FromHex(_clientPrivateHex));
                    session.SetRemoteStaticKey(PocCrypto.FromHex(_serverPublicHex));
                    session.SetPrologue(prologue);
                    session.Start();

                    WriteFrame(stream, session.WriteHandshakeMessage());
                    session.ReadHandshakeMessage(ReadFrame(stream));

                    byte[] learned = session.GetRemoteStaticKey();
                    byte[] pinned = PocCrypto.FromHex(_serverPublicHex);
                    bool identityOk = PocCrypto.FixedTimeEquals(learned, pinned);
                    Log("server_identity_ok=" + identityOk +
                        " learned_fingerprint=" + PocCrypto.Fingerprint(learned) +
                        " pinned_fingerprint=" + PocCrypto.Fingerprint(pinned));
                    if (!identityOk)
                    {
                        Fail("server_identity_mismatch");
                        return;
                    }

                    WriteFrame(stream, session.WriteHandshakeMessage());
                    session.Split();
                    Log("handshake_complete elapsed_ms=" + total.Elapsed.TotalMilliseconds.ToString("F2"));

                    byte[] payload = Encoding.UTF8.GetBytes(
                        "UNITY-PING:" + Guid.NewGuid().ToString("N"));
                    WriteFrame(stream, session.Encrypt(payload, AssociatedData));

                    byte[] echo = ReadFrame(stream);
                    byte[] plaintext;
                    int error;
                    bool roundtripOk =
                        session.TryDecrypt(echo, AssociatedData, out plaintext, out error) &&
                        PocCrypto.BytesEqual(plaintext, payload);

                    Log("aead_roundtrip_ok=" + roundtripOk +
                        " payload_len=" + payload.Length +
                        " error=" + (roundtripOk ? "none" : "0x" + error.ToString("x4") + " " + NoiseNative.ErrorText(error)) +
                        " elapsed_ms=" + total.Elapsed.TotalMilliseconds.ToString("F2"));

                    if (roundtripOk)
                    {
                        Log("RESULT PASS handshake+aead_roundtrip");
                    }
                    else
                    {
                        Log("RESULT FAIL aead_roundtrip");
                    }
                }
            }
            catch (Exception ex)
            {
                Fail(ex.ToString());
            }
            finally
            {
                _done = true;
            }
        }

        private static TcpClient Connect()
        {
            IPAddress address = IPAddress.Parse(Host);
            for (int attempt = 0; attempt < 240; attempt++)
            {
                TcpClient client = new TcpClient();
                try
                {
                    client.Connect(address, Port);
                    return client;
                }
                catch (SocketException)
                {
                    client.Close();
                    Thread.Sleep(250);
                }
            }

            throw new TimeoutException("cannot connect to " + Host + ":" + Port);
        }

        private static void WriteFrame(NetworkStream stream, byte[] payload)
        {
            byte[] header = new byte[4];
            header[0] = (byte)(payload.Length >> 24);
            header[1] = (byte)(payload.Length >> 16);
            header[2] = (byte)(payload.Length >> 8);
            header[3] = (byte)payload.Length;
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
            byte[] buffer = new byte[count];
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

        private static void Fail(string message)
        {
            Log("RESULT FAIL " + message);
        }

        private static void Log(string message)
        {
            UnityEngine.Debug.Log(Tag + message);
        }
    }
}
