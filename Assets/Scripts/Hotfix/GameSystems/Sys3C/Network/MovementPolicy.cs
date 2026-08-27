using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;

namespace Hotfix.GameSystems.Sys3C.Network
{
    /// <summary>
    /// 移动策略接口
    /// </summary>
    public interface IMovementPolicy
    {
        void Update(MoveCommand command);
        void ApplyServerCorrection(Vector3 position, Quaternion rotation);
    }

    /// <summary>
    /// 本地模式（无网络）
    /// </summary>
    public class LocalMovementPolicy : IMovementPolicy
    {
        private readonly Hotfix.GameSystems.Sys3C.Character.CharacterController _controller;

        public LocalMovementPolicy(Hotfix.GameSystems.Sys3C.Character.CharacterController controller)
        {
            _controller = controller;
        }

        public void Update(MoveCommand command)
        {
            _controller.Update(command);
        }

        public void ApplyServerCorrection(Vector3 position, Quaternion rotation)
        {
            // 本地模式不使用服务端校正
        }
    }

}