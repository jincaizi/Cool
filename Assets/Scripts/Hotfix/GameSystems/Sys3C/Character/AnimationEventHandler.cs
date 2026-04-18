using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 挂载到角色 Prefab 上，接收 Unity AnimationEvent 并转发给 CharacterAnimationDriver
    /// 需要在 Animator Controller 的动画 Clip 上添加 AnimationEvent：
    /// - "OnJumpToAir" → 绑定 JumpStart 动画末尾，触发从 JumpStart 切换到 JumpAir
    /// - "OnJumpEndComplete" → 绑定 JumpEnd 动画末尾，触发回到 Idle/BattleIdle
    /// - "OnAttackComplete" → 绑定 Attack01~04 动画末尾，触发攻击完成
    /// </summary>
    public class AnimationEventHandler : MonoBehaviour
    {
        [SerializeField] private CharacterAnimationDriver _driver;

        private void Awake()
        {
            if (_driver == null)
                _driver = GetComponent<CharacterAnimationDriver>();
        }

        /// <summary>
        /// 外部注入 Driver 引用
        /// </summary>
        public void SetDriver(CharacterAnimationDriver driver)
        {
            _driver = driver;
        }

        /// <summary>
        /// 动画事件：跳跃过渡到空中
        /// </summary>
        public void OnJumpToAir()
        {
            _driver?.OnJumpToAir();
        }

        /// <summary>
        /// 动画事件：跳跃结束
        /// </summary>
        public void OnJumpEndComplete()
        {
            _driver?.OnJumpEndComplete();
        }

        /// <summary>
        /// 动画事件：攻击完成
        /// </summary>
        public void OnAttackComplete()
        {
            _driver?.OnAttackComplete();
        }
    }
}
