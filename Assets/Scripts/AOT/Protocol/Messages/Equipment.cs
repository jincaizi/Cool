using System.Collections.Generic;
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>换装请求（客户端 → 服务器；属性剑气 v1）。只选择会话内已发放的实例。</summary>
    [ProtoContract]
    public class EquipWeaponRequest : IMessage
    {
        [ProtoMember(1)] public uint ClientEquipSeq { get; set; }
        [ProtoMember(2)] public long WeaponInstanceId { get; set; }
        [ProtoMember(3)] public uint KnownEquipmentRevision { get; set; }
    }

    /// <summary>换装确认（服务器 → 客户端）。拒绝时也携带当前权威装备摘要，供客户端恢复。</summary>
    [ProtoContract]
    public class EquipWeaponConfirm : IMessage
    {
        [ProtoMember(1)] public uint ClientEquipSeq { get; set; }
        [ProtoMember(2)] public bool Accepted { get; set; }
        [ProtoMember(3)] public string RejectReason { get; set; } = "";
        [ProtoMember(4)] public uint EquipmentRevision { get; set; }
        [ProtoMember(5)] public long WeaponInstanceId { get; set; }
        [ProtoMember(6)] public int TemplateId { get; set; }
        [ProtoMember(7)] public int ElementId { get; set; }
    }

    /// <summary>会话内武器实例摘要（登录基线用）。</summary>
    [ProtoContract]
    public class WeaponInstanceSummary
    {
        [ProtoMember(1)] public long InstanceId { get; set; }
        [ProtoMember(2)] public int TemplateId { get; set; }
        [ProtoMember(3)] public int ElementId { get; set; }
    }

    /// <summary>登录装备基线：生命代次、装备版本、当前装备与完整武器库存。</summary>
    [ProtoContract]
    public class WeaponInventoryBaseline
    {
        [ProtoMember(1)] public uint LifeId { get; set; } = 1;
        [ProtoMember(2)] public uint EquipmentRevision { get; set; }
        [ProtoMember(3)] public long WeaponInstanceId { get; set; }
        [ProtoMember(4)] public int TemplateId { get; set; }
        [ProtoMember(5)] public int ElementId { get; set; }
        /// <summary>必须声明为 IList——IL2CPP 下 List&lt;T&gt; 触发 protobuf-net tuple 探测缺口。</summary>
        [ProtoMember(6)] public IList<WeaponInstanceSummary> Weapons { get; set; } = new List<WeaponInstanceSummary>();
    }
}
