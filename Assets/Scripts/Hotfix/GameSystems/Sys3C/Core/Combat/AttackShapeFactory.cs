using Hotfix.GameSystems.Skills.Data;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public static class AttackShapeFactory
    {
        public static IAttackShape Create(ShapeBlock config,
            IEntityRegistry registry = null, EntityType targetType = EntityType.Monster)
        {
            if (config == null)
                return new ConeShape(2f, 120f, registry, targetType);

            return config.TargetType switch
            {
                TargetType.AOE_Cone => new ConeShape(config.Range, config.Angle, registry, targetType),
                TargetType.AOE_Circle => new CircleShape(config.Range, registry, targetType),
                TargetType.AOE_Sector => new SectorShape(config.Range, config.AngleStart, config.AngleEnd, registry, targetType),
                _ => new ConeShape(config.Range, config.Angle, registry, targetType),
            };
        }
    }
}
