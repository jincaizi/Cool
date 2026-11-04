using Hotfix.GameSystems.Monster;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.Monster
{
    public class AIBrainTargetValidityTests
    {
        private sealed class FakeDamageable : IDamageable
        {
            private readonly Transform _transform;
            private bool _isAlive = true;

            public FakeDamageable(Transform transform) => _transform = transform;

            /// <summary>模拟 Unity 销毁后的严格读取：目标已销毁时任何 IsAlive 访问都抛异常，
            /// 用来证明 ShouldDropTarget 先做 Transform 伪 null 检查。</summary>
            public bool ThrowOnAlive { get; set; }

            public bool IsAlive
            {
                get
                {
                    if (ThrowOnAlive)
                        throw new MissingReferenceException("IsAlive touched after target destroy");
                    return _isAlive;
                }
                set => _isAlive = value;
            }

            public Transform Transform => _transform;
            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection) { }
            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection, HitContact contact) { }
        }

        private GameObject _selfGo;
        private MonsterConfig _config;
        private AIContext _ctx;
        private AIBrain _brain;

        [SetUp]
        public void SetUp()
        {
            _selfGo = new GameObject("ai-self");
            _selfGo.transform.position = new Vector3(1000f, 0f, 1000f); // 远离 PhysicsRegistry 中的实体
            _config = ScriptableObject.CreateInstance<MonsterConfig>();
            _config.DetectRange = 10f;
            _config.LeaveRange = 15f;
            _ctx = new AIContext { Self = _selfGo.transform, Config = _config };
            _brain = new AIBrain(_ctx, null!, _config);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_selfGo);
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void test_ai_target_dead_is_dropped()
        {
            var targetGo = new GameObject("target");
            var fake = new FakeDamageable(targetGo.transform) { IsAlive = false };
            _ctx.Target = targetGo.transform;
            _ctx.TargetEntity = fake;

            _brain.TryFindTarget();

            Assert.IsTrue(_ctx.Target == null); // 静态类型 Transform → 走 Unity 伪 null 重载
            Assert.IsNull(_ctx.TargetEntity);
            Object.DestroyImmediate(targetGo);
        }

        [Test]
        public void test_ai_target_alive_in_range_is_kept()
        {
            var targetGo = new GameObject("target");
            targetGo.transform.position = _selfGo.transform.position + new Vector3(3f, 0f, 0f);
            var fake = new FakeDamageable(targetGo.transform);
            _ctx.Target = targetGo.transform;
            _ctx.TargetEntity = fake;

            _brain.TryFindTarget();

            Assert.AreSame(targetGo.transform, _ctx.Target);
            Assert.AreSame(fake, _ctx.TargetEntity);
            Object.DestroyImmediate(targetGo);
        }

        [Test]
        public void test_ai_target_out_of_leave_range_is_dropped()
        {
            var targetGo = new GameObject("target");
            targetGo.transform.position = _selfGo.transform.position + new Vector3(16f, 0f, 0f); // > 15
            var fake = new FakeDamageable(targetGo.transform);
            _ctx.Target = targetGo.transform;
            _ctx.TargetEntity = fake;

            _brain.TryFindTarget();

            Assert.IsTrue(_ctx.Target == null); // 静态类型 Transform → 走 Unity 伪 null 重载
            Assert.IsNull(_ctx.TargetEntity);
            Object.DestroyImmediate(targetGo);
        }

        [Test]
        public void test_ai_destroyed_target_is_dropped_without_exception()
        {
            var targetGo = new GameObject("target");
            var fake = new FakeDamageable(targetGo.transform) { ThrowOnAlive = true };
            _ctx.Target = targetGo.transform;
            _ctx.TargetEntity = fake;
            Object.DestroyImmediate(targetGo); // Transform 变 Unity 伪 null，接口引用仍非 null

            // 若实现先读 entity.IsAlive，这里会抛 MissingReferenceException
            Assert.DoesNotThrow(() => _brain.TryFindTarget());
            Assert.IsTrue(_ctx.Target == null); // 静态类型 Transform → 走 Unity 伪 null 重载
            Assert.IsNull(_ctx.TargetEntity);
        }
    }
}
