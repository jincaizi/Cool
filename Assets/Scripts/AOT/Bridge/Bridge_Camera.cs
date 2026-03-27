using System;
using AOT.Core.GameEventDispatcher;
using AOT.DataDefinition.Constants;
using AOT.DataDefinition.Interfaces;
using UnityEngine;

namespace AOT.Bridge
{
    /// <summary>
    /// 相机桥接类，供热更新层调用AOT层功能
    /// </summary>
    public static class Bridge_Camera
    {
        private static ICameraHandler? _cameraHandler;

        /// <summary>
        /// 设置相机处理器实例
        /// </summary>
        public static void SetHandler(ICameraHandler handler)
        {
            _cameraHandler = handler;
        }

        /// <summary>
        /// 获取相机处理器
        /// </summary>
        public static ICameraHandler? GetHandler()
        {
            return _cameraHandler;
        }

        /// <summary>
        /// 设置相机跟随目标
        /// </summary>
        public static void SetTarget(Transform target)
        {
            if (_cameraHandler != null)
            {
                _cameraHandler.Target = target;
            }
        }

        /// <summary>
        /// 设置相机距离
        /// </summary>
        public static void SetDistance(float distance)
        {
            if (_cameraHandler != null)
            {
                _cameraHandler.Distance = distance;
            }
        }

        /// <summary>
        /// 设置相机旋转角度
        /// </summary>
        public static void SetRotation(float horizontal, float vertical)
        {
            if (_cameraHandler != null)
            {
                _cameraHandler.HorizontalAngle = horizontal;
                _cameraHandler.VerticalAngle = vertical;
            }
        }

        /// <summary>
        /// 获取相机Transform
        /// </summary>
        public static Transform? GetCameraTransform()
        {
            return _cameraHandler?.CameraTransform;
        }

        /// <summary>
        /// 派发相机旋转事件
        /// </summary>
        public static void DispatchRotated(float horizontal, float vertical)
        {
            var data = new CameraRotatedData { Horizontal = horizontal, Vertical = vertical };
            GameEventDispatcher.Instance.Dispatch(EventKeys.CameraRotated, data);
        }

        /// <summary>
        /// 派发相机缩放事件
        /// </summary>
        public static void DispatchZoomed(float distance)
        {
            GameEventDispatcher.Instance.Dispatch(EventKeys.CameraZoomed, distance);
        }

        /// <summary>
        /// 相机旋转事件数据
        /// </summary>
        public struct CameraRotatedData
        {
            public float Horizontal;
            public float Vertical;
        }
    }
}
