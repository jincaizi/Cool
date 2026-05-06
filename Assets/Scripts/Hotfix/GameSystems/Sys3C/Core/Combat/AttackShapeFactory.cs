namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public static class AttackShapeFactory
    {
        public static IAttackShape Create(AttackShapeConfig config, IEntityRegistry registry = null)
        {
            if (config == null)
                return new ConeShape(2f, 120f, registry);

            return config.Type switch
            {
                ShapeType.Cone => new ConeShape(config.Range, config.Angle, registry),
                ShapeType.Circle => new CircleShape(config.Range, registry),
                _ => new ConeShape(config.Range, config.Angle, registry),
            };
        }
    }
}
