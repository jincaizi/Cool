using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation.StateBehaviours
{
    /// <summary>
    /// 动画完成回调按 **Animator 实例** 注册。控制器资产被本地与远程角色共用；
    /// 旧的静态回调会让远程动画调用本地 FSMManager（远程攻击打断本地施法、
    /// 远程受击提前完成本地 HitFSM）。远程 Animator 查不到条目 → 天然不触发。
    /// </summary>
    public static class AnimationCallbackRegistry
    {
        internal sealed class Entry
        {
            public Action<string>? Attack;
            public Action<string>? Hit;
            public bool HasTriggeredHitComplete;
            public float LastNormalizedTime;
        }

        private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>();

        public static void Register(Animator animator, Action<string>? attack, Action<string>? hit)
        {
            if (animator == null) return;
            Entries[animator.GetInstanceID()] = new Entry
            {
                Attack = attack,
                Hit = hit,
                HasTriggeredHitComplete = false,
                LastNormalizedTime = 0f
            };
        }

        public static void Unregister(Animator animator)
        {
            if (animator == null) return;
            Entries.Remove(animator.GetInstanceID());
        }

        internal static bool TryGet(Animator animator, out Entry entry)
        {
            entry = null!;
            return animator != null && Entries.TryGetValue(animator.GetInstanceID(), out entry!);
        }

        public static void ClearForTests() => Entries.Clear();
    }
}
