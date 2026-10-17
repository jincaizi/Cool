using System.Text.RegularExpressions;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.Meteor;
using Hotfix.GameSystems.Skills.Runtime.Projectile;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 任务 4：联网时弹体伤害权在服务端——三个弹体的**本地伤害入口**必须短路
    /// （移动/生命周期/表现照旧）；单机态不变。
    ///
    /// 装置：`PhysicsRegistry` 注册一个假 <see cref="IDamageable"/> 统计 `TakeDamage` 次数。
    /// 注册表带"已注册实体补充"分支（`PhysicsRegistry.FindNearby`），因此假目标
    /// **不需要挂 Collider**——EditMode 下也不必等物理场景烘焙。
    ///
    /// 资产用 `PreRes/SkillsCfg` 里的真实配置，走真实 Spawner + Tick 驱动：
    /// 这样守卫接在哪一帧、弹体在联网下是否仍回池，都是生产代码的真实路径。
    /// </summary>
    public class NetworkModeDamageGuardTests
    {
        private const string SwordQiConfigPath = "Assets/PreRes/SkillsCfg/SwordQi_Heavy.asset";
        private const string FireballConfigPath = "Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset";
        private const string MeteorConfigPath = "Assets/PreRes/SkillsCfg/MeteorConfig_Fire.asset";

        /// <summary>假目标：只数伤害次数，不做任何表现/物理。</summary>
        private sealed class FakeTarget : MonoBehaviour, IDamageable
        {
            public int HitCount { get; private set; }
            public bool IsAlive => true;
            public Transform Transform => transform;

            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection) => HitCount++;

            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection, HitContact contact)
                => TakeDamage(damageData, hitDirection);
        }

        private NetworkMode _originalMode;
        private GameObject _targetGo;
        private FakeTarget _target;

        [SetUp]
        public void SetUp()
        {
            _originalMode = NetworkModeService.Load();
            _targetGo = new GameObject("GuardFakeTarget");
            _targetGo.transform.position = new Vector3(0f, 0f, 3f);
            _target = _targetGo.AddComponent<FakeTarget>();
            PhysicsRegistry.Instance.Register(_target, EntityType.Monster);
        }

        [TearDown]
        public void TearDown()
        {
            // PhysicsRegistry 是静态单例：漏 Unregister 会让已销毁的假目标在后续用例的
            // FindNearby 补充分支里抛 MissingReferenceException（同 SwordQiProjectileTests 记录）
            PhysicsRegistry.Instance.Unregister(_target);
            NetworkModeService.Save(_originalMode);
            Object.DestroyImmediate(_targetGo);

            // Spawner 的池也是静态单例：逐个清掉，防用例间串扰
            SwordQiSpawner.DisposeForTests();
            FireballSpawner.DisposeForTests();
            MeteorSpawner.DisposeForTests();

            // EditMode 下 Object.Destroy 不生效（见各驱动方法的 LogAssert）——Explode 实例化的
            // VFX 克隆会残留在测试场景；它们是根级 "(Clone)"，按此清理
            DestroyLeftoverVfx();
        }

        private static void DestroyLeftoverVfx()
        {
            foreach (var go in Object.FindObjectsOfType<GameObject>())
            {
                if (go == null) continue;
                if (go.transform.parent != null) continue;
                if (!go.name.EndsWith("(Clone)")) continue;
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------------------------------
        // 剑气（10002 SwordQi_Heavy）：判定带沿 18m/s 直线前进，目标在 z=3 的飞行路径上
        // ------------------------------------------------------------------------------------

        private static SwordQiProjectile FireSwordQi()
        {
            var cfg = UnityEditor.AssetDatabase.LoadAssetAtPath<SwordQiConfig>(SwordQiConfigPath);
            Assert.IsNotNull(cfg, SwordQiConfigPath + " 未找到");
            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);

            var qi = Object.FindObjectOfType<SwordQiProjectile>();
            Assert.IsNotNull(qi, "剑气未生成（Spawner/预制体接线核对）");
            // 飞满 13m > 射程 12m（10 小步）：途经 z=3 的目标（DetectRadius 2m）后回池
            for (int i = 0; i < 10; i++)
                qi.Tick(13f / cfg.Speed / 10f);
            return qi;
        }

        [Test]
        public void test_sword_qi_online_skips_local_damage()
        {
            NetworkModeService.Save(NetworkMode.Online);
            var qi = FireSwordQi();
            Assert.AreEqual(0, _target.HitCount, "联网：剑气伤害权在服务端，本地不得结算");
            Assert.IsFalse(qi.gameObject.activeSelf, "联网只跳伤害入口：飞满射程仍须回池");
        }

        [Test]
        public void test_sword_qi_single_player_applies_local_damage()
        {
            NetworkModeService.Save(NetworkMode.SinglePlayer);
            FireSwordQi();
            Assert.GreaterOrEqual(_target.HitCount, 1, "单机：剑气仍须本地结算");
        }

        // ------------------------------------------------------------------------------------
        // 火球（20003 Fireball_SkillQ）：抛物线；目标放在低空弹道经过处（z=3 时 y≈0.3，
        // 在 0.6m 命中半径内）。联网下仍会 Explode（VFX/回池），但不得造成伤害。
        // ------------------------------------------------------------------------------------

        private static FireballProjectile FireFireball()
        {
            // EditMode 下 Object.Destroy 非法：Explode 的 VFX 清理在生产（PlayMode）没问题，
            // 测试里会记一条固定错误——按预期日志消费掉，其他未预期错误仍会让用例失败
            LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));

            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<ProjectileSkillData>(FireballConfigPath);
            Assert.IsNotNull(data, FireballConfigPath + " 未找到");
            FireballSpawner.Spawn(data, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);

            var fb = Object.FindObjectOfType<FireballProjectile>();
            Assert.IsNotNull(fb, "火球未生成（Spawner/预制体接线核对）");
            // 20 × 0.02s = 0.4s，覆盖 z=3 处的命中窗口（t≈0.17s）
            for (int i = 0; i < 20; i++)
                fb.Tick(0.02f);
            return fb;
        }

        [Test]
        public void test_fireball_online_skips_local_damage()
        {
            NetworkModeService.Save(NetworkMode.Online);
            var fb = FireFireball();
            Assert.AreEqual(0, _target.HitCount, "联网：火球伤害权在服务端，本地不得结算");
            Assert.IsFalse(fb.gameObject.activeSelf, "联网只短路 ApplyDamage：命中仍须爆炸/回池");
        }

        [Test]
        public void test_fireball_single_player_applies_local_damage()
        {
            NetworkModeService.Save(NetworkMode.SinglePlayer);
            FireFireball();
            Assert.GreaterOrEqual(_target.HitCount, 1, "单机：火球仍须本地结算");
        }

        // ------------------------------------------------------------------------------------
        // 陨石（30001 MeteorConfig_Fire）：预警 0.8s 后落点 +1m 中心爆炸（半径 4m），
        // 目标站在落点上。联网下 ImpactVFX/回池照旧，但伤害循环整段跳过。
        // ------------------------------------------------------------------------------------

        private static MeteorStrike DropMeteor()
        {
            // 同火球：Explode 的 ImpactVFX 清理在 EditMode 下会记一条 "Destroy may not be called..."
            LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));

            var cfg = UnityEditor.AssetDatabase.LoadAssetAtPath<MeteorConfig>(MeteorConfigPath);
            Assert.IsNotNull(cfg, MeteorConfigPath + " 未找到");
            MeteorSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), new Vector3(0f, 0f, 3f));

            var strike = Object.FindObjectOfType<MeteorStrike>();
            Assert.IsNotNull(strike, "陨石未生成（Spawner/预制体接线核对）");
            strike.Tick(0.9f); // > WarnDuration 0.8s
            return strike;
        }

        [Test]
        public void test_meteor_online_skips_local_damage()
        {
            NetworkModeService.Save(NetworkMode.Online);
            var strike = DropMeteor();
            Assert.AreEqual(0, _target.HitCount, "联网：陨石伤害权在服务端，本地不得结算");
            Assert.IsFalse(strike.gameObject.activeSelf, "联网只跳过伤害循环：ImpactVFX 与回池照旧");
        }

        [Test]
        public void test_meteor_single_player_applies_local_damage()
        {
            NetworkModeService.Save(NetworkMode.SinglePlayer);
            DropMeteor();
            Assert.GreaterOrEqual(_target.HitCount, 1, "单机：陨石仍须本地结算");
        }
    }
}
