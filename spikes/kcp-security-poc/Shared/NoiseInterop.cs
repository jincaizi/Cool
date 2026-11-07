using System.Runtime.InteropServices;
using System.Text;

namespace KcpSecurityPoc;

/// <summary>
/// Thin P/Invoke surface over the Noise-C reference implementation (rev 34).
/// The native library is built from https://github.com/rweather/noise-c at the
/// commit recorded in native/THIRD_PARTY_NOTICES.md.
/// </summary>
internal static class NoiseInterop
{
    public const string ProtocolName = "Noise_XX_25519_ChaChaPoly_BLAKE2s";

    public const int RoleInitiator = 0x5201; // NOISE_ROLE_INITIATOR
    public const int RoleResponder = 0x5202; // NOISE_ROLE_RESPONDER

    public const int ActionWriteMessage = 0x4101;
    public const int ActionReadMessage = 0x4102;
    public const int ActionFailed = 0x4103;
    public const int ActionSplit = 0x4104;
    public const int ActionComplete = 0x4105;

    public const int ErrorNone = 0;
    public const int ErrorMacFailure = 0x4504;

    public const int KeyLen = 32;

    private const string Lib = "noise";

    [StructLayout(LayoutKind.Sequential)]
    internal struct NoiseBuffer
    {
        public IntPtr data;
        public UIntPtr size;
        public UIntPtr max_size;
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_init();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_strerror(int err, byte[] buf, UIntPtr size);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_handshakestate_new_by_name(
        out IntPtr state, [MarshalAs(UnmanagedType.LPUTF8Str)] string protocolName, int role);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_handshakestate_free(IntPtr state);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr noise_handshakestate_get_local_keypair_dh(IntPtr state);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr noise_handshakestate_get_remote_public_key_dh(IntPtr state);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_handshakestate_set_prologue(
        IntPtr state, byte[] prologue, UIntPtr prologueLen);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_handshakestate_start(IntPtr state);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_handshakestate_get_action(IntPtr state);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_handshakestate_write_message(
        IntPtr state, ref NoiseBuffer message, IntPtr payload);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_handshakestate_read_message(
        IntPtr state, ref NoiseBuffer message, IntPtr payload);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_handshakestate_split(
        IntPtr state, out IntPtr send, out IntPtr receive);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_dhstate_new_by_name(
        out IntPtr state, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_dhstate_free(IntPtr state);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_dhstate_generate_keypair(IntPtr state);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_dhstate_get_keypair(
        IntPtr state, byte[] privateKey, UIntPtr privateKeyLen,
        byte[] publicKey, UIntPtr publicKeyLen);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_dhstate_get_public_key(
        IntPtr state, byte[] publicKey, UIntPtr publicKeyLen);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern UIntPtr noise_dhstate_get_public_key_length(IntPtr state);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_dhstate_set_keypair_private(
        IntPtr state, byte[] privateKey, UIntPtr privateKeyLen);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_dhstate_set_public_key(
        IntPtr state, byte[] publicKey, UIntPtr publicKeyLen);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_cipherstate_encrypt_with_ad(
        IntPtr state, byte[]? ad, UIntPtr adLen, ref NoiseBuffer buffer);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_cipherstate_decrypt_with_ad(
        IntPtr state, byte[]? ad, UIntPtr adLen, ref NoiseBuffer buffer);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int noise_cipherstate_free(IntPtr state);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern UIntPtr noise_cipherstate_get_mac_length(IntPtr state);

    public static string ErrorText(int error)
    {
        var buffer = new byte[256];
        noise_strerror(error, buffer, (UIntPtr)buffer.Length);
        var len = Array.IndexOf(buffer, (byte)0);
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
            throw new NoiseException(operation, error);
        }
    }
}

internal sealed class NoiseException : Exception
{
    public NoiseException(string operation, int error)
        : base(operation + " failed: 0x" + error.ToString("x4") + " " + NoiseInterop.ErrorText(error))
    {
        ErrorCode = error;
    }

    public int ErrorCode { get; }
}
