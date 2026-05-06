namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// 基础FSM接口 - 用于跨程序集解耦
    /// </summary>
    public interface IBaseFSM
    {
        /// <summary>
        /// 当前状态
        /// </summary>
        int CurrentState { get; }

        /// <summary>
        /// 状态变更事件
        /// </summary>
        event System.Action<int> OnStateChanged;

        /// <summary>
        /// 锁定状态
        /// </summary>
        void LockState(int state);

        /// <summary>
        /// 解锁
        /// </summary>
        void Unlock(int defaultState);
    }

    /// <summary>
    /// 攻击FSM接口 - 用于跨程序集解耦
    /// </summary>
    public interface IAttackFSM
    {
        /// <summary>
        /// 当前状态
        /// </summary>
        int CurrentState { get; }

        /// <summary>
        /// 是否有霸体
        /// </summary>
        bool HasSuperArmor { get; }

        /// <summary>
        /// 剩余霸体时间
        /// </summary>
        float SuperArmorRemaining { get; }

        /// <summary>
        /// 是否可以播放技能
        /// </summary>
        bool CanPlaySkill { get; }

        /// <summary>
        /// 攻击完成事件
        /// </summary>
        event System.Action OnAttackCompleted;

        /// <summary>
        /// 技能完成事件
        /// </summary>
        event System.Action OnSkillCompleted;

        /// <summary>
        /// 请求普通攻击
        /// </summary>
        void RequestNormalAttack();

        /// <summary>
        /// 请求技能Q
        /// </summary>
        void RequestSkillQ();

        /// <summary>
        /// 请求技能R
        /// </summary>
        void RequestSkillR(bool isAir);

        /// <summary>
        /// 强制空闲
        /// </summary>
        void ForceIdle();
    }

    /// <summary>
    /// FSM配置接口
    /// </summary>
    public interface ICharacterFSMConfig
    {
        float MaxResistance { get; }
        float HitDuration { get; }
        float KnockbackDuration { get; }
        float LaunchedDuration { get; }
        float DizzyDuration { get; }
        float DownDuration { get; }
        float GetUpDuration { get; }
        float KnockbackDeceleration { get; }
        float LaunchGravity { get; }
        float LaunchHorizontalDrag { get; }
    }

    /// <summary>
    /// 默认配置实现
    /// </summary>
    public class CharacterFSMConfig : ICharacterFSMConfig
    {
        public float MaxResistance => 100f;
        public float HitDuration => 0.2f;
        public float KnockbackDuration => 0.4f;
        public float LaunchedDuration => 1.0f;
        public float DizzyDuration => 2.0f;
        public float DownDuration => 2.0f;
        public float GetUpDuration => 0.5f;
        public float KnockbackDeceleration => 5f;
        public float LaunchGravity => 20f;
        public float LaunchHorizontalDrag => 0.98f;

        public static CharacterFSMConfig Default => new CharacterFSMConfig();
    }
}