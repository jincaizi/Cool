namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public static class AttackShapeFactory
    {
        public static IAttackShape Create(AttackShapeConfig config)
        {
            if (config == null)
                return new ConeShape(2f, 120f);

            return config.Type switch
            {
                ShapeType.Cone => new ConeShape(config.Range, config.Angle),
                ShapeType.Circle => new CircleShape(config.Range),
                _ => new ConeShape(config.Range, config.Angle),
            };
        }
    }
}
