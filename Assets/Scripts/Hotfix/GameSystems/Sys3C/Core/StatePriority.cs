namespace Hotfix.GameSystems.Sys3C.Core
{
    /// <summary>
    /// 层优先级定义
    /// </summary>
    public static class StatePriority
    {
        public const int HitLayer = 3;    // 最高
        public const int AttackLayer = 2;
        public const int BaseLayer = 1;  // 最低

        /// <summary>
        /// 获取优先级数值
        /// </summary>
        public static int GetPriority(LayerType layer)
        {
            return layer switch
            {
                LayerType.Hit => HitLayer,
                LayerType.Attack => AttackLayer,
                LayerType.Base => BaseLayer,
                _ => 0
            };
        }

        /// <summary>
        /// 比较优先级
        /// </summary>
        public static bool IsHigherPriority(LayerType a, LayerType b)
        {
            return GetPriority(a) > GetPriority(b);
        }

        /// <summary>
        /// 判断 a 是否可以打断 b
        /// </summary>
        public static bool CanInterrupt(LayerType interrupter, LayerType target)
        {
            return IsHigherPriority(interrupter, target);
        }

        /// <summary>
        /// 获取打断目标层列表
        /// </summary>
        public static LayerType[] GetInterruptibleLayers(LayerType interrupter)
        {
            var result = new System.Collections.Generic.List<LayerType>();

            foreach (LayerType layer in System.Enum.GetValues(typeof(LayerType)))
            {
                if (IsHigherPriority(interrupter, layer))
                {
                    result.Add(layer);
                }
            }

            return result.ToArray();
        }
    }
}