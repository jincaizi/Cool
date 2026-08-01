using System.Reflection;
using Hotfix.GameSystems.Skills.Effect;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 测试辅助：反射设置私有字段 + IEffectTarget 假实现
    /// </summary>
    public static class TestHelpers
    {
        public static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"Field {fieldName} not found on {target.GetType().Name}");
            field.SetValue(target, value);
        }

        public class FakeTarget : IEffectTarget
        {
            public Transform transform { get; }

            public FakeTarget(Transform t) { transform = t; }

            public IEffectStats Stats => null;
            public IShieldSystem ShieldSystem => null;
            public IPhysicsSystem PhysicsSystem => null;
            public IStatusController StatusController => null;
            public void Heal(float amount) { }
        }
    }
}
