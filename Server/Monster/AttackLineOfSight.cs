using KcpServer.Navigation;

namespace KcpServer.Monster
{
    /// <summary>
    /// 攻击视线（2026-11-04）：与移动体型解耦。
    /// 只回答“武器到目标之间有没有 navmesh 空洞”——0.25m 探针。
    /// 移动/避让仍用 MonsterData.bodyRadius；这里的探针不是身体 clearance。
    /// 不做目标点投影：TryProject 返回网格边界点（clearance 0），0.25m 探针仍会失败。
    /// </summary>
    public static class AttackLineOfSight
    {
        public const float ProbeRadius = 0.25f;

        public static bool HasLineOfSight(NavigationService navigation, Vector3 from, Vector3 to)
            => navigation.HasLineOfSight(from, to, ProbeRadius);
    }
}
