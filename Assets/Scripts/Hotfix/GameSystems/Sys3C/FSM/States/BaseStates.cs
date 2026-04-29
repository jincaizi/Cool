using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.FSM.States
{
    /// <summary>
    /// Idle 状态
    /// </summary>
    public class IdleState : IState
    {
        public string StateName => "Idle";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// Move 状态
    /// </summary>
    public class MoveState : IState
    {
        public string StateName => "Move";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// Sprint 状态
    /// </summary>
    public class SprintState : IState
    {
        public string StateName => "Sprint";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// JumpStart 状态
    /// </summary>
    public class JumpStartState : IState
    {
        public string StateName => "JumpStart";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// JumpAir 状态
    /// </summary>
    public class JumpAirState : IState
    {
        public string StateName => "JumpAir";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// JumpEnd 状态
    /// </summary>
    public class JumpEndState : IState
    {
        public string StateName => "JumpEnd";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }

    /// <summary>
    /// Death 状态
    /// </summary>
    public class DeathState : IState
    {
        public string StateName => "Death";

        public void Enter() { }
        public void Exit() { }
        public void Update(float deltaTime) { }
    }
}
