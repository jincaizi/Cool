using System;
using System.Collections.Generic;
using Cool.Protocol;

namespace Cool.SyncCore
{
    /// <summary>命中判定候选（服务端从权威实体状态映射——纯数据，双端同源编译）</summary>
    public readonly struct CombatCandidate
    {
        public readonly long Id;
        public readonly Vec3 Position;
        public readonly bool Alive;

        public CombatCandidate(long id, Vec3 position, bool alive)
        {
            Id = id;
            Position = position;
            Alive = alive;
        }
    }

    /// <summary>
    /// 服务端权威命中裁决（spec §5-P4：判定几何两端同源）——纯几何，零 UnityEngine 依赖。
    /// 语义与客户端 AttackShape 对齐（XZ 平面；Angle 全角，半角余弦比较）：
    ///  - Single（显式 targetId）：距离 ≤ Range **且**位于前方 ±Angle/2 锥内（显式目标同样过锥，D-6）；
    ///  - Single（targetId=0，P4 客户端恒 0）：Range 内 + 施法者前方 ±Angle/2 锥内最近实体；
    ///    零距离退化（dist≈0）视为锥内命中——客户端 normalized 对零向量返回零向量（dot=0
    ///    &lt; halfCos 漏判），服务端修正（D-P4-2a）；
    ///  - AOE_Circle：水平距离 ≤ AreaRadius。
    /// ShapeType 非 1 一律按 Single 处理，调用方须先经 CombatRules.ValidateShape 校验形状合法性。
    /// AOE 命中顺序 = 候选遍历顺序（非距离排序）。只返回 Alive 候选；caster 由调用方排除（不传入自身）。
    /// yaw 为周期角（sin/cos 展开，天然 wrap 安全）。Math.Sin/Cos 在 .NET 8 与 Mono/IL2CPP 间
    /// 不保证位级一致——命中裁决服务器权威，跨端微小差异无害。
    /// </summary>
    public static class HitResolver
    {
        public static List<long> Resolve(Vec3 casterPos, float casterYawDeg, long targetId,
            HitShape shape, IReadOnlyList<CombatCandidate> candidates)
        {
            var hits = new List<long>();
            if (shape == null || candidates == null) return hits;

            if (shape.ShapeType == 1) // AOE_Circle
            {
                foreach (var c in candidates)
                {
                    if (!c.Alive) continue;
                    if (DistXZ(casterPos, c.Position) <= shape.AreaRadius)
                        hits.Add(c.Id);
                }
                return hits;
            }

            // 锥的唯一定义（自动目标与显式目标共用）——yaw 语义 = Unity 欧拉 Y：yaw=0 → +Z
            float forwardX = (float)Math.Sin(casterYawDeg * Math.PI / 180.0);
            float forwardZ = (float)Math.Cos(casterYawDeg * Math.PI / 180.0);
            float halfCos = (float)Math.Cos(shape.Angle * 0.5f * Math.PI / 180.0);

            // Single
            if (targetId > 0)
            {
                foreach (var c in candidates)
                {
                    if (c.Id != targetId || !c.Alive) continue;
                    if (DistXZ(casterPos, c.Position) > shape.Range) continue;
                    if (!InCone(casterPos, forwardX, forwardZ, halfCos, c.Position)) continue;
                    hits.Add(c.Id);
                    break;
                }
                return hits;
            }

            // 自动目标：Range 内 + 前方锥内最近者
            long best = 0;
            float bestDist = float.MaxValue;
            foreach (var c in candidates)
            {
                if (!c.Alive) continue;
                float dist = DistXZ(casterPos, c.Position);
                if (dist > shape.Range) continue;
                if (!InCone(casterPos, forwardX, forwardZ, halfCos, c.Position)) continue;
                if (dist < bestDist) { best = c.Id; bestDist = dist; }
            }
            if (best != 0) hits.Add(best);
            return hits;
        }

        /// <summary>前方 ±Angle/2 锥内（含边界）；零距离退化视为锥内（D-P4-2a——双端同出生点）。</summary>
        private static bool InCone(Vec3 casterPos, float forwardX, float forwardZ, float halfCos, Vec3 target)
        {
            float dx = target.X - casterPos.X;
            float dz = target.Z - casterPos.Z;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist < 0.0001f) return true;
            return (forwardX * dx + forwardZ * dz) / dist >= halfCos;
        }

        private static float DistXZ(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
