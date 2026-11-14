namespace Hotfix.GameSystems.Skills.Runtime
{
    /// <summary>
    /// 联机剑气表现开关：启用属性剑气 v1 的客户端不再由本地 SkillExecutor 生成剑气，
    /// 统一由服务端 ProjectileLaunchBroadcast 创建，避免本地预测与服务端发射重影。
    /// 离线/单机保持 false，行为不变。
    /// </summary>
    public static class NetworkSwordQiGate
    {
        public static bool SuppressLocalSpawn { get; set; }
    }
}
