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
        private readonly CharacterController _controller;

        public LocalMovementPolicy(CharacterController controller)
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
        private readonly CharacterController _controller;
        private readonly NetworkPrediction _prediction;
        private readonly NetworkBridge _bridge;
        private uint _sequence;

        public PredictionMovementPolicy(CharacterController controller, NetworkBridge bridge)
        {
            _controller = controller;
            _bridge = bridge;
            _prediction = new NetworkPrediction();
        }

        public void Update(MoveCommand command)
        {
            // 执行本地物理
            _controller.Update(command);

            // 记录预测帧
            _prediction.RecordPredictedFrame(_sequence, _controller.Data.Position, _controller.Data.Rotation);

            // 发送输入
            _bridge.SendInput(command, _sequence);

            _sequence++;
        }

        public void ApplyServerCorrection(Vector3 position, Quaternion rotation)
        {
            _controller.ApplyServerPosition(position, rotation);
        }
    }
}