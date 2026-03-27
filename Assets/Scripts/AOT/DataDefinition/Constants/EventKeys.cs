namespace AOT.DataDefinition.Constants
{
    /// <summary>
    /// 事件键常量定义
    /// </summary>
    public static class EventKeys
    {
        // 输入事件
        /// <summary>
        /// 移动事件
        /// </summary>
        public const string Move = "Event_Move";

        /// <summary>
        /// 跳跃事件
        /// </summary>
        public const string Jump = "Event_Jump";

        /// <summary>
        /// 跳跃取消事件
        /// </summary>
        public const string JumpCancelled = "Event_JumpCancelled";

        /// <summary>
        /// 攻击事件
        /// </summary>
        public const string Attack = "Event_Attack";

        /// <summary>
        /// 攻击取消事件
        /// </summary>
        public const string AttackCancelled = "Event_AttackCancelled";

        /// <summary>
        /// 交互事件
        /// </summary>
        public const string Interact = "Event_Interact";

        /// <summary>
        /// 冲刺事件
        /// </summary>
        public const string Sprint = "Event_Sprint";

        /// <summary>
        /// 冲刺取消事件
        /// </summary>
        public const string SprintCancelled = "Event_SprintCancelled";

        // 玩家事件
        /// <summary>
        /// 玩家状态改变事件
        /// </summary>
        public const string PlayerStateChanged = "Event_PlayerStateChanged";

        /// <summary>
        /// 玩家移动事件
        /// </summary>
        public const string PlayerMoved = "Event_PlayerMoved";

        /// <summary>
        /// 玩家跳跃事件
        /// </summary>
        public const string PlayerJumped = "Event_PlayerJumped";

        /// <summary>
        /// 玩家攻击事件
        /// </summary>
        public const string PlayerAttacked = "Event_PlayerAttacked";

        /// <summary>
        /// 玩家受击事件
        /// </summary>
        public const string PlayerHit = "Event_PlayerHit";

        /// <summary>
        /// 玩家死亡事件
        /// </summary>
        public const string PlayerDied = "Event_PlayerDied";

        // 相机事件
        /// <summary>
        /// 相机旋转事件
        /// </summary>
        public const string CameraRotated = "Event_CameraRotated";

        /// <summary>
        /// 相机缩放事件
        /// </summary>
        public const string CameraZoomed = "Event_CameraZoomed";

        // 网络同步事件
        /// <summary>
        /// 服务器位置同步事件
        /// </summary>
        public const string ServerPositionSync = "Event_ServerPositionSync";

        /// <summary>
        /// 服务器状态同步事件
        /// </summary>
        public const string ServerStateSync = "Event_ServerStateSync";
    }
}
