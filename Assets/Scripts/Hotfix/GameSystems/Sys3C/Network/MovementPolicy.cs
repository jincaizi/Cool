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

    /// <summary>
    /// 预测模式（客户端预测 + 服务端校正）
    /// </summary>
    public class PredictionMovementPolicy : IMovementPolicy
    {
        private readonly Hotfix.GameSystems.Sys3C.Character.CharacterController _controller;
        private readonly NetworkBridge _bridge;
        private uint _sequence;

        public PredictionMovementPolicy(Hotfix.GameSystems.Sys3C.Character.CharacterController controller, NetworkBridge bridge)
        {
            _controller = controller;
            _bridge = bridge;
        }

        public void Update(MoveCommand command)
        {
            // 执行本地物理
            _controller.Update(command);

            // 发送位置同步（等AOT层实现SendPositionSync）
            // _bridge.SendPositionSync(_controller.Data.Position, _controller.Data.Rotation, command.Speed);

            _sequence++;
        }

        public void ApplyServerCorrection(Vector3 position, Quaternion rotation)
        {
            _controller.ApplyServerPosition(position, rotation);
        }
    }
}