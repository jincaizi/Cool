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

        /// <summary>服务器地址（无存档 → 默认值）</summary>
        public static string LoadHost()
            => PlayerPrefs.HasKey(HostKey) ? PlayerPrefs.GetString(HostKey) : DefaultHost;

        public static int LoadPort()
            => PlayerPrefs.HasKey(PortKey) ? PlayerPrefs.GetInt(PortKey) : DefaultPort;

        /// <summary>
        /// Task 13：**生效的**服务器地址 —— URL 参数 `?host=` &gt; 存档 &gt; 默认值。
        /// 面试链接里带上地址，对方就不用在那块输入框里手填公网 IP。
        ///
        /// 无 `?host=` 参数时与 `LoadHost` **逐字等价**：`TryFromUrlQuery` 在编辑器/PC 上恒 false，
        /// 在 WebGL 上也要 URL 真带了该参数才为 true。因此这里只覆盖"有参数"那一种输入，
        /// 其余路径（含存档、默认 `127.0.0.1`）一个字没动。
        /// </summary>
        public static string ResolveHost()
            => ServerAddressQuery.TryFromUrlQuery(DefaultPort, out string host, out _) ? host : LoadHost();

        /// <summary>生效的端口：URL 带端口就用它、只带地址则用默认 8000（链接自洽），都没带则回落到存档</summary>
        public static int ResolvePort()
            => ServerAddressQuery.TryFromUrlQuery(DefaultPort, out _, out int port) ? port : LoadPort();

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
