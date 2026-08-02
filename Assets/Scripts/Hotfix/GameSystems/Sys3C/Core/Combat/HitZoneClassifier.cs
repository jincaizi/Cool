using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// 命中部位分类与倍率（纯数学、零依赖；公开静态字段便于调参与测试覆写）
    /// </summary>
    public static class HitZoneClassifier
    {
        public static float HeadRatio = 0.75f;   // 相对高度 ≥0.75 → Head
        public static float LegsRatio = 0.35f;   // 相对高度 <0.35 → Legs
        public static float HeadKnockbackMultiplier = 1.5f;
        public static float TorsoKnockbackMultiplier = 1.0f;
        public static float LegsKnockbackMultiplier = 0.7f;
        public static float HeadScaleMultiplier = 1.3f;
        public static float TorsoScaleMultiplier = 1.0f;
        public static float LegsScaleMultiplier = 0.8f;

        public static HitZonePart Classify(Vector3 point, Bounds bounds)
        {
            float sizeY = bounds.size.y;
            if (sizeY <= 0.0001f) return HitZonePart.Torso;  // 退化保护
            float ratio = (point.y - bounds.min.y) / sizeY;   // 世界空间 AABB 相对高度
            if (ratio >= HeadRatio) return HitZonePart.Head;
            if (ratio < LegsRatio) return HitZonePart.Legs;
            return HitZonePart.Torso;
        }

        public static float KnockbackMultiplier(HitZonePart part)
        {
            switch (part)
            {
                case HitZonePart.Head: return HeadKnockbackMultiplier;
                case HitZonePart.Legs: return LegsKnockbackMultiplier;
                default: return TorsoKnockbackMultiplier;
            }
        }

        public static float ScaleMultiplier(HitZonePart part)
        {
            switch (part)
            {
                case HitZonePart.Head: return HeadScaleMultiplier;
                case HitZonePart.Legs: return LegsScaleMultiplier;
                default: return TorsoScaleMultiplier;
            }
        }
    }
}
