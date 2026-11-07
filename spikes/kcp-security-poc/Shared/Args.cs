namespace KcpSecurityPoc;

internal sealed class Args
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

    public Args(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                _values[key] = args[++i];
            }
            else
            {
                _values[key] = null;
            }
        }
    }

    public bool Has(string key) => _values.ContainsKey(key);

    public string? Get(string key) => _values.TryGetValue(key, out string? value) ? value : null;

    public string Require(string key)
    {
        string? value = Get(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("missing required argument " + key);
        }

        return value;
    }

    public int GetInt(string key, int fallback)
    {
        string? value = Get(key);
        return value is null ? fallback : int.Parse(value);
    }
}

internal static class PocProtocol
{
    public static byte[] Prologue(string hostname, int port)
    {
        return System.Text.Encoding.UTF8.GetBytes(
            "kcp-session-security-poc/v1|" + hostname + ":" + port);
    }

    public static byte[] AssociatedData()
    {
        return System.Text.Encoding.UTF8.GetBytes("kcp-poc-ad-v1");
    }
}
