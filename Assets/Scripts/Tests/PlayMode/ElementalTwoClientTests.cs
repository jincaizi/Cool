using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cool.Protocol;
using Cool.SyncCore;
using Hotfix.GameSystems.SyncLayer;
using KcpNet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WebSocketNet;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// 属性剑气 v1 真实双客户端冒烟：连接本机服务端（默认 127.0.0.1:8123），
    /// 登录取得武器基线 → 权威换装冰剑 → 在途换火剑并释放 10002 →
    /// 两端收到同一 CastId 的发射描述，且发射元素保留冰剑快照。
    /// 运行前需先启动：dotnet Server/bin/Debug/net8.0/Server3C.dll 8123
    /// </summary>
    public class ElementalTwoClientTests
    {
        private const int Port = 8123;

        private sealed class Ctx
        {
            public INetworkClient Adapter = null!;
            public ClientNetworkManager Manager = null!;
            public CombatNetworkBridge Bridge = null!;
            public GameObject Root = null!;
            public readonly List<CastConfirm> Confirms = new List<CastConfirm>();
            public readonly List<EquipWeaponConfirm> EquipResults = new List<EquipWeaponConfirm>();
            public readonly List<ProjectileLaunchBroadcast> Launches = new List<ProjectileLaunchBroadcast>();
            public readonly List<ProjectileStopBroadcast> Stops = new List<ProjectileStopBroadcast>();
        }

        private static Ctx CreateClient(string name, Func<INetworkClient> createAdapter)
        {
            var root = new GameObject("ElementalTwoClient_" + name);
            var registry = new RemoteEntityRegistry(root.transform);
            var ctx = new Ctx { Root = root, Adapter = createAdapter() };
            ctx.Manager = new ClientNetworkManager(ctx.Adapter, registry, name);
            ctx.Bridge = new CombatNetworkBridge(ctx.Adapter, () => ctx.Manager.LocalPlayerId, registry);
            ctx.Adapter.MessageReceived += ctx.Bridge.OnMessage;
            ctx.Bridge.OnCastConfirm += c => ctx.Confirms.Add(c);
            ctx.Bridge.OnEquipResult += c => ctx.EquipResults.Add(c);
            ctx.Bridge.OnProjectileLaunch += l => ctx.Launches.Add(l);
            ctx.Bridge.OnProjectileStop += s => ctx.Stops.Add(s);
            return ctx;
        }

        [UnityTest]
        public IEnumerator TwoClients_Login_Equip_Cast_And_Share_Same_Launch()
            => RunScenario(() => new KcpNetClientAdapter());

        [UnityTest]
        public IEnumerator WebSocketClients_Login_Equip_Cast_And_Share_Same_Launch()
            => RunScenario(() => new WebSocketClientAdapter(new DotNetWebSocketChannel()));

        private static IEnumerator RunScenario(Func<INetworkClient> createAdapter)
        {
            var a = CreateClient("Elemental_A", createAdapter);
            var b = CreateClient("Elemental_B", createAdapter);
            try
            {
                var task = RunAsync(a, b, createAdapter);
                float elapsed = 0f;
                while (!task.IsCompleted && elapsed < 60f)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                Assert.IsTrue(task.IsCompleted, "elemental two-client test timed out");
                var (ok, reason) = task.Result;
                Assert.IsTrue(ok, reason);
            }
            finally
            {
                a.Manager.Dispose();
                b.Manager.Dispose();
                UnityEngine.Object.Destroy(a.Root);
                UnityEngine.Object.Destroy(b.Root);
            }
        }

        private static async Task<(bool, string)> RunAsync(Ctx a, Ctx b, Func<INetworkClient> createAdapter)
        {
            a.Manager.ConnectAndLogin("127.0.0.1", Port);
            if (!await WaitInWorld(a)) return (false, "A failed to enter world");

            b.Manager.ConnectAndLogin("127.0.0.1", Port);
            if (!await WaitInWorld(b)) return (false, "B failed to enter world");

            var baseline = a.Manager.WeaponInventory;
            if (baseline == null) return (false, "A received no elemental weapon baseline");
            if ((a.Manager.EnabledFeatures & ProtocolFeatures.ElementalSwordQiV1) == 0)
                return (false, "server did not enable ElementalSwordQiV1");
            if (baseline.Weapons.Count != 5) return (false, "baseline weapon count != 5");

            a.Bridge.Equipment.InitializeFromBaseline(baseline);
            var ice = baseline.Weapons.First(w => w.TemplateId == ElementalCatalogDefaults.IceSwordTemplateId);
            var fire = baseline.Weapons.First(w => w.TemplateId == ElementalCatalogDefaults.FireSwordTemplateId);

            a.Bridge.RequestEquip(ice.InstanceId);
            if (!await WaitUntil(a, b, () => a.Bridge.Equipment.Revision >= 1, 5f))
                return (false, "ice equip was not confirmed");
            if (a.Bridge.Equipment.TemplateId != ElementalCatalogDefaults.IceSwordTemplateId)
                return (false, "client equipment did not switch to ice");

            // 角色动作期间拒绝换剑；剑气仍按施法冻结的冰武器发射。
            a.Bridge.NotifySkillCast(10002, new HitShape
            {
                ShapeType = 0, Range = 2.5f, Angle = 120f
            });
            uint busySeq = a.Bridge.RequestEquip(fire.InstanceId);
            if (!await WaitUntil(a, b, () => a.EquipResults.Any(c => c.ClientEquipSeq == busySeq
                    && !c.Accepted && c.RejectReason == "action-busy"), 5f))
                return (false, "equip did not reject during the attack");

            if (!await WaitUntil(a, b, () => a.Confirms.Any(c => c.ClientCastSeq == 1), 5f))
                return (false, "cast confirmation did not arrive");
            var confirm = a.Confirms.First(c => c.ClientCastSeq == 1);
            if (!confirm.Accepted || confirm.CastId == 0)
                return (false, "cast was not accepted: " + confirm.RejectReason);
            if (!await WaitUntil(a, b, () =>
                    a.Launches.Any(l => l.CastId == confirm.CastId)
                    && b.Launches.Any(l => l.CastId == confirm.CastId), 6f))
                return (false, "both clients did not receive the accepted cast's launch");

            var launchA = a.Launches.First(l => l.CastId == confirm.CastId);
            var launchB = b.Launches.First(l => l.CastId == confirm.CastId);

            if (launchA.ElementId != 1 || launchB.ElementId != 1)
                return (false, "in-flight equip changed launch element");
            if (Mathf.Abs(launchA.Speed - 18f) > 0.01f) return (false, "launch speed != 18");
            if (Mathf.Abs(launchA.Range - 12f) > 0.01f) return (false, "launch range != 12");

            if (confirm.EquipmentRevision != 1u) return (false, "cast did not keep ice equipment revision");

            if (!await WaitUntil(a, b, () => a.Manager.ServerTick >= confirm.AcceptedTick + 11u, 5f))
                return (false, "attack recovery did not finish");
            a.Bridge.RequestEquip(fire.InstanceId);
            if (!await WaitUntil(a, b, () =>
                    a.Bridge.Equipment.TemplateId == ElementalCatalogDefaults.FireSwordTemplateId, 5f))
                return (false, "fire equip was not confirmed after cast");

            long previousPlayerId = a.Manager.LocalPlayerId;
            await a.Adapter.DisconnectAsync("elemental-reconnect");
            var reconnected = CreateClient("Elemental_A", createAdapter);
            try
            {
                reconnected.Manager.ConnectAndLogin("127.0.0.1", Port);
                if (!await WaitInWorld(reconnected)) return (false, "A failed to reconnect");
                var newBaseline = reconnected.Manager.WeaponInventory;
                if (reconnected.Manager.LocalPlayerId == previousPlayerId || newBaseline == null
                    || newBaseline.TemplateId != ElementalCatalogDefaults.DefaultSwordTemplateId
                    || newBaseline.EquipmentRevision != 0 || newBaseline.Weapons.Count != 5)
                    return (false, "new session did not reset to the default sword");
                return (true, "ok");
            }
            finally
            {
                reconnected.Manager.Dispose();
                UnityEngine.Object.Destroy(reconnected.Root);
            }
        }

        private static async Task<bool> WaitInWorld(Ctx ctx)
        {
            for (int i = 0; i < 100; i++)
            {
                ctx.Manager.Update(0.05f);
                ctx.Bridge.Update();
                if (ctx.Manager.CurrentState == ClientNetworkManager.State.InWorld) return true;
                await Task.Delay(50);
            }
            return false;
        }

        private static async Task<bool> WaitUntil(Ctx a, Ctx b, Func<bool> condition, float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                a.Manager.Update(0.05f);
                b.Manager.Update(0.05f);
                a.Bridge.Update();
                b.Bridge.Update();
                if (condition()) return true;
                await Task.Delay(50);
                elapsed += 0.05f;
            }
            return condition();
        }
    }
}
