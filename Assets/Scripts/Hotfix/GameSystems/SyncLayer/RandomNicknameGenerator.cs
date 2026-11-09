namespace Hotfix.GameSystems.SyncLayer
{
    public static class RandomNicknameGenerator
    {
        private static readonly string[] Prefixes =
        {
            "疾风", "烈焰", "寒霜", "幽影", "星尘", "月华",
            "赤焰", "青岚", "苍穹", "破晓", "孤狼", "银月",
            "铁壁", "流云", "惊雷", "夜雨", "霜刃", "烈阳",
            "玄冰", "飞雪", "碧海", "紫电", "暗星", "白露",
        };

        private static readonly string[] Suffixes =
        {
            "之狼", "之鹰", "之刃", "之影", "之翼", "之心",
            "之魂", "之盾", "之弓", "之剑", "之虎", "之龙",
            "之狐", "之熊", "之雀", "之豹", "之蛇", "之鹿",
            "之狮", "之凤", "骑士", "游侠", "法师", "守卫",
        };

        public static string Generate(System.Random rng)
        {
            rng ??= new System.Random();
            return Prefixes[rng.Next(Prefixes.Length)]
                 + Suffixes[rng.Next(Suffixes.Length)];
        }

        public static string Generate() => Generate(new System.Random());
    }
}
