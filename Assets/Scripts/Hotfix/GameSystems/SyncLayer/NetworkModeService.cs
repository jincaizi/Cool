using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>网络模式（由选人面板选择并持久化）</summary>
    public enum NetworkMode
    {
        SinglePlayer = 0,
        Online = 1
    }

    /// <summary>
    /// 网络模式持久化：读写 PlayerPrefs（镜像 CharacterSelectionService 范式）。
    /// 默认单机——无存档 / 非法值一律单机（联网须显式选择，避免无服务器环境误连）。
    /// </summary>
    public static class NetworkModeService
    {
        private const string PrefsKey = "net.mode";

        public static void Save(NetworkMode mode)
        {
            PlayerPrefs.SetInt(PrefsKey, (int)mode);
            PlayerPrefs.Save();
        }

        public static NetworkMode Load()
        {
            if (!PlayerPrefs.HasKey(PrefsKey)) return NetworkMode.SinglePlayer;
            int raw = PlayerPrefs.GetInt(PrefsKey);
            return raw == (int)NetworkMode.Online ? NetworkMode.Online : NetworkMode.SinglePlayer;
        }

        private const string HostKey = "net.host";
        private const string PortKey = "net.port";
        private const string UserKey = "net.username";

        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 8000;
        public const string DefaultUsername = "Player";

        /// <summary>
        /// 服务器地址（无存档 → 默认值）。**连接的端点就是这里**：选人面板「确认」时把**框里的值**
        /// `SaveConnection` 进来，`ClientNetworkHost.Connect` 再读出去。
        ///
        /// Task 13 修复轮：URL 参数（`?host=`）**只影响预填**（见 `PrefillHost`），**不参与连接决议**——
        /// 否则操作者把框改成别的地址后，链接里的值会在连接那一刻把它静默压掉，
        /// "框里显示的就是会连的"就成了假话。
        /// </summary>
        public static string LoadHost()
            => PlayerPrefs.HasKey(HostKey) ? PlayerPrefs.GetString(HostKey) : DefaultHost;

        /// <summary>服务器端口（无存档 → 默认值）。与 `LoadHost` 同一条规则：连接的端点只来自存档</summary>
        public static int LoadPort()
            => PlayerPrefs.HasKey(PortKey) ? PlayerPrefs.GetInt(PortKey) : DefaultPort;

        /// <summary>
        /// Task 13：**预填**用的地址 —— URL 参数 `?host=` &gt; 存档 &gt; 默认值。
        /// 面试链接里带上地址，对方就不用在那块输入框里手填公网 IP（链接的值先落进框，
        /// 「确认」再把框里的值交给连接）。
        ///
        /// **只用于预填**（`CharacterSelectPanel.OnShow`）。无 `?host=` 参数时与 `LoadHost` 逐字等价：
        /// `CurrentPageUrl` 在编辑器/PC 上恒空串，WebGL 上也要 URL 真带了该参数才为 true。
        /// </summary>
        public static string PrefillHost()
            => PrefillHost(ServerAddressQuery.CurrentPageUrl(), LoadHost());

        /// <summary>预填用的端口：URL 带端口就用它、只带地址则用默认 8000（链接自洽），都没带则回落到存档</summary>
        public static int PrefillPort()
            => PrefillPort(ServerAddressQuery.CurrentPageUrl(), LoadPort());

        /// <summary>预填（纯函数，可单测）：页面 URL 给得出就用它，否则用传入的兜底（存档值）</summary>
        public static string PrefillHost(string? pageUrl, string savedHost)
            => ServerAddressQuery.TryParse(pageUrl, DefaultPort, out string host, out _) ? host : savedHost;

        /// <inheritdoc cref="PrefillHost(string,string)"/>
        public static int PrefillPort(string? pageUrl, int savedPort)
            => ServerAddressQuery.TryParse(pageUrl, DefaultPort, out _, out int port) ? port : savedPort;

        public static string LoadUsername()
            => PlayerPrefs.HasKey(UserKey) ? PlayerPrefs.GetString(UserKey) : DefaultUsername;

        /// <summary>保存连接参数；端口非法或地址为空时回落到默认值（防手输错误导致无法连接）</summary>
        public static void SaveConnection(string host, string portText, string username)
        {
            string safeHost = string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim();

            int safePort = DefaultPort;
            if (int.TryParse(portText, out int parsed) && parsed > 0 && parsed <= 65535)
                safePort = parsed;

            string safeUser = string.IsNullOrWhiteSpace(username) ? DefaultUsername : username.Trim();

            PlayerPrefs.SetString(HostKey, safeHost);
            PlayerPrefs.SetInt(PortKey, safePort);
            PlayerPrefs.SetString(UserKey, safeUser);
            PlayerPrefs.Save();
        }
    }
}
