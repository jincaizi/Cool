using System.Collections.Generic;

namespace KcpServer.Config
{
    public class MonsterConfig
    {
        public List<MonsterData> monsters { get; set; } = new();
    }

    public class MonsterData
    {
        public int templateId;
        public string name;
        public float hp;
        public float moveSpeed;

        /// <summary>
        /// 身体半径（米，XZ 半宽）——ORCA 避让用（2026-10-13 接线）。服务端原本没有这个概念，
        /// 值必须与**客户端渲染出来的东西**同宽，否则两边漂移（客户端挡在 1.66m、服务端按 1.80m
        /// 避让 ⇒ 贴着怪会被轻微来回推）：
        ///   Slime / Wolf = 0.5 —— 客户端目录未接线，退化为 `CreatePrimitive(Sphere)` 单位球（半径 0.5）；
        ///   DogKnight    = 0.88 —— `DogKnightMonster.prefab` 合并 SMR 世界 bounds X 跨度 1.754 / 2；
        ///   SpiderBoss   = 1.66 —— `SpiderMonster.prefab` 同法实测 3.324 / 2（根上 2× 缩放已折进世界尺寸）。
        /// 客户端侧由 EditMode 用例 `MonsterBodyRadiusTests` 反向守着（读本表 ↔ 量模型）。
        /// </summary>
        public float bodyRadius;
        public float detectionRadius;
        public float visionAngle;
        public float attackRange;
        public float patrolRadius;
        public List<string> skills;
    }
}