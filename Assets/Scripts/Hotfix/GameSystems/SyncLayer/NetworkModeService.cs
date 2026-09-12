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
    }
}
