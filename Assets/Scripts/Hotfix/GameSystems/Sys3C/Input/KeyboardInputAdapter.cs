using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 输入适配器接口
    /// </summary>
    public interface IInputAdapter
    {
        string AdapterName { get; }
        Vector3 GetMoveInput();
        Vector2 GetCameraRotationInput();
        bool HasMoveInput();
        bool HasCameraRotationInput();
    }

    /// <summary>
    /// WASD 键盘输入适配器
    /// </summary>
    public class KeyboardInputAdapter : IInputAdapter
    {
        private const float DEAD_ZONE = 0.1f;

        public string AdapterName => "Keyboard";

        /// <summary>
        /// 获取标准化移动输入
        /// </summary>
        public Vector3 GetMoveInput()
        {
            float horizontal = UnityEngine.Input.GetAxisRaw("Horizontal"); // A/D
            float vertical = UnityEngine.Input.GetAxisRaw("Vertical");   // W/S

            Vector3 input = new Vector3(horizontal, 0f, vertical);

            if (input.magnitude < DEAD_ZONE)
                return Vector3.zero;

            return input.normalized;
        }

        /// <summary>
        /// 获取相机旋转输入（鼠标）
        /// </summary>
        public Vector2 GetCameraRotationInput()
        {
            // Mouse X = 水平旋转，Mouse Y = 垂直旋转
            float mouseX = UnityEngine.Input.GetAxisRaw("Mouse X");
            float mouseY = UnityEngine.Input.GetAxisRaw("Mouse Y");
            return new Vector2(mouseX, mouseY);
        }

        /// <summary>
        /// 是否有移动输入
        /// </summary>
        public bool HasMoveInput()
        {
            return GetMoveInput().sqrMagnitude > 0f;
        }

        /// <summary>
        /// 是否有相机旋转输入
        /// </summary>
        public bool HasCameraRotationInput()
        {
            Vector2 rot = GetCameraRotationInput();
            return rot.sqrMagnitude > 0f;
        }
    }
}
