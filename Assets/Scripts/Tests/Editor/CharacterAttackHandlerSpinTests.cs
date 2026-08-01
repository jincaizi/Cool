using System.Reflection;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class CharacterAttackHandlerSpinTests
    {
        private class FakeDeadTarget : IDamageable, ITargetable
        {
            public bool IsAlive => false;
            public Transform Transform => null;
            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection) { }
            public event System.Action<float, int, int> OnHPChanged;
            public event System.Action OnDeath;
            public Vector3 WorldPosition => Vector3.zero;
            public float SelectionRingYOffset => 0f;
            public string DisplayName => "Dead";
            public int Level => 1;
            public Sprite Portrait => null;
            public float HPPercent => 0f;
            public int CurrentHP => 0;
            public int MaxHP => 100;
        }

        private static object GetTarget(CharacterAttackHandler handler)
        {
            var field = typeof(CharacterAttackHandler)
                .GetField("_currentTarget", BindingFlags.NonPublic | BindingFlags.Instance);
            return field.GetValue(handler);
        }

        [Test]
        public void SelectTarget_DeadTarget_IsIgnored()
        {
            var go = new GameObject("AttackHandler");
            var handler = go.AddComponent<CharacterAttackHandler>();
            try
            {
                handler.SelectTarget(new FakeDeadTarget());
                Assert.IsNull(GetTarget(handler), "死亡目标不应被选中（否则光环随尸体销毁）");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
