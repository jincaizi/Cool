using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 服务端权威状态 → 本地表现事件。按 (EntityId, GroupId) 维护投影表并 diff：
    /// 新增/移除（含同组替换）各发一次 <see cref="StatusEffectAppliedEvent"/> /
    /// <see cref="StatusEffectRemovedEvent"/>；同一投影重复到达不重复发事件。
    /// 同一实体换了渲染根（Proxy↔Full / 本地角色重建）时：旧根全部 Removed，新根全部 Applied。
    /// 纯逻辑类（不碰 Unity 组件），由 ClientNetworkHost 驱动。
    /// </summary>
    public sealed class AuthoritativeStatusVfx
    {
        /// <summary>实体当前染色目标与按组投影表</summary>
        private sealed class TargetState
        {
            public Transform Target;
            public readonly Dictionary<int, int> Groups = new Dictionary<int, int>();
        }

        /// <summary>entity → 当前目标 Transform + groupId → effectId</summary>
        private readonly Dictionary<long, TargetState> _entities =
            new Dictionary<long, TargetState>();

        /// <summary>EffectId/100 → 元素类型：1 冰 / 2 火 / 3 雷 / 4 风；未知按普通 Debuff。</summary>
        public static EffectType Map(int effectId) => (effectId / 100) switch
        {
            1 => EffectType.Freeze,    // 冰
            2 => EffectType.Burn,      // 火
            3 => EffectType.Stun,      // 雷
            4 => EffectType.Vulnerable,// 风
            _ => EffectType.Debuff
        };

        /// <summary>
        /// 应用一次权威状态投影（空列表 = 清空）。新增/移除/同组替换各发一次事件；
        /// 与已应用集合一致时不发任何事件（投影每帧携带完整列表）。
        /// 同一实体 target 变化（Proxy↔Full 换根 / 本地角色重建）时先对旧根发全部 Removed，
        /// 再按新根走常规 diff（即全部 Applied）——表现系统首次事件时冻结 Renderer 集合，
        /// 不重放的话染色会留在被隐藏/销毁的旧模型上。
        /// </summary>
        public void Apply(long entityId, Transform target, IList<StatusProjection> statuses)
        {
            if (target == null) return;

            if (!_entities.TryGetValue(entityId, out var state))
            {
                state = new TargetState { Target = target };
                _entities[entityId] = state;
            }
            else if (state.Target != target)
            {
                foreach (var pair in state.Groups)
                    EmitRemoved(entityId, state.Target, pair.Value);
                state.Groups.Clear();
                state.Target = target;
            }

            var applied = state.Groups;

            // 移除：投影里没有该组，或同组 EffectId 变了（替换 = 先移除旧的）
            var removed = new List<int>();
            foreach (var pair in applied)
            {
                bool found = false;
                int desiredEffectId = 0;
                if (statuses != null)
                {
                    for (int i = 0; i < statuses.Count; i++)
                    {
                        var status = statuses[i];
                        if (status == null || status.GroupId != pair.Key) continue;
                        desiredEffectId = status.EffectId;
                        found = true;
                        break;
                    }
                }
                if (found && desiredEffectId == pair.Value) continue;
                removed.Add(pair.Key);
                EmitRemoved(entityId, target, pair.Value);
            }
            for (int i = 0; i < removed.Count; i++)
                applied.Remove(removed[i]);

            // 新增（含同组替换后的新状态）
            if (statuses != null)
            {
                for (int i = 0; i < statuses.Count; i++)
                {
                    var status = statuses[i];
                    if (status == null || applied.ContainsKey(status.GroupId)) continue;
                    applied[status.GroupId] = status.EffectId;
                    EmitApplied(entityId, target, status.EffectId);
                }
            }

            if (applied.Count == 0) _entities.Remove(entityId);
        }

        /// <summary>实体离开视野/销毁：清表并移除全部染色。</summary>
        public void Remove(long entityId)
        {
            if (!_entities.TryGetValue(entityId, out var state)) return;
            _entities.Remove(entityId);
            foreach (var pair in state.Groups)
                EmitRemoved(entityId, state.Target, pair.Value);
        }

        private static void EmitApplied(long entityId, Transform target, int effectId)
        {
            EventBus.Emit(new StatusEffectAppliedEvent
            {
                EntityId = (int)entityId,
                Target = target,
                EffectType = Map(effectId),
                EffectId = effectId.ToString(),
                Duration = 0f,
                Stacks = 1,
            });
        }

        private static void EmitRemoved(long entityId, Transform target, int effectId)
        {
            EventBus.Emit(new StatusEffectRemovedEvent
            {
                EntityId = (int)entityId,
                Target = target,
                EffectType = Map(effectId),
                EffectId = effectId.ToString(),
            });
        }
    }
}
