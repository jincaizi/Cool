using System.Collections;
using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using KcpNet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// 任务 12 三客户端旁观验收（A 打 B、C 旁观）：
    /// ① 同一发 CastRequest 的 DamageResult 必须同时到达施法者 A、受击者 B 与观察者 C，
    ///    且三端的伤害 / 服务端余血逐值一致（观察者扩播 + 单一权威掷骰）；
    /// ② C 的远程视图必须从快照落到 `Targetable`：原始 `Hp` 等于 DamageResult 余血，
    ///    显示口径 `CurrentHP` 等于钳制后的余血。服务端 `ApplyDamage` 允许负值
    ///    （Q 120/180 对 100 血必致负），而 `NetworkTargetable.CurrentHP` 是 UI 口径的
    ///    `Max(0, CeilToInt(Hp))`——所以断言同时覆盖原始值与显示投影，两者都证明
    ///    “C 看到的 B 的血量来自服务端”，而不是退化为客户端自算。
    /// 三客户端同出生点，等待 C 的 AOI 先看到 B 再施法。
    /// </summary>
    public class ThreeClientCombatConsistencyTests
    {
        private sealed class Ctx
        {
            public KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public ClientNetworkManager Manager = null!;
            public RemoteEntityRegistry Registry = null!;
            public readonly List<DamageResult> Damages = new List<DamageResult>();
        }

        [UnityTest]
        public IEnumerator Observer_Receives_Damage_And_Vitals_From_A_Vs_B()
        {
            var root = new GameObject("ThreeClientCombatRoot");
            var a = NewClient(root, "Obs_A");
            var b = NewClient(root, "Obs_B");
            var c = NewClient(root, "Obs_C");

            var task = RunAsync(a, b, c);
            float elapsed = 0f;
            while (!task.IsCompleted && elapsed < 120f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            try
            {
                Assert.IsTrue(task.IsCompleted, "three-client test timed out");
                var (ok, reason) = task.Result;
                Assert.IsTrue(ok, reason);
            }
            finally
            {
                a.Manager.Dispose();
                b.Manager.Dispose();
                c.Manager.Dispose();
                UnityEngine.Object.Destroy(root);
            }
        }

        private static Ctx NewClient(GameObject root, string name)
        {
            var ctx = new Ctx();
            ctx.Registry = new RemoteEntityRegistry(root.transform);
            ctx.Manager = new ClientNetworkManager(ctx.Adapter, ctx.Registry, name);
            ctx.Adapter.MessageReceived += (id, msg) =>
            {
                if (id == MessageId.DamageResult) ctx.Damages.Add((DamageResult)msg);
            };
            return ctx;
        }

        private static async System.Threading.Tasks.Task<(bool, string)> RunAsync(Ctx a, Ctx b, Ctx c)
        {
            a.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(a.Manager)) return (false, "A failed to enter world");
            b.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(b.Manager)) return (false, "B failed to enter world");
            c.Manager.ConnectAndLogin("127.0.0.1", 8000);
            if (!await WaitInWorld(c.Manager)) return (false, "C failed to enter world");

            // 三方同出生点；等 C 看到 B 再攻击（C 的 AOI 必须包含 B）。
            for (int i = 0; i < 40 && c.Registry.GetView(b.Manager.LocalPlayerId) == null; i++)
            {
                a.Manager.Update(0.05f);
                b.Manager.Update(0.05f);
                c.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(100);
            }
            if (c.Registry.GetView(b.Manager.LocalPlayerId) == null)
                return (false, "C never saw B");

            a.Adapter.Send(MessageId.CastRequest, new CastRequest
            {
                SkillId = 20001,
                TargetPlayerId = b.Manager.LocalPlayerId,
                Shape = new HitShape
                {
                    ShapeType = 0, Range = 3.5f, Angle = 120f,
                    InnerRadius = 0.8f, InnerAngle = 90f
                }
            });

            for (int i = 0; i < 100
                && (a.Damages.Count == 0 || b.Damages.Count == 0 || c.Damages.Count == 0); i++)
            {
                a.Manager.Update(0.05f);
                b.Manager.Update(0.05f);
                c.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(100);
            }

            var aHit = FindHit(a.Damages, b.Manager.LocalPlayerId);
            var bHit = FindHit(b.Damages, b.Manager.LocalPlayerId);
            var cHit = FindHit(c.Damages, b.Manager.LocalPlayerId);
            if (aHit == null || bHit == null || cHit == null)
                return (false, "caster/victim/observer did not all receive the DamageResult");

            if (Mathf.Abs(aHit.Damage - bHit.Damage) > 0.001f
                || Mathf.Abs(aHit.Damage - cHit.Damage) > 0.001f)
                return (false, "damage values differ across clients");

            if (Mathf.Abs(aHit.TargetRemainingHp - cHit.TargetRemainingHp) > 0.001f
                || Mathf.Abs(bHit.TargetRemainingHp - cHit.TargetRemainingHp) > 0.001f)
                return (false, "remaining hp differs across clients");

            // C 的权威 HP 必须从快照落到 Targetable。服务端余血可为负（100 血吃 Q 120/180），
            // Targetable.CurrentHP 是显示口径的 Max(0, CeilToInt(Hp))——两种口径都要对上。
            int expectedDisplayHp = Mathf.Max(0, Mathf.CeilToInt(cHit.TargetRemainingHp));
            for (int i = 0; i < 40; i++)
            {
                var view = c.Registry.GetView(b.Manager.LocalPlayerId);
                if (view != null
                    && Mathf.Abs(view.Hp - cHit.TargetRemainingHp) <= 0.001f
                    && view.Targetable.CurrentHP == expectedDisplayHp)
                    return (true, "ok");
                c.Manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(100);
            }

            return (false, $"C targetable HP != DamageResult HP ({cHit.TargetRemainingHp})");
        }

        private static HitEntry? FindHit(List<DamageResult> results, long targetId)
        {
            foreach (var result in results)
                foreach (var hit in result.Hits)
                    if (hit.TargetPlayerId == targetId) return hit;
            return null;
        }

        private static async System.Threading.Tasks.Task<bool> WaitInWorld(ClientNetworkManager manager)
        {
            float elapsed = 0f;
            while (manager.CurrentState != ClientNetworkManager.State.InWorld && elapsed < 10f)
            {
                manager.Update(0.05f);
                await System.Threading.Tasks.Task.Delay(50);
                elapsed += 0.05f;
            }
            return manager.CurrentState == ClientNetworkManager.State.InWorld;
        }
    }
}
