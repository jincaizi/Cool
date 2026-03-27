using UnityEngine;

namespace AOT.DataDefinition.Interfaces
{
    /// <summary>
    /// 相机处理器接口
    /// </summary>
    public interface ICameraHandler
    {
        /// <summary>
        /// 相机Transform
        /// </summary>
        Transform CameraTransform { get; }

        /// <summary>
        /// 目标对象
        /// </summary>
        Transform Target { get; set; }

        /// <summary>
        /// 相机距离
        /// </summary>
        float Distance { get; set; }

        /// <summary>
        /// 水平旋转角度
        /// </summary>
        float HorizontalAngle { get; set; }

        /// <summary>
        /// 垂直旋转角度
        /// </summary>
        float VerticalAngle { get; set; }

        /// <summary>
        /// 相机平滑系数
        /// </summary>
        float SmoothTime { get; set; }

        /// <summary>
        /// 初始化
        /// </summary>
        void Initialize();

        /// <summary>
        /// 更新相机
        /// </summary>
        void UpdateCamera();

        /// <summary>
        /// 销毁
        /// </summary>
        void Destroy();
    }
}
