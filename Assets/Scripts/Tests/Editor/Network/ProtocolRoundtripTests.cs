using System;
using NUnit.Framework;
using Cool.Protocol;

namespace Hotfix.GameSystems.Tests.Network
{
    /// <summary>
    /// 协议层回归测试（Unity 编辑器环境版本）
    /// 与 Server.Tests 的 CodecRoundtripTests 断言语义一致
    /// </summary>
    public class ProtocolRoundtripTests
    {
        [Test]
        public void Roundtrip_EchoResponse_PreservesFields()
        {
            var source = new EchoResponse
            {
                ClientTimestamp = 123456789L,
                ServerTimestamp = 987654321L
            };

            var encoded = MessageCodec.EncodeToArray(MessageId.EchoResponse, source);
            var (_, decoded) = MessageCodec.Decode(encoded);

            // Unity 内置 NUnit(3.5.1) 的 Assert.IsAssignableFrom<T> 返回 void，改用等价断言
            Assert.That(decoded, Is.InstanceOf<EchoResponse>());
            var response = (EchoResponse)decoded;
            Assert.AreEqual(source.ClientTimestamp, response.ClientTimestamp);
            Assert.AreEqual(source.ServerTimestamp, response.ServerTimestamp);
        }

        [Test]
        public void Every_MessageId_HasRegisteredType()
        {
            foreach (MessageId id in Enum.GetValues(typeof(MessageId)))
            {
                Assert.IsTrue(
                    MessageTypeRegistry.IsRegistered(id),
                    $"MessageId.{id} 未在 MessageTypeRegistry 注册");
            }
        }

        [Test]
        public void Roundtrip_CancelCastRequest_Preserves_SkillId()
        {
            var source = new CancelCastRequest { SkillId = 20002 };
            var encoded = MessageCodec.EncodeToArray(MessageId.CancelCastRequest, source);
            var (_, decoded) = MessageCodec.Decode(encoded);

            Assert.That(decoded, Is.InstanceOf<CancelCastRequest>());
            Assert.AreEqual(20002, ((CancelCastRequest)decoded).SkillId);
        }

        /// <summary>格挡减伤服务端化的输入侧（tag 8）：防御标志必须真的上线上网。</summary>
        [Test]
        public void Roundtrip_PositionSyncRequest_Preserves_Defending_Flag()
        {
            var source = new PositionSyncRequest
            {
                X = 1.5f, Y = 0.25f, Z = -3.75f,
                Rotation = 90f, Speed = 4.2f,
                IsDefending = true,
                Timestamp = 555L, Sequence = 7U
            };

            var encoded = MessageCodec.EncodeToArray(MessageId.PositionSyncRequest, source);
            var (_, decoded) = MessageCodec.Decode(encoded);

            Assert.That(decoded, Is.InstanceOf<PositionSyncRequest>());
            var sync = (PositionSyncRequest)decoded;
            Assert.AreEqual(1.5f, sync.X, 0.001f);   // 既有字段回归（累积式：只加不删）
            Assert.AreEqual(90f, sync.Rotation, 0.001f);
            Assert.AreEqual(7U, sync.Sequence);
            Assert.IsTrue(sync.IsDefending,
                "IsDefending 未往返——服务端永远看到 false，格挡减伤整条链静默失效");
        }

        /// <summary>格挡标志的下行侧（HitEntry tag 7）：客户端据此走格挡受击表现。</summary>
        [Test]
        public void Roundtrip_DamageResult_Preserves_Blocked_Flag()
        {
            var source = new DamageResult
            {
                SourcePlayerId = 2_000_001, SkillId = 50001,
                Hits =
                {
                    new HitEntry
                    {
                        TargetPlayerId = 7, Damage = 3f, IsCritical = false,
                        IsBlocked = true, TargetRemainingHp = 97f
                    }
                }
            };

            var encoded = MessageCodec.EncodeToArray(MessageId.DamageResult, source);
            var (_, decoded) = MessageCodec.Decode(encoded);

            Assert.That(decoded, Is.InstanceOf<DamageResult>());
            var result = (DamageResult)decoded;
            Assert.AreEqual(1, result.Hits.Count);
            Assert.AreEqual(3f, result.Hits[0].Damage, 0.001f);  // 减免后的伤害随包下传
            Assert.AreEqual(97f, result.Hits[0].TargetRemainingHp, 0.001f);
            Assert.IsTrue(result.Hits[0].IsBlocked,
                "IsBlocked 未往返——客户端无法区分格挡/普通受击表现");
        }

        [Test]
        public void Roundtrip_Equip_Request_And_Confirm()
        {
            var request = (EquipWeaponRequest)MessageCodec.Decode(MessageCodec.EncodeToArray(
                MessageId.EquipWeaponRequest,
                new EquipWeaponRequest { ClientEquipSeq = 7, WeaponInstanceId = 42, KnownEquipmentRevision = 3 })).message;
            Assert.AreEqual(7u, request.ClientEquipSeq);
            Assert.AreEqual(42L, request.WeaponInstanceId);
            Assert.AreEqual(3u, request.KnownEquipmentRevision);

            var confirm = (EquipWeaponConfirm)MessageCodec.Decode(MessageCodec.EncodeToArray(
                MessageId.EquipWeaponConfirm,
                new EquipWeaponConfirm
                {
                    ClientEquipSeq = 7, Accepted = false, RejectReason = "stale-revision",
                    EquipmentRevision = 4, WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
                })).message;
            Assert.IsFalse(confirm.Accepted);
            Assert.AreEqual("stale-revision", confirm.RejectReason);
            Assert.AreEqual(4u, confirm.EquipmentRevision);
            Assert.AreEqual(1, confirm.ElementId);
        }

        [Test]
        public void Roundtrip_Login_Weapon_Baseline_Empty_And_Full()
        {
            var empty = (LoginResponse)MessageCodec.Decode(MessageCodec.EncodeToArray(
                MessageId.LoginResponse,
                new LoginResponse { Success = true, WeaponInventory = new WeaponInventoryBaseline() })).message;
            Assert.IsNotNull(empty.WeaponInventory);
            Assert.AreEqual(0, empty.WeaponInventory!.Weapons.Count);

            var full = (LoginResponse)MessageCodec.Decode(MessageCodec.EncodeToArray(
                MessageId.LoginResponse,
                new LoginResponse
                {
                    Success = true,
                    ElementalCatalogFingerprint = "abc",
                    WeaponInventory = new WeaponInventoryBaseline
                    {
                        LifeId = 2, EquipmentRevision = 3, WeaponInstanceId = 42,
                        TemplateId = 90002, ElementId = 1,
                        Weapons =
                        {
                            new WeaponInstanceSummary { InstanceId = 42, TemplateId = 90002, ElementId = 1 },
                            new WeaponInstanceSummary { InstanceId = 43, TemplateId = 90003, ElementId = 2 }
                        }
                    }
                })).message;
            Assert.AreEqual("abc", full.ElementalCatalogFingerprint);
            Assert.AreEqual(2, full.WeaponInventory!.Weapons.Count);
            Assert.AreEqual(43L, full.WeaponInventory.Weapons[1].InstanceId);
        }

        [Test]
        public void Roundtrip_CastId_AttackSpeed_And_CastConfirm()
        {
            var broadcast = (CastBroadcast)MessageCodec.Decode(MessageCodec.EncodeToArray(
                MessageId.CastBroadcast,
                new CastBroadcast
                {
                    SkillId = 10002, CasterPlayerId = 1, CastId = 99, CasterLifeId = 2,
                    AttackSpeedMultiplier = 1.25f, ElementId = 1, EquipmentRevision = 4
                })).message;
            Assert.AreEqual(99ul, broadcast.CastId);
            Assert.AreEqual(1.25f, broadcast.AttackSpeedMultiplier, 0.001f);
            Assert.AreEqual(1, broadcast.ElementId);

            var confirm = (CastConfirm)MessageCodec.Decode(MessageCodec.EncodeToArray(
                MessageId.CastConfirm,
                new CastConfirm
                {
                    ClientCastSeq = 12, Accepted = true, CastId = 99, SkillId = 10002,
                    CasterLifeId = 2, EquipmentRevision = 4, AttackSpeedMultiplier = 1.25f,
                    AcceptedTick = 321
                })).message;
            Assert.AreEqual(12u, confirm.ClientCastSeq);
            Assert.AreEqual(99ul, confirm.CastId);
            Assert.AreEqual(321u, confirm.AcceptedTick);
        }

        [Test]
        public void Roundtrip_Projectile_Launch_And_Stop()
        {
            var launch = (ProjectileLaunchBroadcast)MessageCodec.Decode(MessageCodec.EncodeToArray(
                MessageId.ProjectileLaunchBroadcast,
                new ProjectileLaunchBroadcast
                {
                    CastId = 77, CasterPlayerId = 1, CasterLifeId = 2, CombatSceneId = "room-a",
                    LaunchTick = 123, OriginX = 1f, OriginY = 2f, OriginZ = 3f,
                    Yaw = 45f, Speed = 18f, Range = 12f, ElementId = 1,
                    SkillId = 10002, ServerTimestamp = 999
                })).message;
            Assert.AreEqual(77ul, launch.CastId);
            Assert.AreEqual("room-a", launch.CombatSceneId);
            Assert.AreEqual(18f, launch.Speed, 0.001f);
            Assert.AreEqual(12f, launch.Range, 0.001f);

            var stop = (ProjectileStopBroadcast)MessageCodec.Decode(MessageCodec.EncodeToArray(
                MessageId.ProjectileStopBroadcast,
                new ProjectileStopBroadcast
                {
                    CastId = 77, CasterLifeId = 2, CombatSceneId = "room-a",
                    StopTick = 130, Reason = ProjectileStopReason.Blocked, StopX = 4f, StopZ = 5f
                })).message;
            Assert.AreEqual(77ul, stop.CastId);
            Assert.AreEqual(ProjectileStopReason.Blocked, stop.Reason);
            Assert.AreEqual(4f, stop.StopX);
        }

        [Test]
        public void Roundtrip_HitEntry_Cast_Identity_Fields()
        {
            var result = (DamageResult)MessageCodec.Decode(MessageCodec.EncodeToArray(
                MessageId.DamageResult,
                new DamageResult
                {
                    Hits =
                    {
                        new HitEntry
                        {
                            TargetPlayerId = 3, CastId = 77, EventSeq = 5, EventId = 9001,
                            DamageKind = DamageKind.Direct, TargetLifeId = 2, CombatVersion = 6
                        }
                    }
                })).message;

            var hit = result.Hits[0];
            Assert.AreEqual(77ul, hit.CastId);
            Assert.AreEqual(5u, hit.EventSeq);
            Assert.AreEqual(9001ul, hit.EventId);
            Assert.AreEqual(DamageKind.Direct, hit.DamageKind);
            Assert.AreEqual(2u, hit.TargetLifeId);
            Assert.AreEqual(6u, hit.CombatVersion);
        }
    }
}
