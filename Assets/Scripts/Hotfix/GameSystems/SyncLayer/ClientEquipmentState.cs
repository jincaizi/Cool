using System;
using Cool.Protocol;
using Cool.SyncCore;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 客户端权威装备状态：按 EquipmentRevision 应用确认。接受与拒绝确认都携带服务器摘要；
    /// 较新的拒绝摘要用于把客户端从旧请求恢复到服务器当前装备。
    /// </summary>
    public sealed class ClientEquipmentState
    {
        private readonly ElementalCatalogDefinition _catalog;

        public ClientEquipmentState(ElementalCatalogDefinition? catalog = null)
        {
            _catalog = catalog ?? ElementalCatalogDefaults.Create();
        }

        public uint Revision { get; private set; }
        public uint LifeId { get; private set; } = 1;
        public long WeaponInstanceId { get; private set; }
        public int TemplateId { get; private set; }
        public int ElementId { get; private set; }
        public float AttackSpeedMultiplier { get; private set; } = 1f;
        public float BaseAttack { get; private set; } = 1f;
        public bool HasEquipment { get; private set; }

        public event Action<EquipWeaponConfirm>? Changed;

        /// <summary>登录基线：无条件初始化（版本 0 也可作为起点）。</summary>
        public void InitializeFromBaseline(WeaponInventoryBaseline baseline)
        {
            if (baseline == null) return;
            Revision = baseline.EquipmentRevision;
            LifeId = baseline.LifeId == 0 ? 1u : baseline.LifeId;
            WeaponInstanceId = baseline.WeaponInstanceId;
            TemplateId = baseline.TemplateId;
            ElementId = baseline.ElementId;
            HasEquipment = true;
            RefreshStats();
            Changed?.Invoke(new EquipWeaponConfirm
            {
                Accepted = true,
                EquipmentRevision = Revision,
                WeaponInstanceId = WeaponInstanceId,
                TemplateId = TemplateId,
                ElementId = ElementId
            });
        }

        /// <summary>应用确认；旧版本忽略。返回是否更新了状态。</summary>
        public bool TryApply(EquipWeaponConfirm confirm)
        {
            if (confirm == null) return false;
            if (confirm.EquipmentRevision < Revision) return false;
            if (confirm.EquipmentRevision == Revision && HasEquipment) return false;

            Revision = confirm.EquipmentRevision;
            WeaponInstanceId = confirm.WeaponInstanceId;
            TemplateId = confirm.TemplateId;
            ElementId = confirm.ElementId;
            HasEquipment = true;
            RefreshStats();
            Changed?.Invoke(confirm);
            return true;
        }

        public void SetLifeId(uint lifeId)
        {
            if (lifeId != 0) LifeId = lifeId;
        }

        public void Reset()
        {
            Revision = 0;
            LifeId = 1;
            WeaponInstanceId = 0;
            TemplateId = 0;
            ElementId = 0;
            AttackSpeedMultiplier = 1f;
            BaseAttack = 1f;
            HasEquipment = false;
        }

        private void RefreshStats()
        {
            var weapon = _catalog.Weapons.Find(w => w.TemplateId == TemplateId);
            AttackSpeedMultiplier = weapon?.AttackSpeedMultiplier ?? 1f;
            BaseAttack = weapon?.BaseAttack ?? 1f;
        }
    }
}
