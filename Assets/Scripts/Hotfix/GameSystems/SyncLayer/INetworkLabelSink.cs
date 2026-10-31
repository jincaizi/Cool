using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>远程实体名牌/血条的注入点；测试传 null 即不注册（避免命中场景单例）。</summary>
    public interface INetworkLabelSink
    {
        void Register(long entityId, Transform owner, string displayName);
        void UpdateName(long entityId, string displayName);
        void UpdateHp(long entityId, float percent);
        void Unregister(long entityId);
    }
}
