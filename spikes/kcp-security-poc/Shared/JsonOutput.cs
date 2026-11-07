using System.Text.Json;

namespace KcpSecurityPoc;

internal static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Json(object value) => JsonSerializer.Serialize(value, Options);

    public static void Line(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value));
        Console.Out.Flush();
    }

    public static void WriteFile(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
        Console.WriteLine("summary_path=" + Path.GetFullPath(path));
    }
}
