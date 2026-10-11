using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// Task 13：`?host=&lt;IP&gt;` 的解析层与它的**作用范围**。纯字符串逻辑，本地可测——不需要真连接，
    /// 也不需要出包（出包后的页面 URL 是浏览器边界，见 `production/verify/t13/` 的实测）。
    ///
    /// **规则（每种输入都必须有确定行为，畸形一律忽略、绝不抛）**：
    /// | URL | host | port |
    /// |---|---|---|
    /// | `?host=1.2.3.4` | `1.2.3.4` | 传入的默认端口（演示里是 8000） |
    /// | `?host=1.2.3.4:9000` | `1.2.3.4` | `9000` |
    /// | 无该参数 / 畸形 | 不覆盖（`TryParse` 返回 false，调用方保持既有行为） | — |
    ///
    /// **作用范围**：这个参数只喂**预填**（`NetworkModeService.PrefillHost/PrefillPort` 填进选人面板的
    /// 地址框）；连接的端点由框里的值决定（`test_confirm_entry_uses_the_box_value_not_the_url` 钉住的就是它）。
    /// </summary>
    public class ServerAddressQueryTests
    {
        private const int DefaultPort = 8000;

        private const string HostKey = "net.host";
        private const string PortKey = "net.port";
        private const string UserKey = "net.username";

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

        /// <summary>
        /// 修复轮：**手打的（框里的）值压过 URL 参数**。
        ///
        /// 这条语义在修复前不成立：链接带 `?host=` → 面板预填 → 操作者改成别的地址 → 确认
        /// （手打值落进存档）→ 而 `Connect` 又解析了一次 URL，于是**拨的是链接里的地址，
        /// 框里（和他记忆里）却是另一个**，只有失败浮层说真话。正是本任务要消灭的"端点悄悄不对"。
        ///
        /// 现在：URL 的作用止于预填，连接只读确认时写进去的存档值。
        /// 编辑器里页面 URL 恒空串（浏览器边界），所以这里把 URL **当参数注入**来钉住这条规则。
        /// </summary>
        [Test]
        public void test_confirm_entry_uses_the_box_value_not_the_url()
        {
            const string url = "http://192.168.1.4:8080/index.html?host=1.2.3.4";
            const int boxPort = 8019;

            try
            {
                NetworkModeService.SaveConnection("10.0.0.9", "9000", "Player");  // 上一次留下的存档

                // ① 预填：链接的值压过存档（面试官打开链接，框里就是链接给的地址）
                Assert.AreEqual("1.2.3.4", NetworkModeService.PrefillHost(url, NetworkModeService.LoadHost()),
                    "预填吃 URL");
                Assert.AreEqual(8000, NetworkModeService.PrefillPort(url, NetworkModeService.LoadPort()),
                    "只给地址不给端口 → 预填默认 8000");

                // ② 操作者把框改成手打值并点「确认」（= 面板把框里的值写进存档）。
                //    对照 ① 的 1.2.3.4：如果连接再解析一次 URL，下面这两行就会是 1.2.3.4 / 8000。
                NetworkModeService.SaveConnection("127.0.0.1", boxPort.ToString(), "Player");

                // ③ 连接入口（= `ClientNetworkHost.Connect` 读的那两个）拿到的就是**框里的值**
                Assert.AreEqual("127.0.0.1", NetworkModeService.LoadHost(), "手打 host 压过 URL");
                Assert.AreEqual(boxPort, NetworkModeService.LoadPort(), "手打 port 压过 URL");

                // ④ 而**无参数时**预填与连接完全一致（改动前的老行为）
                Assert.AreEqual(NetworkModeService.LoadHost(),
                    NetworkModeService.PrefillHost("http://192.168.1.4:8080/index.html", NetworkModeService.LoadHost()),
                    "没有 ?host= 时预填落回存档");
                Assert.IsFalse(ServerAddressQuery.TryParse("http://192.168.1.4:8080/index.html", DefaultPort,
                    out _, out _), "无参数不解析出任何覆盖");
            }
            finally
            {
                // PlayerPrefs 是编辑器级全局状态（与 CharacterSelectionServiceTests 同款收尾）
                PlayerPrefs.DeleteKey(HostKey);
                PlayerPrefs.DeleteKey(PortKey);
                PlayerPrefs.DeleteKey(UserKey);
            }
        }

        /// <summary>
        /// **修复的回归测试**：连接的端点决议（`NetworkModeService.ResolveConnectEndpoint`，
        /// `ClientNetworkHost.Connect` 的唯一调用方）里**没有 URL**。
        ///
        /// 为什么要有它（上一版那条测试不够）：上一版的用例只断言了 `LoadHost/LoadPort` 与
        /// `Prefill*`，而旧缺陷在 `Connect` —— 有人把"URL 优先"加回决议时，那条测试**照样全绿**
        /// （`test-standards.md`：修 bug 必须配一条"抓得住原缺陷"的回归测试）。
        /// 这里把**链接**一起喂给决议函数：旧语义（URL 优先）下它会返回 `1.2.3.4:8000`，
        /// 于是这几条断言必红 —— 判据是**两套语义给出不同答案**，不是"跑得过"。
        /// </summary>
        [Test]
        public void test_connect_endpoint_ignores_url_and_keeps_the_box_value()
        {
            const string url = "http://192.168.1.4:8080/index.html?host=1.2.3.4:8000";

            try
            {
                // Arrange：存档里是操作者在框里确认过的值（他手改过，端口 8019）
                NetworkModeService.SaveConnection("127.0.0.1", "8019", "Player");

                // Act：把**链接**也一起喂进去（旧语义会在这里返回 1.2.3.4 / 8000）
                var (host, port) = NetworkModeService.ResolveConnectEndpoint(url, null, null);

                // Assert：连的是框里的值
                Assert.AreEqual("127.0.0.1", host, "连接端点来自框（存档），不是链接");
                Assert.AreEqual(8019, port, "端口同理：链接里的 8000 不得压过框里的 8019");

                // 显式入参仍然最优先（测试与代码路径覆盖用），URL 再大也压不过它
                var (overridden, overriddenPort) = NetworkModeService.ResolveConnectEndpoint(url, "10.0.0.5", 9001);
                Assert.AreEqual("10.0.0.5", overridden, "显式入参 > 一切");
                Assert.AreEqual(9001, overriddenPort);

                // 无参数、无存档时仍是那对老默认值（改动前逐字等价）
                PlayerPrefs.DeleteKey(HostKey);
                PlayerPrefs.DeleteKey(PortKey);
                var (fresh, freshPort) = NetworkModeService.ResolveConnectEndpoint(url, null, null);
                Assert.AreEqual(NetworkModeService.DefaultHost, fresh, "无链接无覆盖 → 默认 127.0.0.1");
                Assert.AreEqual(NetworkModeService.DefaultPort, freshPort, "无链接无覆盖 → 默认 8000");
            }
            finally
            {
                PlayerPrefs.DeleteKey(HostKey);
                PlayerPrefs.DeleteKey(PortKey);
                PlayerPrefs.DeleteKey(UserKey);
            }
        }
    }
}
