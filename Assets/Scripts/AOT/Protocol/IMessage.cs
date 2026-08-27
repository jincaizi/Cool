namespace Cool.Protocol
{
    /// <summary>
    /// 消息基接口 —— 全部线上消息必须实现；禁止向本接口添加成员，
    /// 保持标记接口形态（与两端现有编解码机制耦合）
    /// </summary>
    public interface IMessage
    {
    }
}
