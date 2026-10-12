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
        /// 值必须与**客户端渲染出来的东西**同宽，否则两边漂移（客户端挡在 1.74m、服务端按 1.60m
        /// 避让 ⇒ 贴着怪会被轻微来回推）：
        ///   Slime / Wolf = 0.5 —— 客户端目录未接线，退化为 `CreatePrimitive(Sphere)` 单位球（半径 0.5）；
        ///   DogKnight    = 0.88 —— `DogKnightMonster.prefab` 合并 SMR 世界 bounds 的 XZ 跨度取大者 / 2（1.754/2）；
        ///   SpiderBoss   = 1.74 —— `SpiderMonster.prefab` 同法实测 3.484 / 2（X 跨度 3.324 略窄，**取大的 Z**——
        ///                  客户端身体是 X/Z 方盒包络，只按 X 算每侧会小 0.08m；根上 2× 缩放已折进世界尺寸）。
        /// 客户端侧由 EditMode 用例 `MonsterBodyRadiusTests` 反向守着（读本表 ↔ 量模型，容差 0.05）。
        /// 服务端侧由 `AvoidanceWiringTests.Template_Body_Radii_Are_Read_From_The_Shipped_Json` 守着
        /// （防 `IncludeFields` 被关掉 / json 键改名 / 新模板漏填时的静默落 0 → 半径退化）。
        /// </summary>
        public float bodyRadius;
        public float detectionRadius;
        public float visionAngle;
        public float attackRange;
        public float patrolRadius;
        public List<string> skills;
    }
}