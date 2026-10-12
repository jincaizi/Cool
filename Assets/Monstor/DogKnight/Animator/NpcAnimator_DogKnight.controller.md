# NpcAnimator_DogKnight.controller —— 资产注记

（AnimatorController 没有注释字段，注记以同名伴生文件放在资产旁。）

驱动侧：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcEntityAnimator.cs`（服务端快照 + 施法广播）。

**参数名是硬契约**：`MonsterState`(Int) / `AttackVariant`(Int) / `Attack`(Trigger)。
名字与驱动里 `Animator.StringToHash` 的常量不一致时，`SetInteger` **不报错**、只是永远写不进去，动画静默不动。

状态（单层 `Base Layer`，默认 `Idle`）：Idle / Walk / Run / Attack01 / Attack02 / Hit / Death。

## 不变量：攻击片段时长必须**短于**该怪的冷却

所有 AnyState 转移都设了 `canTransitionToSelf = false`（条件持续为真时不重入 —— 否则受击/移动动画会逐帧重播自己）。
代价是：**下一刀到达时若攻击态还没退出，新 Trigger 被静默吞掉** —— 不报错、不播、不计数，只是丢刀。

实测余量（2026-10-12）：

| 项 | 值 |
|---|---|
| Attack01 / Attack02 片段时长 | **0.833s** |
| 该怪攻击冷却 | **1.5s** |
| 攻击态退出的最迟时刻 | exit-time 0.95 × 0.833s + 0.1s 过渡 ≈ 0.93s |

约 1.8 倍余量。**调冷却或换片段时必须重验**：`NpcAnimatorAssetTests.Two_Swings_In_A_Row_Both_Play` 会替你发现越界（它推过片段 + exit-time 后断言攻击态已回 Idle，再打第二刀）。
