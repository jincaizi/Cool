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
    }
}
