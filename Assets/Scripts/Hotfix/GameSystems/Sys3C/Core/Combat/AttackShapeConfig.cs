using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public enum ShapeType
    {
        Cone = 0,
        Circle = 1,
        Rect = 2,
        Ray = 3,
    }

    [Serializable]
    public class AttackShapeConfig
    {
        [Header("Shape")]
        [Tooltip("攻击形状类型：锥形、圆形、矩形或射线")]
        public ShapeType Type;

        [Header("Dimensions")]
        [Tooltip("攻击范围 (距离)")]
        public float Range;

        [Tooltip("锥形角度 (仅锥形有效)")]
        public float Angle;

        [Tooltip("矩形宽度 (仅矩形有效)")]
        public float Width;

        [Header("Collision")]
        [Tooltip("碰到第一个目标后是否停止")]
        public bool StopAtFirst;
    }
}
