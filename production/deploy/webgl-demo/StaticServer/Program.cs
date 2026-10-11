using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;

namespace Cool.Deploy
{
    /// <summary>
    /// WebGL 面试演示的静态托管 —— 边传边 gzip。
    ///
    ///   StaticServer.exe --root .\site --port 8080                          # 本机自测（免管理员）
    ///   StaticServer.exe --root .\site --port 8080 --prefix http://+:8080/  # 对外（需 URL ACL）
    ///   StaticServer.exe --root .\site --port 8080 --no-gzip                # 对照组：未压缩
    ///
    /// **为什么是 C# 而不是 Node**：目标机（腾讯云 Windows 轻量服务器）上唯一被保证存在的
    /// 运行时是**我们随包发布的**那一个 —— 自包含发布里带着 .NET 运行时，Node/Python/IIS
    /// 都要另装。整套部署的前提是"人类只需要把文件夹拷上去、双击两个脚本"，
    /// 多一个"先去装 Node"的步骤就多一个能在面试前夜坏掉的环节。
    /// （`production/deploy/webgl-demo/static-server.js` 是同一件事的 Node 实现，
    ///   留给有 Node 的开发机做本地对照，不是部署路径。）
    ///
    /// **为什么在服务器压而不是在 Unity 里压**：Unity 侧预压缩（WebGLCompressionFormat.Gzip）
    /// 产出改名文件（WebGL.wasm.gz），服务器就得为每个文件维护"请求 X 给 X.gz + Content-Encoding"
    /// 的映射，任何一处对不上就是 404，或者浏览器把 .gz 字节当 wasm 执行；而且 loader.js /
    /// framework.js / index.html 这些每次都要拉的小文件 Unity 不压 —— 首屏最慢的那段正好漏掉。
    /// 服务器侧压缩的产物形状与 Unity 设置解耦，双重压缩也不可能发生。
    ///
    /// 两个必须做对的细节：
    ///   ① `Content-Length` 发**压缩后**的长度。Unity 的 loader 靠它算进度条百分比；
    ///      不设（分块传输）进度条会一直停在 0，看起来像卡死。所以这里整份压进内存再发，
    ///      而不是 pipe 一个 gzip 流。
    ///   ② `.wasm` 必须回 `application/wasm`：MIME 不对，浏览器就不走
    ///      `WebAssembly.instantiateStreaming`。
    /// </summary>
    public static class Program
    {
        private static string _root = ".";
        private static bool _gzip = true;

        // gzip 结果按 "路径:mtime:size" 缓存 —— 换包即失效，不必重启。
        // Lazy 保证同一文件并发请求也只压一次（loader 会并行拉 wasm/data/framework）。
        private static readonly ConcurrentDictionary<string, Lazy<byte[]>> GzipCache =
            new ConcurrentDictionary<string, Lazy<byte[]>>();

        private static readonly Dictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" },
            { ".js", "text/javascript; charset=utf-8" },
            { ".css", "text/css; charset=utf-8" },
            { ".json", "application/json; charset=utf-8" },
            { ".wasm", "application/wasm" },
            { ".data", "application/octet-stream" },
            { ".ico", "image/x-icon" },
            { ".png", "image/png" },
            { ".jpg", "image/jpeg" },
            { ".svg", "image/svg+xml" },
        };

        // 值得压的类型。图片/图标本身已是压缩格式，再 gzip 只浪费 CPU。
        private static readonly HashSet<string> Compressible = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".html", ".js", ".css", ".json", ".wasm", ".data", ".svg",
        };

        private static int Main(string[] args)
        {
            int port = 8080;
            string? prefix = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--root":
                        if (i + 1 < args.Length) _root = args[++i];
                        break;
                    case "--port":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var p)) { port = p; i++; }
                        break;
                    case "--prefix":
                        if (i + 1 < args.Length) prefix = args[++i];
                        break;
                    case "--no-gzip":
                        _gzip = false;
                        break;
                    default:
                        // 位置参数：端口（与 Server3C.exe 的 `-- 8000` 同款手感）
                        if (int.TryParse(args[i], out var positional)) port = positional;
                        break;
                }
            }

            _root = Path.GetFullPath(_root);
            // 默认只绑 127.0.0.1：localhost 前缀不需要 URL ACL，本机自测免管理员。
            // 对外必须显式传 --prefix http://+:8080/ 并先加 URL ACL —— 与游戏服务端同款要求。
            if (prefix == null) prefix = "http://127.0.0.1:" + port + "/";

            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);

            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                Console.WriteLine("ERROR: 无法监听 " + prefix + " —— " + ex.Message);
                Console.WriteLine("  非 localhost 前缀需要管理员权限，或先加 URL ACL：");
                Console.WriteLine("    netsh http add urlacl url=" + prefix + " user=Everyone");
                return 1;
            }

            Console.WriteLine("StaticServer listening on " + prefix);
            Console.WriteLine("  root = " + _root);
            Console.WriteLine("  gzip = " + (_gzip ? "on" : "off"));
            Console.WriteLine("按 Ctrl+C 停止。");

            while (true)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = listener.GetContext();
                }
                catch (HttpListenerException)
                {
                    break;   // 监听器被关掉
                }
                // 每个请求一条线程：一个 20MB 的 gzip 不能把 wasm/data 的并发请求堵在门口
                _ = Task.Run(() => Handle(ctx));
            }

            listener.Close();
            return 0;
        }

        private static void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            long started = Environment.TickCount64;
            string urlPath = req.Url?.AbsolutePath ?? "/";

            try
            {
                string? file = Resolve(urlPath);
                if (file == null)
                {
                    Console.WriteLine(req.HttpMethod + " " + urlPath + " -> 404");
                    res.StatusCode = 404;
                    return;
                }

                var info = new FileInfo(file);
                long size = info.Length;
                string ext = Path.GetExtension(file);
                string type = Types.TryGetValue(ext, out var t) ? t : "application/octet-stream";
                res.ContentType = type;

                // 单区间 Range。压缩与 Range 天然冲突（没法给压缩流任意偏移），
                // 所以带 Range 的请求一律回原文件 —— 与 nginx 的 gzip 默认行为一致。
                string? rangeHeader = req.Headers["Range"];
                if (rangeHeader != null && TryParseRange(rangeHeader, size, out long from, out long to))
                {
                    byte[] slice = ReadSlice(file, from, to);
                    res.StatusCode = 206;
                    res.AddHeader("Content-Range", "bytes " + from + "-" + to + "/" + size);
                    res.AddHeader("Accept-Ranges", "bytes");
                    res.ContentLength64 = slice.Length;
                    WriteBody(req, res, slice);
                    Console.WriteLine(req.HttpMethod + " " + urlPath + " 206 " + slice.Length + "B range");
                    return;
                }

                string acceptEncoding = req.Headers["Accept-Encoding"] ?? "";
                bool wantsGzip = _gzip && Compressible.Contains(ext)
                    && acceptEncoding.IndexOf("gzip", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!wantsGzip)
                {
                    byte[] raw = File.ReadAllBytes(file);
                    res.StatusCode = 200;
                    res.AddHeader("Accept-Ranges", "bytes");
                    res.ContentLength64 = raw.Length;
                    WriteBody(req, res, raw);
                    Console.WriteLine(req.HttpMethod + " " + urlPath + " 200 " + raw.Length + "B plain "
                        + (Environment.TickCount64 - started) + "ms");
                    return;
                }

                byte[] body = GzipOf(file, info);
                res.StatusCode = 200;
                res.AddHeader("Content-Encoding", "gzip");
                res.AddHeader("Vary", "Accept-Encoding");
                res.AddHeader("Accept-Ranges", "bytes");
                res.ContentLength64 = body.Length;
                WriteBody(req, res, body);
                Console.WriteLine(req.HttpMethod + " " + urlPath + " 200 " + body.Length + "B gzip"
                    + " (" + size + "B raw, " + (size == 0 ? 0 : body.Length * 100 / size) + "%)"
                    + " " + (Environment.TickCount64 - started) + "ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR " + urlPath + ": " + ex.Message);
                try { res.StatusCode = 500; } catch { }
            }
            finally
            {
                try { res.Close(); } catch { }
            }
        }

        private static void WriteBody(HttpListenerRequest req, HttpListenerResponse res, byte[] body)
        {
            if (req.HttpMethod == "HEAD") return;
            res.OutputStream.Write(body, 0, body.Length);
        }

        /// <summary>把 URL 路径解析成 root 内的真实文件；目录取 index.html；越界/不存在回 null。</summary>
        private static string? Resolve(string urlPath)
        {
            string rel = Uri.UnescapeDataString(urlPath).TrimStart('/', '\\');
            string full = Path.GetFullPath(Path.Combine(_root, rel));
            if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) return null;   // 目录穿越
            if (Directory.Exists(full)) full = Path.Combine(full, "index.html");
            return File.Exists(full) ? full : null;
        }

        private static byte[] ReadSlice(string file, long from, long to)
        {
            using (var fs = File.OpenRead(file))
            {
                fs.Seek(from, SeekOrigin.Begin);
                var buf = new byte[to - from + 1];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = fs.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return read == buf.Length ? buf : buf[..read];
            }
        }

        private static bool TryParseRange(string header, long size, out long from, out long to)
        {
            from = to = 0;
            if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
            string spec = header.Substring(6).Split(',')[0].Trim();
            int dash = spec.IndexOf('-');
            if (dash < 0) return false;

            string left = spec.Substring(0, dash);
            string right = spec.Substring(dash + 1);

            if (left.Length == 0)
            {
                if (!long.TryParse(right, out long suffix) || suffix <= 0) return false;
                from = Math.Max(0, size - suffix);
                to = size - 1;
            }
            else
            {
                if (!long.TryParse(left, out from)) return false;
                to = right.Length == 0 || !long.TryParse(right, out to) || to >= size ? size - 1 : to;
            }
            return from <= to && from < size;
        }

        private static byte[] GzipOf(string file, FileInfo info)
        {
            string key = file + ":" + info.LastWriteTimeUtc.Ticks + ":" + info.Length;
            var lazy = GzipCache.GetOrAdd(key, _ => new Lazy<byte[]>(() => Compress(file)));
            return lazy.Value;
        }

        private static byte[] Compress(string file)
        {
            using (var output = new MemoryStream())
            {
                using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    using (var input = File.OpenRead(file))
                    {
                        input.CopyTo(gz, 1 << 20);
                    }
                }
                return output.ToArray();
            }
        }
    }
}
