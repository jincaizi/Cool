using Hotfix.GameSystems.Sys3C.Character;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Network
{
    /// <summary>
    /// 网络移动策略（spec B+：本地即时模拟 + 服务端校验平滑纠偏）——
    /// Update 驱动本地物理 + 节流上报；纠偏按偏移衰减收敛（不瞬移）。
    /// </summary>
    public class NetMovementPolicy : IMovementPolicy
    {
        private readonly Hotfix.GameSystems.Sys3C.Character.CharacterController _controller;
        private bool _correcting;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private const float CorrectionBlendPerSecond = 8f; // 每秒收敛速度

        // 不持有 MovementReporter——上报节流由 ClientNetworkManager.Update 驱动（与测试宿主解耦）
        public NetMovementPolicy(Hotfix.GameSystems.Sys3C.Character.CharacterController controller)
        {
            _controller = controller;
        }

        public void Update(MoveCommand command)
        {
            _controller.Update(command);
            // 上报节流由 ClientNetworkManager.Update 驱动（与测试宿主解耦）——
            // policy 只管本地物理 + 拉回

            // 平滑拉回：每帧向纠偏目标收敛（偏移衰减，无跳变）
            if (_correcting)
            {
                var current = _controller.Transform.position;
                var next = Vector3.Lerp(current, _targetPosition, CorrectionBlendPerSecond * Time.deltaTime);
                _controller.Transform.position = next;
                _controller.Transform.rotation = Quaternion.Slerp(
                    _controller.Transform.rotation, _targetRotation, CorrectionBlendPerSecond * Time.deltaTime);

                if (Vector3.Distance(next, _targetPosition) < 0.05f
                    && Quaternion.Angle(_controller.Transform.rotation, _targetRotation) < 5f)
                    _correcting = false;
            }
        }

        public void ApplyServerCorrection(Vector3 position, Quaternion rotation)
        {
            _targetPosition = position;
            _targetRotation = rotation;
            _correcting = true;
        }
    }
}
