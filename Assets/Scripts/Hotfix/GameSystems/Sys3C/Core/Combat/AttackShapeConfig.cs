using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public enum ShapeType
    {
        Cone = 0,
        Circle = 1,
        Rect = 2,
        Sector = 3,
    }

    [Serializable]
    public class AttackShapeConfig
    {
        [Header("Shape")]
        [Tooltip("攻击形状类型")]
        public ShapeType Type;

        [Header("Dimensions")]
        [Tooltip("攻击范围（距离/半径）")]
        public float Range = 2f;

        [Tooltip("锥形角度（仅 Cone 有效）")]
        public float Angle = 120f;

        [Tooltip("扇形起始角度，forward=0°，正值=右，负值=左（仅 Sector 有效）")]
        public float AngleStart;

        [Tooltip("扇形终止角度（仅 Sector 有效）")]
        public float AngleEnd = 90f;

        [Tooltip("矩形宽度（仅 Rect 有效）")]
        public float Width = 1f;

        [Header("Collision")]
        [Tooltip("碰到第一个目标后是否停止")]
        public bool StopAtFirst;
    }
}
