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

约 1.8 倍余量。**调冷却或换片段时必须重验**：`NpcAnimatorAssetTests.Two_Swings_In_A_Row_Both_Play` 会替你发现越界（它推过片段 + exit-time 后断言攻击态已回 Idle，再打第二刀，两只怪各跑一遍）。

## 为什么 `Hit` 没有出边也不怕片段循环（`GetHit.anim` 是 `m_LoopTime: 1`）

`Hit` 状态刻意不放出边（见上）。`GetHit.anim` 自身是循环片段，看上去"没有出边 = 循环不会自己停"，但**这条风险不可达**：

| 项 | 值 |
|---|---|
| `GetHit.anim` 长度（末关键帧，字节实测） | **0.833s** |
| 服务端受击硬直窗口 `MonsterModule.HitDisplayTicks` | 5 tick × `TickDeltaSeconds` 0.066s = **0.33s** |

服务端在 0.33s 后就把 `MonsterState` 带离 4，时间轴在片段走到循环点（0.833s）之前就已经离开 `Hit` 态。余量 ≈ 2.5 倍。
（即：**别为了"怕循环"给 Hit 加出边** —— 那才会让 `MonsterState==4` 持续为真时逐帧重播受击。）

## 改完怎么验（本资产的写回不可靠）

`animator-modify` / `script-execute` 写 `.controller` 后，Unity 会留着**陈旧实例**：磁盘字节可能没变、或变了但 Unity 视图仍是旧的（本次任务踩到过，含"变异回滚只落到内存、没落到磁盘"）。
所以**只跑测试不足以证明**（测试读的是 Unity 视图）。改完必须三者一致：

1. 磁盘字节：`grep -c 'm_Name: Attack$' NpcAnimator_DogKnight.controller` = 1 之类的字节级检查
2. Unity 视图：`LoadAssetAtPath<AnimatorController>` 打印参数名+类型
3. 测试绿灯

`ImportAsset(ForceUpdate)` 刷不掉陈旧实例；可靠解法是**把好字节在另一个路径导入（新 GUID）→ `AssetDatabase.MoveAsset` 挪到目标路径**。
