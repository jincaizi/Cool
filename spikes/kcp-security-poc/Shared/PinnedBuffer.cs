using System.Runtime.InteropServices;

namespace KcpSecurityPoc;

/// <summary>
/// Pins a managed byte array for the duration of a Noise-C call and exposes it
/// as a NoiseBuffer (data / size / max_size).
/// </summary>
internal sealed class PinnedBuffer : IDisposable
{
    private GCHandle _handle;

    public PinnedBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Bytes = new byte[capacity];
        _handle = GCHandle.Alloc(Bytes, GCHandleType.Pinned);
    }

    public byte[] Bytes { get; }

    public NoiseInterop.NoiseBuffer Buffer;

    public int Size => (int)Buffer.size;

    public void SetOutput()
    {
        Buffer = new NoiseInterop.NoiseBuffer
        {
            data = _handle.AddrOfPinnedObject(),
            size = UIntPtr.Zero,
            max_size = (UIntPtr)Bytes.Length,
        };
    }

    public void SetInput(int length)
    {
        Buffer = new NoiseInterop.NoiseBuffer
        {
            data = _handle.AddrOfPinnedObject(),
            size = (UIntPtr)length,
            max_size = (UIntPtr)length,
        };
    }

    public void SetInOut(int length, int maxLength)
    {
        Buffer = new NoiseInterop.NoiseBuffer
        {
            data = _handle.AddrOfPinnedObject(),
            size = (UIntPtr)length,
            max_size = (UIntPtr)maxLength,
        };
    }

    public byte[] ToArray(int length) => Bytes.AsSpan(0, length).ToArray();

    public void Dispose()
    {
        if (_handle.IsAllocated)
        {
            _handle.Free();
        }
    }
}
