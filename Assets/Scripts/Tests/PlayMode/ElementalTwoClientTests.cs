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
            public KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public ClientNetworkManager Manager = null!;
            public CombatNetworkBridge Bridge = null!;
            public GameObject Root = null!;
            public readonly List<CastConfirm> Confirms = new List<CastConfirm>();
            public readonly List<ProjectileLaunchBroadcast> Launches = new List<ProjectileLaunchBroadcast>();
            public readonly List<ProjectileStopBroadcast> Stops = new List<ProjectileStopBroadcast>();
        }

        private static Ctx CreateClient(string name)
        {
            var root = new GameObject("ElementalTwoClient_" + name);
            var registry = new RemoteEntityRegistry(root.transform);
            var ctx = new Ctx { Root = root };
            ctx.Manager = new ClientNetworkManager(ctx.Adapter, registry, name);
            ctx.Bridge = new CombatNetworkBridge(ctx.Adapter, () => ctx.Manager.LocalPlayerId, registry);
            ctx.Adapter.MessageReceived += ctx.Bridge.OnMessage;
            ctx.Bridge.OnCastConfirm += c => ctx.Confirms.Add(c);
            ctx.Bridge.OnProjectileLaunch += l => ctx.Launches.Add(l);
            ctx.Bridge.OnProjectileStop += s => ctx.Stops.Add(s);
            return ctx;
        }

        [UnityTest]
        public IEnumerator TwoClients_Login_Equip_Cast_And_Share_Same_Launch()
        {
            var a = CreateClient("Elemental_A");
            var b = CreateClient("Elemental_B");
            try
            {
                var task = RunAsync(a, b);
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

        private static async Task<(bool, string)> RunAsync(Ctx a, Ctx b)
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

            // 释放 10002 后立刻在途换火剑：已接受施法必须保留冰剑快照。
            a.Bridge.NotifySkillCast(10002, new HitShape
            {
                ShapeType = 0, Range = 2.5f, Angle = 120f
            });
            a.Bridge.RequestEquip(fire.InstanceId);

            if (!await WaitUntil(a, b, () => a.Launches.Count > 0 && b.Launches.Count > 0, 6f))
                return (false, "both clients did not receive ProjectileLaunchBroadcast");

            var launchA = a.Launches.First(l => l.CastId != 0);
            var launchB = b.Launches.FirstOrDefault(l => l.CastId == launchA.CastId);
            if (launchB == null) return (false, "B received a different CastId");

            if (launchA.ElementId != 1) return (false, "in-flight equip changed launch element");
            if (Mathf.Abs(launchA.Speed - 18f) > 0.01f) return (false, "launch speed != 18");
            if (Mathf.Abs(launchA.Range - 12f) > 0.01f) return (false, "launch range != 12");

            var confirm = a.Confirms.FirstOrDefault(c => c.ClientCastSeq != 0);
            if (confirm == null || !confirm.Accepted) return (false, "cast was not confirmed");
            if (confirm.CastId != launchA.CastId) return (false, "CastConfirm CastId mismatch");
            if (confirm.EquipmentRevision != 1u) return (false, "cast did not keep ice equipment revision");

            if (!await WaitUntil(a, b, () =>
                    a.Bridge.Equipment.TemplateId == ElementalCatalogDefaults.FireSwordTemplateId, 5f))
                return (false, "fire equip was not confirmed after cast");

            return (true, "ok");
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
