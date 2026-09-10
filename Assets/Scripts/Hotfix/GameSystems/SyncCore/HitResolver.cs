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
    ///  - Single（显式 targetId）：距离 ≤ Range；
    ///  - Single（targetId=0，P4 客户端恒 0）：Range 内 + 施法者前方 ±Angle/2 锥内最近实体；
    ///    零距离退化（dist≈0）视为锥内命中——客户端 normalized 零向量 NaN 漏判，服务端修正（D-P4-2a）；
    ///  - AOE_Circle：水平距离 ≤ AreaRadius。
    /// 只返回 Alive 候选；caster 由调用方排除（不传入自身）。
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

            // Single
            if (targetId > 0)
            {
                foreach (var c in candidates)
                {
                    if (c.Id == targetId && c.Alive && DistXZ(casterPos, c.Position) <= shape.Range)
                    {
                        hits.Add(c.Id);
                        break;
                    }
                }
                return hits;
            }

            // 自动目标：Range 内 + 前方锥内最近者（yaw 语义 = Unity 欧拉 Y：yaw=0 → +Z）
            float forwardX = (float)Math.Sin(casterYawDeg * Math.PI / 180.0);
            float forwardZ = (float)Math.Cos(casterYawDeg * Math.PI / 180.0);
            float halfCos = (float)Math.Cos(shape.Angle * 0.5f * Math.PI / 180.0);
            long best = 0;
            float bestDist = float.MaxValue;
            foreach (var c in candidates)
            {
                if (!c.Alive) continue;
                float dx = c.Position.X - casterPos.X;
                float dz = c.Position.Z - casterPos.Z;
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                if (dist > shape.Range) continue;
                if (dist >= 0.0001f)
                {
                    float dot = (forwardX * dx + forwardZ * dz) / dist;
                    if (dot < halfCos) continue;
                }
                if (dist < bestDist) { best = c.Id; bestDist = dist; }
            }
            if (best != 0) hits.Add(best);
            return hits;
        }

        private static float DistXZ(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
