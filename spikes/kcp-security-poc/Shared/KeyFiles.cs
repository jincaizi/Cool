using System.Text;

namespace KcpSecurityPoc;

internal static class KeyFiles
{
    public static (byte[] PrivateKey, byte[] PublicKey) GenerateKeyPair()
    {
        int error = NoiseInterop.noise_dhstate_new_by_name(out IntPtr dh, "25519");
        NoiseInterop.Check(error, "noise_dhstate_new_by_name");

        try
        {
            NoiseInterop.Check(NoiseInterop.noise_dhstate_generate_keypair(dh), "generate_keypair");

            var privateKey = new byte[NoiseInterop.KeyLen];
            var publicKey = new byte[NoiseInterop.KeyLen];
            NoiseInterop.Check(
                NoiseInterop.noise_dhstate_get_keypair(
                    dh, privateKey, (UIntPtr)privateKey.Length,
                    publicKey, (UIntPtr)publicKey.Length),
                "get_keypair");
            return (privateKey, publicKey);
        }
        finally
        {
            NoiseInterop.noise_dhstate_free(dh);
        }
    }

    public static void Write(string path, byte[] value) => File.WriteAllBytes(path, value);

    public static byte[] Read(string path)
    {
        byte[] value = File.ReadAllBytes(path);
        if (value.Length != NoiseInterop.KeyLen)
        {
            throw new InvalidDataException(
                path + " must contain exactly " + NoiseInterop.KeyLen + " bytes, got " + value.Length);
        }

        return value;
    }

    public static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();

    public static byte[] FromHex(string hex) => Convert.FromHexString(hex);

    public static string Fingerprint(byte[] publicKey)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(publicKey);
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    public static void GenerateSet(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var (privateKey, publicKey) = GenerateKeyPair();
        Write(Path.Combine(directory, name + ".key"), privateKey);
        Write(Path.Combine(directory, name + ".pub"), publicKey);
        Console.WriteLine(JsonOutput.Json(new
        {
            @event = "keygen",
            name,
            public_key = Hex(publicKey),
            fingerprint = Fingerprint(publicKey),
        }));
    }
}

internal static class BuildInfo
{
    public const string Library = "Noise-C";
    public const string LibraryCommit = "cfe25410979a87391bb9ac8d4d4bef64e9f268c6";
    public const string LibraryLicense = "MIT";
    public const string Protocol = NoiseInterop.ProtocolName;
}
