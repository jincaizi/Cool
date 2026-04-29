# 3C 系统调试记录

## 问题1：角色漂浮（胶囊体与模型错位）

**现象**：角色站在地面上，但视觉上看起来脚悬空，略微漂浮。

### 排查过程

1. 通过运行时诊断获取 Animator 数据：
   - `rootPosition.y = 0.281`（角色根节点世界坐标）
   - `leftFeetBottomHeight = 0.03`（左脚底相对根节点高度）
   - 计算得：脚底世界 Y ≈ `0.281 - 0.03 = 0.251`

2. 获取 CharacterController 胶囊体数据：
   - `center = (0, 0, 0)`，`height = 2`，`radius = 0.5`
   - 胶囊体底部世界 Y = `transform.position.y + center.y - height/2` = `0.281 + 0 - 1 = -0.719`

3. **根因确认**：
   - 胶囊体底部 world Y = **-0.719**
   - 模型脚底 world Y = **0.251**
   - **相差 ~1.0 单位**，胶囊体和模型完全错位

4. 查看 prefab hierarchy：`MaleCharacterPBR` → `root` → `pelvis` → ...
   - 模型 root 节点的 localPosition 与 CharacterController center 没有对齐

### 解决方案

**方案**：调整 `CharacterController.center.y`，将胶囊体在角色 local space 中上移 0.97 单位，使胶囊底部和脚底对齐。

计算：`feetBottomWorld - transform.position.y + height/2 = 0.251 - 0.281 + 1.0 = 0.97`

修改内容：
- `CharacterController.center.y`: `0` → `0.97`
- `root.localPosition`: 保持 `(0, 0, 0)` 不变

### 验证方法

运行后在 Console 执行诊断：
```csharp
var cc = FindObjectOfType<UnityEngine.CharacterController>();
var root = GameObject.Find("MaleCharacterPBR")?.transform.Find("root");
float capsuleBottom = cc.transform.position.y + cc.center.y - cc.height/2;
float feetWorld = root.position.y - 0.03f;
Debug.Log($"capsuleBottom={capsuleBottom:F3}, feetWorld={feetWorld:F3}, diff={Mathf.Abs(feetWorld - capsuleBottom):F3}");
```

正常值：`diff` 应接近 `0`（允许 ±0.05 误差）。

### 影响范围

同类模型（如女性角色 `FemaleCharacterPBR`）如果有相同结构的 rig hierarchy，可能存在同样的错位问题。
检查方法同上，如发现差值接近 1.0 单位，同样的 `center.y` 调整即可。

---

## 问题2：跳跃后动画卡在 JumpEnd

**现象**：按空格跳跃后，角色落地时动画过渡到 JumpEnd，但一直停留在 JumpEnd 状态，无法回到 Idle。

### 排查过程（进行中）

待补充——需要运行时日志确认卡住的具体环节。

### 可能的原因

1. **JumpEnd → Idle 转换条件不满足**：Animator Controller 中该转换的 `hasExitTime=False`，条件为 `JumpPhase > 0 AND IsJumping == false`。如果这些参数没有正确设置，转换不会触发。

2. **JumpEnd 动画 exitTime 配置过长**： JumpEnd 动画的 `exitTime = 0.9`，意味着需要播放到 90% 才开始计算退出条件。如果动画本身较长，延迟会很明显。

3. **代码逻辑问题**：`CharacterController.Update()` 每帧可能将 `State` 覆盖为 `Idle`，导致 JumpEnd 状态被覆盖。

### 解决方案（待验证）

已实施：
- 添加 `_stateLocked` 标志，在 JumpEnd 播放期间阻止 `Update()` 覆盖状态
- 添加 `FinishJump()` 方法，在 JumpEnd 动画退出时调用，重置所有跳跃状态
- 动画落地时通过 `OnLanded` 事件触发 `OnLanding()`，正确设置 `JumpPhase=End` 和 `State=JumpEnd`
- 在 `Sys3CEntry.OnAnimatorStateExit()` 中检测 JumpEnd 退出并调用 `FinishJump()`

### 验证方法

在 Unity Console 中观察以下日志：
- `[Landing]` — 着地检测触发
- `[OnLanding event]` — OnLanded 事件被调用
- `[OnLanding]` — 动画参数被设置
- `[Driver.Update#...] normalizedTime=...` — JumpEnd 动画播放进度
- `[OnStateExit] JumpEnd exit detected!` — JumpEnd 动画退出回调
- `[FinishJump]` — FinishJump 被调用

---

## 通用调试技巧

### 获取 Animator Controller 转换信息（运行时）
```csharp
var animator = FindObjectOfType<Animator>();
var ctrl = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
var layer = ctrl.layers[0];
var sm = layer.stateMachine;

// 递归查找状态
string FindState(UnityEditor.Animations.AnimatorStateMachine sm, string name) {
    foreach (var s in sm.states) {
        if (s.state.name == name) {
            var t = s.state.transitions[0]; // 示例
            return $"exitTime={t.exitTime}, hasExitTime={t.hasExitTime}";
        }
    }
    foreach (var child in sm.stateMachines)
        return FindState(child.stateMachine, name);
    return null;
}
```

### 获取 CharacterController 胶囊体位置
```csharp
var cc = FindObjectOfType<UnityEngine.CharacterController>();
float bottom = cc.transform.position.y + cc.center.y - cc.height/2;
float top = cc.transform.position.y + cc.center.y + cc.height/2;
Debug.Log($"center={cc.center}, bottom={bottom}, top={top}");
```

### 检查模型脚底位置
```csharp
var animator = FindObjectOfType<Animator>();
float lFoot = animator.leftFeetBottomHeight;
float rFoot = animator.rightFeetBottomHeight;
Debug.Log($"leftFoot={lFoot}, rightFoot={rFoot}");
```
