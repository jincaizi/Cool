using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer
{
    /// <summary>权威换装：归属/模板/版本校验 + 请求序号幂等。主循环线程独占。</summary>
    public sealed class EquipHandler
    {
        private const int RecentSeqWindow = 16;
        private readonly ElementalCatalogDefinition _catalog;

        public EquipHandler(ElementalCatalogDefinition catalog)
        {
            _catalog = catalog;
        }

        public async Task HandleAsync(Server3C server, IServerSession session, EquipWeaponRequest request)
        {
            long? playerId = server.GetPlayerId(session);
            var player = playerId.HasValue ? server.GetEntity(playerId.Value) : null;
            if (player == null) return;

            var confirm = ValidateAndApply(request, player);
            await server.SendToSessionAsync(session, MessageId.EquipWeaponConfirm, confirm);
        }

        public EquipWeaponConfirm ValidateAndApply(EquipWeaponRequest request, PlayerState player)
        {
            if (request.ClientEquipSeq != 0)
            {
                if (player.RecentEquipConfirms.TryGetValue(request.ClientEquipSeq, out var cached))
                    return cached;
                if (player.LastEquipSeq != 0 && request.ClientEquipSeq < player.LastEquipSeq)
                    return Reject(request, player, "stale-seq");
            }

            if (!player.WeaponInstanceTemplates.TryGetValue(request.WeaponInstanceId, out int templateId))
                return Reject(request, player, "not-owned");
            if (request.KnownEquipmentRevision != player.EquipmentRevision)
                return Reject(request, player, "stale-revision");

            var weapon = _catalog.Weapons.Find(w => w.TemplateId == templateId);
            if (weapon == null)
                return Reject(request, player, "unknown-template");

            player.EquippedWeaponInstanceId = request.WeaponInstanceId;
            player.EquippedWeaponTemplateId = templateId;
            player.EquipmentRevision++;
            player.BumpCombatVersion();

            var confirm = new EquipWeaponConfirm
            {
                ClientEquipSeq = request.ClientEquipSeq,
                Accepted = true,
                EquipmentRevision = player.EquipmentRevision,
                WeaponInstanceId = player.EquippedWeaponInstanceId,
                TemplateId = templateId,
                ElementId = weapon.ElementId
            };

            if (request.ClientEquipSeq != 0)
            {
                player.LastEquipSeq = request.ClientEquipSeq;
                player.RecentEquipConfirms[request.ClientEquipSeq] = confirm;
                Prune(player);
            }
            return confirm;
        }

        public static WeaponInventoryBaseline BuildBaseline(PlayerState player,
            ElementalCatalogDefinition catalog)
        {
            var baseline = new WeaponInventoryBaseline
            {
                LifeId = player.LifeId,
                EquipmentRevision = player.EquipmentRevision,
                WeaponInstanceId = player.EquippedWeaponInstanceId,
                TemplateId = player.EquippedWeaponTemplateId,
                ElementId = ElementOf(catalog, player.EquippedWeaponTemplateId)
            };

            foreach (var pair in player.WeaponInstanceTemplates)
            {
                baseline.Weapons.Add(new WeaponInstanceSummary
                {
                    InstanceId = pair.Key,
                    TemplateId = pair.Value,
                    ElementId = ElementOf(catalog, pair.Value)
                });
            }
            return baseline;
        }

        private EquipWeaponConfirm Reject(EquipWeaponRequest request, PlayerState player, string reason)
            => new EquipWeaponConfirm
            {
                ClientEquipSeq = request.ClientEquipSeq,
                Accepted = false,
                RejectReason = reason,
                EquipmentRevision = player.EquipmentRevision,
                WeaponInstanceId = player.EquippedWeaponInstanceId,
                TemplateId = player.EquippedWeaponTemplateId,
                ElementId = ElementOf(_catalog, player.EquippedWeaponTemplateId)
            };

        private static int ElementOf(ElementalCatalogDefinition? catalog, int templateId)
        {
            catalog ??= ElementalCatalogDefaults.Create();
            var weapon = catalog.Weapons.Find(w => w.TemplateId == templateId);
            return weapon?.ElementId ?? 0;
        }

        private static void Prune(PlayerState player)
        {
            if (player.RecentEquipConfirms.Count <= RecentSeqWindow) return;
            var keys = new List<uint>(player.RecentEquipConfirms.Keys);
            keys.Sort();
            int remove = player.RecentEquipConfirms.Count - RecentSeqWindow;
            for (int i = 0; i < remove; i++)
                player.RecentEquipConfirms.Remove(keys[i]);
        }
    }
}
