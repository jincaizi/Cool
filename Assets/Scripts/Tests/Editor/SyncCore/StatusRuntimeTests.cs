using System.Collections.Generic;
using System.Linq;
using Cool.SyncCore;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncCore
{
    public class StatusRuntimeTests
    {
        [Test]
        public void Apply_SameGroup_Refreshes_EndTick_And_Keeps_Burn_Source()
        {
            var catalog = ElementalCatalogDefaults.Create();
            var burn = catalog.Statuses.Find(s => s.EffectId == 201);
            var runtime = new StatusRuntime();
            runtime.Configure(catalog.Pvp);

            Assert.AreEqual(StatusApplyResult.Applied,
                runtime.Apply(burn, 1, 10002, 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true));

            // tick 15 先结算原排定跳（状态 tick 先于输入），再被新命中刷新。
            var dueAt15 = new List<ActiveStatus>();
            runtime.CollectDueDots(15, dueAt15);
            runtime.AdvanceDot(dueAt15[0], 15);

            Assert.AreEqual(StatusApplyResult.Refreshed,
                runtime.Apply(burn, 2, 10002, 8, tick: 15, targetIsPlayer: false, sourceIsPlayer: true));

            var active = runtime.Active.Single();
            Assert.AreEqual(1L, active.SourceId);       // 首次施加者保留
            Assert.AreEqual(60u, active.EndTick);       // 15 + 45
            Assert.AreEqual(30u, active.NextTick);      // 原下一跳 30 与新间隔 30 取较早
        }

        [Test]
        public void Burn_Ticks_On_Interval_And_Removes_After_EndTick_Tick()
        {
            var catalog = ElementalCatalogDefaults.Create();
            var burn = catalog.Statuses.Find(s => s.EffectId == 201);
            var runtime = new StatusRuntime();
            runtime.Configure(catalog.Pvp);
            runtime.Apply(burn, 1, 10002, 7, tick: 0, targetIsPlayer: false, sourceIsPlayer: true);

            var due = new List<ActiveStatus>();
            runtime.CollectDueDots(15, due);
            Assert.AreEqual(1, due.Count);
            runtime.AdvanceDot(due[0], 15);
            Assert.AreEqual(1, runtime.Count);

            due.Clear();
            runtime.CollectDueDots(45, due);
            Assert.AreEqual(1, due.Count);              // EndTick 仍结算
            runtime.AdvanceDot(due[0], 45);
            Assert.AreEqual(0, runtime.Count);
        }

        [Test]
        public void Control_Derives_SpeedScale_And_ActionLock()
        {
            var catalog = ElementalCatalogDefaults.Create();
            var ice = catalog.Statuses.Find(s => s.EffectId == 101);
            var thunder = catalog.Statuses.Find(s => s.EffectId == 301);
            var runtime = new StatusRuntime();
            runtime.Configure(catalog.Pvp);
            runtime.Apply(ice, 1, 10002, 7, 0, false, true);
            runtime.Apply(thunder, 1, 10002, 7, 0, false, true);

            runtime.DeriveControl(out float speed, out bool locked);
            Assert.AreEqual(0.7f, speed, 0.0001f);
            Assert.IsTrue(locked);
        }
    }
}
