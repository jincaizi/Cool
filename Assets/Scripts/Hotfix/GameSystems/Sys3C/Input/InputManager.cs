using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 输入管理器 — 双层抽象核心
    /// 统一所有输入适配器，输出标准化 MoveCommand
    /// </summary>
    public class InputManager
    {
        private readonly List<IInputAdapter> _adapters = new List<IInputAdapter>();
        private IInputAdapter _activeAdapter;

        // 相机旋转灵敏度
        public float CameraSensitivityX { get; set; } = 2.0f;
        public float CameraSensitivityY { get; set; } = 2.0f;

        // 移动速度
        public float MoveSpeed { get; set; } = 5.0f;

        // 当前序列号（用于网络预测）
        private uint _sequence;

        public InputManager()
        {
            // 注册所有适配器
            RegisterAdapter(new KeyboardInputAdapter());
            RegisterAdapter(new JoystickInputAdapter());

            // 默认使用键盘
            _activeAdapter = _adapters[0]; // KeyboardInputAdapter
        }

        /// <summary>
        /// 注册输入适配器
        /// </summary>
        public void RegisterAdapter(IInputAdapter adapter)
        {
            if (!_adapters.Contains(adapter))
                _adapters.Add(adapter);
        }

        /// <summary>
        /// 切换活动适配器
        /// </summary>
        public void SetActiveAdapter(string adapterName)
        {
            foreach (var adapter in _adapters)
            {
                if (adapter.AdapterName == adapterName)
                {
                    _activeAdapter = adapter;
                    return;
                }
            }
        }

        /// <summary>
        /// 获取标准化移动命令
        /// </summary>
        public MoveCommand GetMoveCommand(Vector3 characterForward)
        {
            Vector3 moveInput = _activeAdapter.GetMoveInput();

            // 将世界坐标输入转换为角色朝向相关的局部坐标
            // 如果有输入，角色朝向跟随移动方向
            Quaternion targetRotation = characterForward.sqrMagnitude > 0.1f
                ? Quaternion.LookRotation(characterForward)
                : Quaternion.identity;

            if (moveInput.sqrMagnitude > 0.1f)
            {
                // 将输入从世界坐标转换到相机视角
                // （相机朝向决定"前方向"）
                Vector3 worldMoveDir = ConvertToWorldDirection(moveInput);
                targetRotation = Quaternion.LookRotation(worldMoveDir);
            }

            return new MoveCommand
            {
                MoveDir = moveInput,
                Speed = MoveSpeed,
                Rotation = targetRotation,
                Timestamp = System.DateTime.UtcNow.Ticks,
                Sequence = ++_sequence
            };
        }

        /// <summary>
        /// 获取相机旋转输入（原始向量）
        /// </summary>
        public Vector2 GetCameraRotationInput()
        {
            return _activeAdapter.GetCameraRotationInput();
        }

        /// <summary>
        /// 将输入向量从相机视角转换为世界方向
        /// </summary>
        private Vector3 ConvertToWorldDirection(Vector3 input)
        {
            // 简化实现：input 是相对于相机的方向
            // 实际需要相机朝向，这里假设相机的forward是场景中的"前"
            // 真实实现需要 Camera.main.transform.forward
            return input;
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update()
        {
            // 可在此处理适配器自动切换逻辑
            // 例如：检测到触摸时切换到 JoystickInputAdapter
        }
    }
}