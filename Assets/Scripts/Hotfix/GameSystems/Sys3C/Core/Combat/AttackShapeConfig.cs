using System;

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
        public ShapeType Type;
        public float Range;
        public float Angle;
        public float Width;
        public bool StopAtFirst;
    }
}
