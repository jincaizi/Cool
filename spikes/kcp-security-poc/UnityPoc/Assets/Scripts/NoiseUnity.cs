using System;
using System.Runtime.InteropServices;
using System.Text;

namespace KcpPocUnity
{
    // P/Invoke surface over the Noise-C reference implementation (rev 34).
    // libnoise.so is built from github.com/rweather/noise-c at the commit
    // recorded in native/THIRD_PARTY_NOTICES.md.
    internal static class NoiseNative
    {
        public const string ProtocolName = "Noise_XX_25519_ChaChaPoly_BLAKE2s";
        public const int RoleInitiator = 0x5201;
        public const int RoleResponder = 0x5202;
        public const int ActionWriteMessage = 0x4101;
        public const int ActionReadMessage = 0x4102;
        public const int ActionSplit = 0x4104;
        public const int ErrorNone = 0;

        private const string Lib = "noise";

        [StructLayout(LayoutKind.Sequential)]
        public struct NoiseBuffer
        {
            public IntPtr data;
            public UIntPtr size;
            public UIntPtr max_size;
        }

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_init();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_strerror(int err, byte[] buf, UIntPtr size);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_handshakestate_new_by_name(
            out IntPtr state, [MarshalAs(UnmanagedType.LPUTF8Str)] string protocolName, int role);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_handshakestate_free(IntPtr state);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr noise_handshakestate_get_local_keypair_dh(IntPtr state);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr noise_handshakestate_get_remote_public_key_dh(IntPtr state);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_handshakestate_set_prologue(
            IntPtr state, byte[] prologue, UIntPtr prologueLen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_handshakestate_start(IntPtr state);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_handshakestate_get_action(IntPtr state);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_handshakestate_write_message(
            IntPtr state, ref NoiseBuffer message, IntPtr payload);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_handshakestate_read_message(
            IntPtr state, ref NoiseBuffer message, IntPtr payload);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_handshakestate_split(
            IntPtr state, out IntPtr send, out IntPtr receive);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_dhstate_get_public_key(
            IntPtr state, byte[] publicKey, UIntPtr publicKeyLen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern UIntPtr noise_dhstate_get_public_key_length(IntPtr state);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_dhstate_set_keypair_private(
            IntPtr state, byte[] privateKey, UIntPtr privateKeyLen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_dhstate_set_public_key(
            IntPtr state, byte[] publicKey, UIntPtr publicKeyLen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_cipherstate_encrypt_with_ad(
            IntPtr state, byte[] ad, UIntPtr adLen, ref NoiseBuffer buffer);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_cipherstate_decrypt_with_ad(
            IntPtr state, byte[] ad, UIntPtr adLen, ref NoiseBuffer buffer);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int noise_cipherstate_free(IntPtr state);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern UIntPtr noise_cipherstate_get_mac_length(IntPtr state);

        public static string ErrorText(int error)
        {
            byte[] buffer = new byte[256];
            noise_strerror(error, buffer, (UIntPtr)buffer.Length);
            int len = Array.IndexOf(buffer, (byte)0);
            if (len < 0)
            {
                len = buffer.Length;
            }

            return Encoding.ASCII.GetString(buffer, 0, len);
        }

        public static void Check(int error, string operation)
        {
            if (error != ErrorNone)
            {
                throw new InvalidOperationException(
                    operation + " failed: 0x" + error.ToString("x4") + " " + ErrorText(error));
            }
        }
    }

    internal sealed class NoiseSession : IDisposable
    {
        private const int MaxHandshakeMessage = 2048;

        private IntPtr _handshake;
        private IntPtr _send;
        private IntPtr _receive;
        private bool _split;

        public NoiseSession(bool initiator)
        {
            NoiseNative.Check(NoiseNative.noise_init(), "noise_init");
            NoiseNative.Check(
                NoiseNative.noise_handshakestate_new_by_name(
                    out _handshake, NoiseNative.ProtocolName,
                    initiator ? NoiseNative.RoleInitiator : NoiseNative.RoleResponder),
                "noise_handshakestate_new_by_name");
        }

        public void SetLocalStaticKey(byte[] privateKey)
        {
            IntPtr dh = NoiseNative.noise_handshakestate_get_local_keypair_dh(_handshake);
            NoiseNative.Check(
                NoiseNative.noise_dhstate_set_keypair_private(
                    dh, privateKey, (UIntPtr)privateKey.Length),
                "set_keypair_private");
        }

        public void SetRemoteStaticKey(byte[] publicKey)
        {
            IntPtr dh = NoiseNative.noise_handshakestate_get_remote_public_key_dh(_handshake);
            NoiseNative.Check(
                NoiseNative.noise_dhstate_set_public_key(
                    dh, publicKey, (UIntPtr)publicKey.Length),
                "set_public_key");
        }

        public void SetPrologue(byte[] prologue)
        {
            NoiseNative.Check(
                NoiseNative.noise_handshakestate_set_prologue(
                    _handshake, prologue, (UIntPtr)prologue.Length),
                "set_prologue");
        }

        public void Start()
        {
            NoiseNative.Check(NoiseNative.noise_handshakestate_start(_handshake), "start");
        }

        public byte[] WriteHandshakeMessage()
        {
            using (PinnedBuffer buffer = new PinnedBuffer(MaxHandshakeMessage))
            {
                buffer.SetOutput();
                NoiseNative.Check(
                    NoiseNative.noise_handshakestate_write_message(
                        _handshake, ref buffer.Buffer, IntPtr.Zero),
                    "write_message");
                return buffer.ToArray(buffer.Size);
            }
        }

        public void ReadHandshakeMessage(byte[] message)
        {
            using (PinnedBuffer buffer = new PinnedBuffer(Math.Max(message.Length, 1)))
            {
                buffer.SetInput(message.Length);
                Array.Copy(message, buffer.Bytes, message.Length);
                NoiseNative.Check(
                    NoiseNative.noise_handshakestate_read_message(
                        _handshake, ref buffer.Buffer, IntPtr.Zero),
                    "read_message");
            }
        }

        public byte[] GetRemoteStaticKey()
        {
            IntPtr dh = NoiseNative.noise_handshakestate_get_remote_public_key_dh(_handshake);
            int length = (int)NoiseNative.noise_dhstate_get_public_key_length(dh);
            byte[] key = new byte[length];
            NoiseNative.Check(
                NoiseNative.noise_dhstate_get_public_key(dh, key, (UIntPtr)key.Length),
                "get_public_key");
            return key;
        }

        public void Split()
        {
            NoiseNative.Check(
                NoiseNative.noise_handshakestate_split(_handshake, out _send, out _receive),
                "split");
            _split = true;
            NoiseNative.noise_handshakestate_free(_handshake);
            _handshake = IntPtr.Zero;
        }

        public byte[] Encrypt(byte[] plaintext, byte[] associatedData)
        {
            if (!_split)
            {
                throw new InvalidOperationException("handshake not split");
            }

            int macLength = (int)NoiseNative.noise_cipherstate_get_mac_length(_send);
            using (PinnedBuffer buffer = new PinnedBuffer(plaintext.Length + macLength + 16))
            {
                Array.Copy(plaintext, buffer.Bytes, plaintext.Length);
                buffer.SetInOut(plaintext.Length, plaintext.Length + macLength);
                NoiseNative.Check(
                    NoiseNative.noise_cipherstate_encrypt_with_ad(
                        _send, associatedData, (UIntPtr)associatedData.Length, ref buffer.Buffer),
                    "encrypt");
                return buffer.ToArray(buffer.Size);
            }
        }

        public bool TryDecrypt(byte[] ciphertext, byte[] associatedData, out byte[] plaintext, out int error)
        {
            if (!_split)
            {
                throw new InvalidOperationException("handshake not split");
            }

            using (PinnedBuffer buffer = new PinnedBuffer(Math.Max(ciphertext.Length, 1)))
            {
                Array.Copy(ciphertext, buffer.Bytes, ciphertext.Length);
                buffer.SetInOut(ciphertext.Length, ciphertext.Length);
                error = NoiseNative.noise_cipherstate_decrypt_with_ad(
                    _receive, associatedData, (UIntPtr)associatedData.Length, ref buffer.Buffer);
                if (error == NoiseNative.ErrorNone)
                {
                    plaintext = buffer.ToArray(buffer.Size);
                    return true;
                }

                plaintext = new byte[0];
                return false;
            }
        }

        public void Dispose()
        {
            if (_send != IntPtr.Zero)
            {
                NoiseNative.noise_cipherstate_free(_send);
                _send = IntPtr.Zero;
            }

            if (_receive != IntPtr.Zero)
            {
                NoiseNative.noise_cipherstate_free(_receive);
                _receive = IntPtr.Zero;
            }

            if (_handshake != IntPtr.Zero)
            {
                NoiseNative.noise_handshakestate_free(_handshake);
                _handshake = IntPtr.Zero;
            }
        }
    }

    internal sealed class PinnedBuffer : IDisposable
    {
        private GCHandle _handle;

        public PinnedBuffer(int capacity)
        {
            Bytes = new byte[capacity];
            _handle = GCHandle.Alloc(Bytes, GCHandleType.Pinned);
        }

        public byte[] Bytes { get; private set; }

        public NoiseNative.NoiseBuffer Buffer;

        public int Size
        {
            get { return (int)Buffer.size; }
        }

        public void SetOutput()
        {
            Buffer = new NoiseNative.NoiseBuffer();
            Buffer.data = _handle.AddrOfPinnedObject();
            Buffer.size = UIntPtr.Zero;
            Buffer.max_size = (UIntPtr)Bytes.Length;
        }

        public void SetInput(int length)
        {
            Buffer = new NoiseNative.NoiseBuffer();
            Buffer.data = _handle.AddrOfPinnedObject();
            Buffer.size = (UIntPtr)length;
            Buffer.max_size = (UIntPtr)length;
        }

        public void SetInOut(int length, int maxLength)
        {
            Buffer = new NoiseNative.NoiseBuffer();
            Buffer.data = _handle.AddrOfPinnedObject();
            Buffer.size = (UIntPtr)length;
            Buffer.max_size = (UIntPtr)maxLength;
        }

        public byte[] ToArray(int length)
        {
            byte[] result = new byte[length];
            Array.Copy(Bytes, result, length);
            return result;
        }

        public void Dispose()
        {
            if (_handle.IsAllocated)
            {
                _handle.Free();
            }
        }
    }

    internal static class PocCrypto
    {
        public static byte[] FromHex(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }

        public static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            int diff = 0;
            for (int i = 0; i < left.Length; i++)
            {
                diff |= left[i] ^ right[i];
            }

            return diff == 0;
        }

        public static bool BytesEqual(byte[] left, byte[] right)
        {
            return FixedTimeEquals(left, right);
        }

        public static string Fingerprint(byte[] key)
        {
            // FNV-1a 64 for a short, non-security POC fingerprint.
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < key.Length; i++)
            {
                hash ^= key[i];
                hash *= 1099511628211UL;
            }

            return hash.ToString("x16");
        }
    }
}
