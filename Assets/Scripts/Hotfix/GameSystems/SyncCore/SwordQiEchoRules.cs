using System;
using System.Collections.Generic;

namespace Cool.SyncCore
{
    /// <summary>「剑气回荡」首版配置（双端同源；不依赖 UnityEngine）。</summary>
    public sealed class SwordQiEchoSpec
    {
        public float Radius { get; }
        public int MaxTargets { get; }
        public float DamageMultiplier { get; }
        public float DurationSeconds { get; }
        public int SwordCount { get; }
        public float OvershootMeters { get; }

        public static readonly SwordQiEchoSpec Default =
            new SwordQiEchoSpec(radius: 12f, maxTargets: 8, damageMultiplier: 2f,
                durationSeconds: 2f, swordCount: 4, overshootMeters: 0.75f);

        public SwordQiEchoSpec(float radius, int maxTargets, float damageMultiplier,
            float durationSeconds, int swordCount, float overshootMeters)
        {
            if (!float.IsFinite(radius) || radius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (maxTargets <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxTargets));
            if (!float.IsFinite(damageMultiplier) || damageMultiplier <= 0f)
                throw new ArgumentOutOfRangeException(nameof(damageMultiplier));
            if (!float.IsFinite(durationSeconds) || durationSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            if (swordCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(swordCount));
            if (!float.IsFinite(overshootMeters) || overshootMeters < 0f)
                throw new ArgumentOutOfRangeException(nameof(overshootMeters));

            Radius = radius;
            MaxTargets = maxTargets;
            DamageMultiplier = damageMultiplier;
            DurationSeconds = durationSeconds;
            SwordCount = swordCount;
            OvershootMeters = overshootMeters;
        }
    }

    /// <summary>一次「剑气回荡」中，某个目标被哪道剑气在第几 tick 首次穿过。</summary>
    public readonly struct SwordQiEchoContact
    {
        public readonly long TargetId;
        public readonly int SwordIndex;
        public readonly uint OffsetTicks;
        public readonly Vec3 Position;

        public SwordQiEchoContact(long targetId, int swordIndex, uint offsetTicks, Vec3 position)
        {
            TargetId = targetId;
            SwordIndex = swordIndex;
            OffsetTicks = offsetTicks;
            Position = position;
        }
    }

    /// <summary>
    /// 「剑气回荡」共享规则：选敌、首次接触时间表与默认伤害。
    /// 选敌按表面距离（max(0, 中心距 − 目标半径)）排序，等距按实体 ID 升序。
    /// 首次接触按极角分配剑气；每轮落在施法窗口 20%–80%，同一道剑气的 tick 严格递增。
    /// </summary>
    public static class SwordQiEchoRules
    {
        public static readonly SwordQiEchoSpec Spec = SwordQiEchoSpec.Default;
        public const float PrepareSeconds = 5f;
        public const float ReturnSeconds = 0.46f;
        public const float DissolveSeconds = 0.72f;
        public const float FinishOrbitDelaySeconds = 0.15f;

        /// <summary>普攻首段 10001 的权威基础伤害 × 配置倍率（首版 50 × 2 = 100）。</summary>
        public static float DefaultDamage
        {
            get
            {
                if (!CombatRules.TryGet(10001, out var light))
                    throw new InvalidOperationException("CombatRules 缺少 10001 表项");
                return light.BaseDamage * Spec.DamageMultiplier;
            }
        }

        /// <summary>
        /// 从候选里选出半径内的存活目标，按表面距离升序、等距按 ID 升序，取前 <paramref name="limit"/> 个。
        /// 己方、区域、正式玩家身份由调用方在建候选时过滤；本方法只负责存活与几何。
        /// </summary>
        public static List<CombatCandidate> SelectTargets(Vec3 origin, float radius, int limit,
            IReadOnlyList<CombatCandidate> eligible)
        {
            if (eligible == null) throw new ArgumentNullException(nameof(eligible));
            if (!float.IsFinite(radius) || radius < 0f)
                throw new ArgumentOutOfRangeException(nameof(radius));

            var selected = new List<CombatCandidate>();
            if (limit <= 0) return selected;

            foreach (var candidate in eligible)
            {
                if (!candidate.Alive) continue;
                if (HitResolver.SurfaceDistance(origin, candidate.Position, candidate.Radius) > radius)
                    continue;
                selected.Add(candidate);
            }

            selected.Sort((a, b) =>
            {
                float distanceA = HitResolver.SurfaceDistance(origin, a.Position, a.Radius);
                float distanceB = HitResolver.SurfaceDistance(origin, b.Position, b.Radius);
                int distanceOrder = distanceA.CompareTo(distanceB);
                return distanceOrder != 0 ? distanceOrder : a.Id.CompareTo(b.Id);
            });

            if (selected.Count > limit)
                selected.RemoveRange(limit, selected.Count - limit);
            return selected;
        }

        /// <summary>
        /// 为已选目标生成首次接触时间表。目标按相对圆心的极角排序（同角按表面距离、ID）；
        /// 第 i 个目标由第 i % swordCount 道剑气负责，第 i / swordCount 轮抵达。
        /// 轮次公式：offset = clamp(round((0.2 + roundIndex × 0.6 / max(1, rounds − 1)) × durationTicks), 1, durationTicks − 1)。
        /// </summary>
        public static List<SwordQiEchoContact> BuildContacts(Vec3 origin,
            IReadOnlyList<CombatCandidate> targets, int swordCount, uint durationTicks, ulong routeSeed = 0)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            if (swordCount <= 0) throw new ArgumentOutOfRangeException(nameof(swordCount));
            if (durationTicks < 2) throw new ArgumentOutOfRangeException(nameof(durationTicks));

            var contacts = new List<SwordQiEchoContact>();
            if (targets.Count == 0) return contacts;

            var sorted = new List<CombatCandidate>(targets);
            sorted.Sort((a, b) =>
            {
                int angleOrder = PolarAngle(origin, a.Position).CompareTo(PolarAngle(origin, b.Position));
                if (angleOrder != 0) return angleOrder;
                float distanceA = HitResolver.SurfaceDistance(origin, a.Position, a.Radius);
                float distanceB = HitResolver.SurfaceDistance(origin, b.Position, b.Radius);
                int distanceOrder = distanceA.CompareTo(distanceB);
                return distanceOrder != 0 ? distanceOrder : a.Id.CompareTo(b.Id);
            });

            int rounds = (sorted.Count + swordCount - 1) / swordCount;
            var offsets = new uint[rounds];
            for (int round = 0; round < rounds; round++)
            {
                double fraction = 0.2 + (double)round * 0.6 / Math.Max(1, rounds - 1);
                double raw = fraction * durationTicks;
                double rounded = Math.Round(raw, MidpointRounding.AwayFromZero);
                double clamped = Math.Clamp(rounded, 1.0, durationTicks - 1.0);
                uint offset = (uint)clamped;

                if (round > 0 && offset <= offsets[round - 1])
                    throw new InvalidOperationException(
                        "SwordQiEcho 配置无法为同一道剑气分配严格递增的接触 tick");
                offsets[round] = offset;
            }

            // 按圆周安排目标，再随机映射到可见的飞剑位。未分配到目标的剑继续环绕。
            var swordOrder = new int[swordCount];
            for (int i = 0; i < swordOrder.Length; i++) swordOrder[i] = i;
            if (routeSeed != 0)
            {
                for (int i = swordOrder.Length - 1; i > 0; i--)
                {
                    routeSeed ^= routeSeed << 13;
                    routeSeed ^= routeSeed >> 7;
                    routeSeed ^= routeSeed << 17;
                    int j = (int)(routeSeed % (ulong)(i + 1));
                    (swordOrder[i], swordOrder[j]) = (swordOrder[j], swordOrder[i]);
                }
            }

            for (int i = 0; i < sorted.Count; i++)
            {
                contacts.Add(new SwordQiEchoContact(
                    sorted[i].Id,
                    swordIndex: swordOrder[i % swordCount],
                    offsetTicks: offsets[i / swordCount],
                    position: sorted[i].Position));
            }

            return contacts;
        }

        private static double PolarAngle(Vec3 origin, Vec3 position)
        {
            double dx = position.X - origin.X;
            double dz = position.Z - origin.Z;
            if (Math.Abs(dx) < 1e-6 && Math.Abs(dz) < 1e-6)
                return double.PositiveInfinity; // 圆心重合，交给表面距离/ID 兜底

            double angle = Math.Atan2(dz, dx);
            return angle < 0 ? angle + Math.PI * 2 : angle;
        }
    }
}
