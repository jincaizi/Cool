using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 调试浮层读数行（B2 审查修复）。任务 10（手机实测）的**全部数据**都从这一行读
    /// （Full/Proxy/预算/Q/FPS），而竖屏手机约 390px 宽——原先的单行 620px 会把这段
    /// 整段截在屏外：质量档不上屏，这次跑的是档 2 还是 Ultra 就无法自证。
    /// `OnGUI` 不能在 EditMode 里驱动，所以读数行的内容与长度钉在它抽出的纯函数上
    /// （`ClientNetworkHost.FormatTierReadout`；`OnGUI` 只是把它塞进第二个 `GUI.Box`）。
    /// </summary>
    public class ClientNetworkHostOverlayTests
    {
        private const int PortraitLineCharBudget = 48;

        /// <summary>竖屏 390px、Unity 默认 IMGUI 字号 ≈7px/字符（读数行全 ASCII）⇒ 48 字符 ≈336px。
        /// 最坏取值（计数/预算三位数、FPS 三位数）也 < 48：`Full=100 Proxy=100 B=200  Q=5  FPS=144` = 38 字符。</summary>
        private static void AssertFitsPortraitWidth(string line)
            => Assert.LessOrEqual(line.Length, PortraitLineCharBudget,
                $"读数行必须能在约 390px 的竖屏上完整显示（≤{PortraitLineCharBudget} 字符），实长 {line.Length}：'{line}'");

        [Test]
        public void Tier_Readout_Carries_Every_Measurement_Field()
        {
            var root = new GameObject("OverlayReadoutRoot");
            var prefab = new GameObject("OverlayReadoutAvatar");
            prefab.AddComponent<Animator>();
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = prefab } });
            try
            {
                // 两个实体、无观察者 ⇒ 全 Full（读数非零，计数对得上）
                registry.ApplySnapshot(Snap(1), serverTicks: 1_000_000L);
                registry.ApplySnapshot(Snap(2), serverTicks: 1_000_000L);
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                var line = ClientNetworkHost.FormatTierReadout(registry, qualityLevel: 2, fps: 61.4f);

                Assert.AreEqual("Full=2 Proxy=0 B=20  Q=2  FPS=61", line,
                    "读数行必须恰好携带 Full/Proxy/预算(B)/质量档(Q)/FPS 五项——"
                    + "少任何一项实测都无法自证（质量档尤其：任务 10 要求记录档位）");
                AssertFitsPortraitWidth(line);
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
            }
        }

        /// <summary>非联网态（registry 未建/未接线）= 不能炸，且质量档与 FPS 仍在（`Q=` 判据与联网态同一处）。</summary>
        [Test]
        public void Tier_Readout_Without_A_Registry_Still_Reports_Quality_And_Fps()
        {
            var line = ClientNetworkHost.FormatTierReadout(registry: null, qualityLevel: 5, fps: 30f);

            Assert.AreEqual("Q=5  FPS=30", line, "未联网只出质量档与 FPS，不带档位计数（不炸）");
            AssertFitsPortraitWidth(line);
        }

        private static EntitySnapshot Snap(long id)
            => new EntitySnapshot
            {
                PlayerId = id,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind),
                X = 1f, Y = 0f, Z = 2f, Kind = (byte)EntityKind.Player,
                Tick = 1
            };
    }
}
