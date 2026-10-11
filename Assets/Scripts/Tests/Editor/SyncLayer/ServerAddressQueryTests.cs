using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    /// <summary>
    /// Task 13：`?host=&lt;IP&gt;` 的解析层。纯字符串逻辑，本地可测——不需要真连接，
    /// 也不需要出包（出包后的页面 URL 是浏览器边界，见 `production/verify/t13/` 的实测）。
    ///
    /// **规则（每种输入都必须有确定行为，畸形一律忽略、绝不抛）**：
    /// | URL | host | port |
    /// |---|---|---|
    /// | `?host=1.2.3.4` | `1.2.3.4` | 传入的默认端口（演示里是 8000） |
    /// | `?host=1.2.3.4:9000` | `1.2.3.4` | `9000` |
    /// | 无该参数 / 畸形 | 不覆盖（`TryParse` 返回 false，调用方保持既有行为） | — |
    /// </summary>
    public class ServerAddressQueryTests
    {
        private const int DefaultPort = 8000;

        [Test]
        public void test_server_address_query_parses_bare_host_with_default_port()
        {
            // Arrange & Act
            bool ok = ServerAddressQuery.TryParse(
                "http://1.2.3.4:8080/index.html?host=1.2.3.4", DefaultPort, out string host, out int port);

            // Assert
            Assert.IsTrue(ok, "?host=1.2.3.4 必须被识别");
            Assert.AreEqual("1.2.3.4", host, "host 原样带出");
            Assert.AreEqual(DefaultPort, port, "不带端口 → 回落到默认端口（链接自洽，不吃上一次的存档）");
        }

        [Test]
        public void test_server_address_query_parses_host_with_explicit_port()
        {
            // Arrange & Act
            bool ok = ServerAddressQuery.TryParse(
                "http://1.2.3.4:8080/index.html?host=1.2.3.4:9000", DefaultPort, out string host, out int port);

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual("1.2.3.4", host);
            Assert.AreEqual(9000, port, "带端口就用 URL 里的端口");
        }

        [Test]
        public void test_server_address_query_fills_unspecified_port_from_argument()
        {
            // Arrange & Act：默认端口是参数，不是硬编码的 8000（否则手机那条 8002 的链接没法复用同一份规则）
            bool ok = ServerAddressQuery.TryParse(
                "http://x/index.html?host=192.168.1.4", 8002, out string host, out int port);

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual("192.168.1.4", host);
            Assert.AreEqual(8002, port);
        }

        [Test]
        public void test_server_address_query_parses_host_among_other_params()
        {
            // Arrange & Act：投递链接会同时带 ?touch=1（手机演示），顺序不可控
            bool ok = ServerAddressQuery.TryParse(
                "http://192.168.1.4:8080/index.html?touch=1&host=192.168.1.4:8002&x=2",
                DefaultPort, out string host, out int port);

            // Assert
            Assert.IsTrue(ok, "夹在 ?touch=1 之间也要认");
            Assert.AreEqual("192.168.1.4", host);
            Assert.AreEqual(8002, port);

            // 顺序反过来同样认
            ok = ServerAddressQuery.TryParse(
                "http://x/index.html?host=10.0.0.5&touch=1", DefaultPort, out host, out port);
            Assert.IsTrue(ok);
            Assert.AreEqual("10.0.0.5", host);
            Assert.AreEqual(DefaultPort, port);
        }

        [Test]
        public void test_server_address_query_returns_false_when_absent()
        {
            // Arrange & Act & Assert：**无参数时行为必须与现在逐字等价** —— 全部返回 false，
            // 调用方于是原样走"存档 → 默认 127.0.0.1:8000"的老路
            Assert.IsFalse(ServerAddressQuery.TryParse(null, DefaultPort, out _, out _), "null URL 不覆盖");
            Assert.IsFalse(ServerAddressQuery.TryParse("", DefaultPort, out _, out _), "空串不覆盖");
            Assert.IsFalse(ServerAddressQuery.TryParse("http://1.2.3.4:8080/index.html", DefaultPort, out _, out _),
                "无查询串不覆盖");
            Assert.IsFalse(ServerAddressQuery.TryParse("http://x/index.html?touch=1", DefaultPort, out _, out _),
                "只有别的参数不覆盖");
        }

        [Test]
        public void test_server_address_query_ignores_malformed_values()
        {
            // Arrange：畸形一律**忽略**（退回既有行为），绝不抛——现场多打一个字符的代价不能是"打不开游戏"
            var malformed = new[]
            {
                "http://x/index.html?host=",                 // 空值（串尾）
                "http://x/index.html?host=&touch=1",         // 空值（后跟别的参数）
                "http://x/index.html?host=:9000",            // 只有端口没有地址
                "http://x/index.html?host=:",                // 空地址空端口
                "http://x/index.html?host=1.2.3.4:abc",      // 端口不是数字
                "http://x/index.html?host=1.2.3.4:0",        // 端口越界（下界）
                "http://x/index.html?host=1.2.3.4:65536",    // 端口越界（上界）
                "http://x/index.html?host=1.2.3.4:8000abc",  // 端口拖尾巴
                "http://x/index.html?host=1.2.3.4 bad",      // 地址里有空格
                "http://x/index.html?host=a/b",              // 地址里有斜杠
            };

            // Act & Assert
            foreach (var url in malformed)
            {
                Assert.IsFalse(ServerAddressQuery.TryParse(url, DefaultPort, out string host, out int port), url);
                Assert.IsTrue(string.IsNullOrEmpty(host), url + " → 失败时不得留下半个 host");
                Assert.AreEqual(0, port, url + " → 失败时不得留下半个 port");
            }
        }

        [Test]
        public void test_server_address_query_stops_value_at_separators()
        {
            // Arrange & Act：值到 `&` 或 `#` 为止（否则 host 会拖着后面的参数一起进连接串）
            bool ok = ServerAddressQuery.TryParse(
                "http://x/index.html?host=1.2.3.4:9000#frag", DefaultPort, out string host, out int port);

            // Assert
            Assert.IsTrue(ok, "锚点 # 后面的内容不算地址");
            Assert.AreEqual("1.2.3.4", host);
            Assert.AreEqual(9000, port);
        }

        [Test]
        public void test_server_address_query_trims_surrounding_whitespace()
        {
            // Arrange & Act：首尾空白容忍（复制粘贴的常见残留），串内空白仍算畸形
            bool ok = ServerAddressQuery.TryParse(
                "http://x/index.html?host= 1.2.3.4 ", DefaultPort, out string host, out int port);

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual("1.2.3.4", host);
            Assert.AreEqual(DefaultPort, port);
        }
    }
}
