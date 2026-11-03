using System;
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer.Monster
{
    /// <summary>把模板技能条目转成 CastRequest（形状按表上限构建，入队前 ValidateShape）。</summary>
    public static class MonsterCastBuilder
    {
        public static bool TryBuild(MonsterState monster, SelectedSkill selected,
            Vector3 targetPosition, out CastRequest request)
        {
            var entry = selected.Entry;
            request = new CastRequest
            {
                SkillId = entry.SkillId,
                TargetPlayerId = monster.TargetId ?? 0L
            };

            if (entry.Projectile != null)
            {
                // 投射物：形状为零（ValidateShape 只要求 ShapeType 一致、上限不超）。
                request.Shape = new HitShape { ShapeType = entry.ShapeType };
                var spec = entry.Projectile;
                if (spec.Kind == ProjectileKind.CircleAtPoint)
                {
                    float dx = targetPosition.X - monster.Position.X;
                    float dz = targetPosition.Z - monster.Position.Z;
                    float distance = MathF.Sqrt(dx * dx + dz * dz);
                    float maxImpact = spec.MaxImpactDistance > 0f ? spec.MaxImpactDistance : distance;
                    float scale = distance > maxImpact && distance > 0.0001f ? maxImpact / distance : 1f;
                    request.ImpactX = monster.Position.X + dx * scale;
                    request.ImpactY = targetPosition.Y;
                    request.ImpactZ = monster.Position.Z + dz * scale;
                }
            }
            else if (entry.ShapeType == 1)
            {
                request.Shape = new HitShape
                {
                    ShapeType = 1,
                    AreaRadius = entry.MaxAreaRadius,
                    Range = 0f,
                    Angle = 0f
                };
            }
            else
            {
                request.Shape = new HitShape
                {
                    ShapeType = 0,
                    Range = MathF.Min(selected.Config.MaxRangeOf(entry), entry.MaxRange),
                    Angle = entry.MaxAngle
                };
            }

            return CombatRules.ValidateShape(request.Shape, entry);
        }
    }
}
