using System.Security.Cryptography;

namespace KcpSecurityPoc;

/// <summary>
/// Noise XX session wrapper (mutual static-key authentication, ChaCha20-Poly1305
/// AEAD, BLAKE2s hash, X25519 DH). The initiator pins the responder static key
/// and compares the key learned during message 2; the responder does the same
/// with the initiator key learned during message 3.
/// </summary>
internal sealed class NoiseSession : IDisposable
{
    private const int MaxHandshakeMessage = 2048;

    private IntPtr _handshake;
    private IntPtr _send;
    private IntPtr _receive;
    private bool _split;

    public NoiseSession(bool initiator)
    {
        int error = NoiseInterop.noise_handshakestate_new_by_name(
            out _handshake, NoiseInterop.ProtocolName,
            initiator ? NoiseInterop.RoleInitiator : NoiseInterop.RoleResponder);
        NoiseInterop.Check(error, "noise_handshakestate_new_by_name");
        NoiseInterop.Check(NoiseInterop.noise_init(), "noise_init");
    }

    public int Action => NoiseInterop.noise_handshakestate_get_action(_handshake);

    public void SetLocalStaticKey(byte[] privateKey)
    {
        IntPtr dh = NoiseInterop.noise_handshakestate_get_local_keypair_dh(_handshake);
        if (dh == IntPtr.Zero)
        {
            throw new InvalidOperationException("handshake has no local static DH state");
        }

        int error = NoiseInterop.noise_dhstate_set_keypair_private(
            dh, privateKey, (UIntPtr)privateKey.Length);
        NoiseInterop.Check(error, "noise_dhstate_set_keypair_private");
    }

    public void SetRemoteStaticKey(byte[] publicKey)
    {
        IntPtr dh = NoiseInterop.noise_handshakestate_get_remote_public_key_dh(_handshake);
        if (dh == IntPtr.Zero)
        {
            throw new InvalidOperationException("handshake has no remote static DH state");
        }

        int error = NoiseInterop.noise_dhstate_set_public_key(
            dh, publicKey, (UIntPtr)publicKey.Length);
        NoiseInterop.Check(error, "noise_dhstate_set_public_key");
    }

    public void SetPrologue(byte[] prologue)
    {
        int error = NoiseInterop.noise_handshakestate_set_prologue(
            _handshake, prologue, (UIntPtr)prologue.Length);
        NoiseInterop.Check(error, "noise_handshakestate_set_prologue");
    }

    public void Start()
    {
        NoiseInterop.Check(NoiseInterop.noise_handshakestate_start(_handshake), "start");
    }

    public byte[] WriteHandshakeMessage()
    {
        using var buffer = new PinnedBuffer(MaxHandshakeMessage);
        buffer.SetOutput();
        NoiseInterop.Check(
            NoiseInterop.noise_handshakestate_write_message(_handshake, ref buffer.Buffer, IntPtr.Zero),
            "write_message");
        return buffer.ToArray(buffer.Size);
    }

    public void ReadHandshakeMessage(byte[] message)
    {
        using var buffer = new PinnedBuffer(Math.Max(message.Length, 1));
        buffer.SetInput(message.Length);
        message.CopyTo(buffer.Bytes, 0);
        NoiseInterop.Check(
            NoiseInterop.noise_handshakestate_read_message(_handshake, ref buffer.Buffer, IntPtr.Zero),
            "read_message");
    }

    public byte[] GetRemoteStaticKey()
    {
        IntPtr dh = NoiseInterop.noise_handshakestate_get_remote_public_key_dh(_handshake);
        if (dh == IntPtr.Zero)
        {
            throw new InvalidOperationException("handshake has no remote static DH state");
        }

        int length = (int)NoiseInterop.noise_dhstate_get_public_key_length(dh);
        var key = new byte[length];
        NoiseInterop.Check(
            NoiseInterop.noise_dhstate_get_public_key(dh, key, (UIntPtr)key.Length),
            "noise_dhstate_get_public_key");
        return key;
    }

    public void Split()
    {
        NoiseInterop.Check(
            NoiseInterop.noise_handshakestate_split(_handshake, out _send, out _receive),
            "split");
        _split = true;
        NoiseInterop.noise_handshakestate_free(_handshake);
        _handshake = IntPtr.Zero;
    }

    public byte[] Encrypt(byte[] plaintext, byte[] associatedData)
    {
        EnsureSplit();
        int macLength = (int)NoiseInterop.noise_cipherstate_get_mac_length(_send);
        using var buffer = new PinnedBuffer(plaintext.Length + macLength + 16);
        plaintext.CopyTo(buffer.Bytes, 0);
        buffer.SetInOut(plaintext.Length, plaintext.Length + macLength);
        NoiseInterop.Check(
            NoiseInterop.noise_cipherstate_encrypt_with_ad(
                _send, associatedData, (UIntPtr)associatedData.Length, ref buffer.Buffer),
            "encrypt");
        return buffer.ToArray(buffer.Size);
    }

    public bool TryDecrypt(byte[] ciphertext, byte[] associatedData, out byte[] plaintext, out int error)
    {
        EnsureSplit();
        using var buffer = new PinnedBuffer(Math.Max(ciphertext.Length, 1));
        ciphertext.CopyTo(buffer.Bytes, 0);
        buffer.SetInOut(ciphertext.Length, ciphertext.Length);
        error = NoiseInterop.noise_cipherstate_decrypt_with_ad(
            _receive, associatedData, (UIntPtr)associatedData.Length, ref buffer.Buffer);
        if (error == NoiseInterop.ErrorNone)
        {
            plaintext = buffer.ToArray(buffer.Size);
            return true;
        }

        plaintext = Array.Empty<byte>();
        return false;
    }

    public static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private void EnsureSplit()
    {
        if (!_split)
        {
            throw new InvalidOperationException("handshake has not been split yet");
        }
    }

    public void Dispose()
    {
        if (_send != IntPtr.Zero)
        {
            NoiseInterop.noise_cipherstate_free(_send);
            _send = IntPtr.Zero;
        }

        if (_receive != IntPtr.Zero)
        {
            NoiseInterop.noise_cipherstate_free(_receive);
            _receive = IntPtr.Zero;
        }

        if (_handshake != IntPtr.Zero)
        {
            NoiseInterop.noise_handshakestate_free(_handshake);
            _handshake = IntPtr.Zero;
        }
    }
}
