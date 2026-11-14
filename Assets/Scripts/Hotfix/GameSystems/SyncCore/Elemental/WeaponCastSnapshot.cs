namespace Cool.SyncCore
{
    /// <summary>施法瞬间冻结的武器参数。换装不改变已接受施法的伤害、判定与冷却。</summary>
    public readonly struct WeaponCastSnapshot
    {
        public readonly long WeaponInstanceId;
        public readonly int TemplateId;
        public readonly int ElementId;
        public readonly uint EquipmentRevision;
        public readonly float BaseAttack;
        public readonly float AttackSpeedMultiplier;
        public readonly float SwordQiDamageMultiplier;

        public WeaponCastSnapshot(long weaponInstanceId, int templateId, int elementId,
            uint equipmentRevision, float baseAttack, float attackSpeedMultiplier,
            float swordQiDamageMultiplier)
        {
            WeaponInstanceId = weaponInstanceId;
            TemplateId = templateId;
            ElementId = elementId;
            EquipmentRevision = equipmentRevision;
            BaseAttack = baseAttack;
            AttackSpeedMultiplier = attackSpeedMultiplier;
            SwordQiDamageMultiplier = swordQiDamageMultiplier;
        }
    }
}
