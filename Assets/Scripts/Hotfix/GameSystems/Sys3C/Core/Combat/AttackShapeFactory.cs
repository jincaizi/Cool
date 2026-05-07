namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public static class AttackShapeFactory
    {
        public static IAttackShape Create(AttackShapeConfig config,
            IEntityRegistry registry = null, EntityType targetType = EntityType.Monster)
        {
            if (config == null)
                return new ConeShape(2f, 120f, registry, targetType);

            return config.Type switch
            {
                ShapeType.Cone => new ConeShape(config.Range, config.Angle, registry, targetType),
                ShapeType.Circle => new CircleShape(config.Range, registry, targetType),
                ShapeType.Sector => new SectorShape(config.Range, config.AngleStart, config.AngleEnd, registry, targetType),
                ShapeType.Rect => new RectShape(config.Range, config.Width, config.StopAtFirst, registry, targetType),
                _ => new ConeShape(config.Range, config.Angle, registry, targetType),
            };
        }
    }
}
