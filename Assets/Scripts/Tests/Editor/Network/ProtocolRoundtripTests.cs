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
    }
}
