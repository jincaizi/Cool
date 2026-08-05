# 选人界面修复设计（EventSystem + 布局）

- 日期: 2026-08-06
- 状态: 已批准
- 触发: 用户反馈两个问题 —— ① 角色选择界面没有点击响应；② 男/女按钮太大，布局不合理

## 1. 根因与结论

**问题①根因**：CharacterSelect 场景**没有 EventSystem**（0 个），UGUI 无输入处理 → 点击无响应。此前的 E2E 验证用 `onClick.Invoke()` 绕过了输入系统，未覆盖此缺陷。
**修复**：选人场景添加 EventSystem 对象，结构照搬 DemoDay（GameObject + Transform + EventSystem 组件 guid `76c392e4` + StandaloneInputModule guid `4f231c4f`）。已确认 `activeInputHandler: 2`（新旧输入均启用）→ StandaloneInputModule 可用。

**问题②**：卡片 360×520 过大（相对设计分辨率 1920×1080），改为底部居中紧凑布局。

## 2. 布局调整（CharacterSelectPanel.prefab，坐标 = 设计分辨率单位）

| 元素 | 当前 | 调整后 |
|---|---|---|
| MaleCard / FemaleCard | 360×520 @ (±460, 40) | 200×280 @ (±130, -80) |
| 高亮框 ×2 | 392×552 | 220×300（套卡片外 10px） |
| ConfirmButton | 280×90 @ (0, -430) | 220×70 @ (0, -330) |
| 「男」「女」字号 | 48 | 40 |
| 「确认」字号 | 48 | 36 |

坐标框架：`ScreenAdapter` 将 CanvasScaler 覆盖为 `ScaleWithScreenSize` + 设计分辨率 1920×1080（MatchWidthOrHeight 0.5），所有坐标/尺寸按设计分辨率单位。核对：卡片下边缘 -220、确认按钮下边缘 -365，均在 ±540 画布范围内，任意宽高比可见。

## 3. 范围与验证

- 范围：选人场景 +1 个 EventSystem 对象；面板 prefab 布局调整；**零代码逻辑改动**（面板/控制器/生成器不动）
- 验证：EditMode 66/66 回归；PlayMode **真实射线点击**（`EventSystem.current.RaycastAll` + `ExecuteEvents` 指针按下/抬起模拟，不 bypass UI）验证卡片/确认按钮点击链路 + 高亮/预览切换；截图确认新布局
