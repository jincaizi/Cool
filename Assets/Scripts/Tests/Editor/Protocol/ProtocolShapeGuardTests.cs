using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace Cool.Protocol.Tests
{
    /// <summary>
    /// 形状门禁：Cool.Protocol 的消息成员不得声明为 List&lt;T&gt;。
    ///
    /// 为什么需要它：protobuf-net 判定"成员是不是集合"时会跑 tuple 探测，其中为识别
    /// init-only setter 会调 ReturnParameter.GetRequiredCustomModifiers()，而 IL2CPP 的
    /// icall RuntimeParameterInfo::GetTypeModifiers 未实现 —— 运行时抛 NotSupportedException。
    /// 症状是**静默的**（解码失败、列表恒空），而现有全部测试都跑在 Mono/.NET 上，抓不到
    /// 这一类。所以用一条反射断言把这个形状规则钉住：IList&lt;T&gt; 不触发，List&lt;T&gt; 触发。
    ///
    /// 中招条件是成员类型恰好是 List&lt;T&gt;（三个公开构造 + 可写 Capacity/Item 足以过 tuple
    /// 探测的门槛）；数组、接口类型、无参构造的 List 子类都过不了，故本断言只针对 List&lt;T&gt; 本身。
    /// </summary>
    public class ProtocolShapeGuardTests
    {
        private static readonly Type ListOfT = typeof(List<>);

        [Test]
        public void ProtoMembers_Are_Never_Declared_As_List()
        {
            var offenders = new List<string>();
            int inspected = 0;

            foreach (var type in typeof(MessageCodec).Assembly.GetTypes())
            {
                if (type.IsEnum || type.IsInterface || type.IsGenericTypeDefinition) continue;

                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                         | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

                foreach (var member in type.GetMembers(flags))
                {
                    if (!HasProtoMember(member)) continue;

                    Type memberType = member is PropertyInfo property ? property.PropertyType
                                    : member is FieldInfo field ? field.FieldType
                                    : null;
                    if (memberType == null) continue;

                    inspected++;
                    if (!memberType.IsGenericType || memberType.GetGenericTypeDefinition() != ListOfT) continue;

                    offenders.Add(type.Name + "." + member.Name + " : " + memberType);
                }
            }

            // 防空转：反射一旦扫不到成员（改程序集、改命名空间），下面的断言会变成恒真
            Assert.Greater(inspected, 0, "未扫到任何 [ProtoMember] 成员——反射目标失效，本门禁已失效");

            Assert.IsEmpty(offenders,
                "以下 [ProtoMember] 成员声明为 List<T>——IL2CPP 构建里 protobuf-net 的 tuple 探测会抛 "
                + "NotSupportedException（RuntimeParameterInfo::GetTypeModifiers 未实现），解码静默失败。"
                + "请把声明改成 IList<T>（初始化器 new List<T>() 可保留，线上字节格式不变）：\n  "
                + string.Join("\n  ", offenders));
        }

        [Test]
        public void EntityKind_Carries_PlayerBot()
        {
            // bot 与真人同类：伤害路径与渲染路径都不分叉（D-N8；D-P5-7 的"渲染成占位"已被推翻）
            Assert.AreEqual(2, (byte)Cool.Protocol.EntityKind.PlayerBot,
                "PlayerBot 必须是 2——0/1 是已发布的 wire 契约，不能改");
        }

        [Test]
        public void EntitySnapshot_Carries_Dead_With_A_Mask_Bit()
        {
            var prop = typeof(Cool.Protocol.EntitySnapshot).GetProperty("Dead");
            Assert.IsNotNull(prop, "EntitySnapshot 必须有 Dead");
            Assert.AreEqual(typeof(bool), prop!.PropertyType);
            Assert.AreEqual(12, GetProtoMemberTag(prop!),
                "Dead 必须真的挂在线上（[ProtoMember(12)]）——缺 attribute 会静默不序列化；"
                + "号写错 protobuf-net 也不报错（它只报重号），观察者会恒读到\"活着\"");
            // 值必须是枚举的底层类型（ushort）——Mono 的 Enum.IsDefined 对 int 会抛
            // ArgumentException（"Enum underlying type and the object must be same type"）
            Assert.IsTrue(System.Enum.IsDefined(typeof(Cool.Protocol.EntityFieldMask), (ushort)(1 << 6)),
                "EntityFieldMask 必须新增 1 << 6（只加不删：新字段追加高位）");
        }

        [Test]
        public void EntityKind_Existing_Values_Are_Pinned()
        {
            // 只加不删的机器化防线：0/1 是已发布的 wire 契约，挪一位老客户端就把种类解反
            Assert.AreEqual(0, (byte)Cool.Protocol.EntityKind.Player);
            Assert.AreEqual(1, (byte)Cool.Protocol.EntityKind.Npc);
        }

        [Test]
        public void NpcAnimationState_Existing_Values_Are_Pinned()
        {
            // 只加不删的机器化防线：0..6 是已发布的 wire 契约（玩家与 NPC 共用这一栏）
            Assert.AreEqual(0, (byte)Cool.Protocol.NpcAnimationState.Idle);
            Assert.AreEqual(1, (byte)Cool.Protocol.NpcAnimationState.Walk);
            Assert.AreEqual(2, (byte)Cool.Protocol.NpcAnimationState.Run);
            Assert.AreEqual(3, (byte)Cool.Protocol.NpcAnimationState.Attack);
            Assert.AreEqual(4, (byte)Cool.Protocol.NpcAnimationState.Skill);
            Assert.AreEqual(5, (byte)Cool.Protocol.NpcAnimationState.Hit);
            Assert.AreEqual(6, (byte)Cool.Protocol.NpcAnimationState.Death);
        }

        [Test]
        public void EntitySnapshot_Existing_Tags_Are_Pinned()
        {
            // 只加不删的机器化防线：既有 ProtoMember 号一旦被挪（哪怕挪成另一个唯一号），
            // protobuf-net 不报错、老客户端静默解到别的字段上
            Assert.AreEqual(9, GetProtoMemberTag(typeof(Cool.Protocol.EntitySnapshot).GetProperty("Tick")!));
            Assert.AreEqual(10, GetProtoMemberTag(typeof(Cool.Protocol.EntitySnapshot).GetProperty("Kind")!));
            Assert.AreEqual(11, GetProtoMemberTag(typeof(Cool.Protocol.EntitySnapshot).GetProperty("TemplateId")!));
        }

        [Test]
        public void EntityFieldMask_Bits_Are_Unchanged()
        {
            // 增量快照靠这些位判断"这一栏可不可信"（EntityFieldMask 的文档："字段值仅在对应位被置位时有效"）。
            // 任何一个既有位的值变了，客户端会把"没带这个字段"读成"带了"（或反过来）——静默的。
            Assert.AreEqual(1 << 0, (ushort)Cool.Protocol.EntityFieldMask.Position);
            Assert.AreEqual(1 << 1, (ushort)Cool.Protocol.EntityFieldMask.Rotation);
            Assert.AreEqual(1 << 2, (ushort)Cool.Protocol.EntityFieldMask.Speed);
            Assert.AreEqual(1 << 3, (ushort)Cool.Protocol.EntityFieldMask.ActionState);
            Assert.AreEqual(1 << 4, (ushort)Cool.Protocol.EntityFieldMask.Kind);
            Assert.AreEqual(1 << 5, (ushort)Cool.Protocol.EntityFieldMask.TemplateId);
        }

        [Test]
        public void RespawnNotice_Roundtrips()
        {
            var sent = new Cool.Protocol.RespawnNotice { Hp = 100f, Tick = 4242u };
            var bytes = Cool.Protocol.MessageCodec.EncodeToArray(
                Cool.Protocol.MessageId.RespawnNotice, sent);
            Assert.IsTrue(Cool.Protocol.MessageCodec.TryDecode(bytes, out var decoded));
            Assert.AreEqual(Cool.Protocol.MessageId.RespawnNotice, decoded.messageId);
            var got = (Cool.Protocol.RespawnNotice)decoded.message;
            Assert.AreEqual(100f, got.Hp);
            Assert.AreEqual(4242u, got.Tick);
        }

        [Test]
        public void NpcAnimationState_Carries_Down_And_GetUp()
        {
            Assert.AreEqual(7, (byte)Cool.Protocol.NpcAnimationState.Down);
            Assert.AreEqual(8, (byte)Cool.Protocol.NpcAnimationState.GetUp);
        }

        [Test]
        public void PositionSyncRequest_Defending_Tag_Is_Pinned()
        {
            // 只加不删的机器化防线（1..7 已发布）：Defending 必须在 tag 8——挪号不报错，
            // 老客户端的上报会静默落到别的字段上（服务端永远读到 false）
            var prop = typeof(Cool.Protocol.PositionSyncRequest).GetProperty("IsDefending");
            Assert.IsNotNull(prop, "PositionSyncRequest 必须有 IsDefending");
            Assert.AreEqual(typeof(bool), prop!.PropertyType);
            Assert.AreEqual(8, GetProtoMemberTag(prop),
                "IsDefending 必须挂在 tag 8（既有 1..7 是已发布的 wire 契约）");
        }

        [Test]
        public void HitEntry_Blocked_Tag_Is_Pinned()
        {
            // 只加不删的机器化防线：格挡标志追加 tag 7（1..6 已发布）
            var prop = typeof(Cool.Protocol.HitEntry).GetProperty("IsBlocked");
            Assert.IsNotNull(prop, "HitEntry 必须有 IsBlocked");
            Assert.AreEqual(typeof(bool), prop!.PropertyType);
            Assert.AreEqual(7, GetProtoMemberTag(prop),
                "IsBlocked 必须挂在 tag 7（既有 1..6 是已发布的 wire 契约）");
        }

        [Test]
        public void DamageResult_ProjectileStop_Tags_Are_Pinned()
        {
            var stopped = typeof(Cool.Protocol.DamageResult).GetProperty("ProjectileStopped");
            Assert.IsNotNull(stopped, "DamageResult 必须有 ProjectileStopped");
            Assert.AreEqual(typeof(bool), stopped!.PropertyType);
            Assert.AreEqual(5, GetProtoMemberTag(stopped),
                "ProjectileStopped 必须挂在 tag 5（1..4 已发布）");

            var stopX = typeof(Cool.Protocol.DamageResult).GetProperty("StopX");
            Assert.IsNotNull(stopX);
            Assert.AreEqual(typeof(float), stopX!.PropertyType);
            Assert.AreEqual(6, GetProtoMemberTag(stopX), "StopX 必须挂在 tag 6");

            var stopZ = typeof(Cool.Protocol.DamageResult).GetProperty("StopZ");
            Assert.IsNotNull(stopZ);
            Assert.AreEqual(typeof(float), stopZ!.PropertyType);
            Assert.AreEqual(7, GetProtoMemberTag(stopZ), "StopZ 必须挂在 tag 7");
        }

        /// <summary>
        /// 按名字认 [ProtoMember]：本测试程序集引用不到 protobuf-net 插件（Unity 不给它自动引用
        /// 预编译 DLL，GameSys.EditorTests.csproj 里没有该 Reference），所以不能写 ProtoBuf.ProtoMemberAttribute。
        /// </summary>
        private static bool HasProtoMember(MemberInfo member)
        {
            foreach (var attribute in member.GetCustomAttributes(false))
            {
                if (attribute.GetType().FullName == "ProtoBuf.ProtoMemberAttribute") return true;
            }
            return false;
        }

        /// <summary>
        /// 取 [ProtoMember] 的 Tag（线上字段号）。没有 attribute 时返回 -1——调用方断言具体号，-1 即失败。
        /// attribute 同样按名字认（本程序集引用不到 protobuf-net）。
        /// </summary>
        private static int GetProtoMemberTag(PropertyInfo property)
        {
            foreach (var attribute in property.GetCustomAttributes(false))
            {
                if (attribute.GetType().FullName != "ProtoBuf.ProtoMemberAttribute") continue;

                var tag = attribute.GetType().GetProperty("Tag");
                if (tag == null)
                    throw new InvalidOperationException(
                        "ProtoMemberAttribute 上没有 Tag 属性——本门禁的反射目标失效，取号方式需更新");

                return (int)tag.GetValue(attribute);
            }
            return -1;
        }
    }
}
