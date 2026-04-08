using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 虚拟摇杆输入适配器（用于移动端）
    /// </summary>
    public class JoystickInputAdapter : IInputAdapter
    {
        private const float DEAD_ZONE = 0.05f;

        public string AdapterName => "Joystick";

        /// <summary>
        /// 获取标准化移动输入（从虚拟摇杆）
        /// </summary>
        public Vector3 GetMoveInput()
        {
            float horizontal = UnityEngine.Input.GetAxisRaw("Horizontal_Joystick");
            float vertical = UnityEngine.Input.GetAxisRaw("Vertical_Joystick");

            Vector3 input = new Vector3(horizontal, 0f, vertical);

            if (input.magnitude < DEAD_ZONE)
                return Vector3.zero;

            return input.normalized;
        }

        /// <summary>
        /// 获取相机旋转输入（从第二个摇杆或触摸拖拽）
        /// </summary>
        public Vector2 GetCameraRotationInput()
        {
            float rotX = UnityEngine.Input.GetAxisRaw("Mouse X_Joystick");
            float rotY = UnityEngine.Input.GetAxisRaw("Mouse Y_Joystick");

            // 如果没有摇杆输入，尝试触摸拖拽
            if (Mathf.Abs(rotX) < DEAD_ZONE && Mathf.Abs(rotY) < DEAD_ZONE)
            {
                if (UnityEngine.Input.touchCount > 0)
                {
                    Touch touch = UnityEngine.Input.GetTouch(0);
                    if (touch.phase == TouchPhase.Moved)
                    {
                        return touch.deltaPosition / 100f;
                    }
                }
            }

            return new Vector2(rotX, rotY);
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
