namespace Hotfix.GameSystems.Sys3C.FSM.States
{
    /// <summary>
    /// FSM 状态接口
    /// </summary>
    public interface IState
    {
        /// <summary>
        /// 状态进入
        /// </summary>
        void Enter();

        /// <summary>
        /// 状态退出
        /// </summary>
        void Exit();

        /// <summary>
        /// 每帧更新
        /// </summary>
        void Update(float deltaTime);

        /// <summary>
        /// 状态名称
        /// </summary>
        string StateName { get; }
    }
}