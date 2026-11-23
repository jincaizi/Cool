# 剑气回荡与蓄力炎爆美术贴图需求

交付给图片生成 agent。共 9 张，首批 4 张可以直接改善战斗画面。每个代码块是一份独立 prompt，按对应文件名交付。尺寸是本次面向移动端与 WebGL 的制作建议。

## 当前资源与接线

| 技能 | 当前表现 | 需要补上的接线 |
| --- | --- | --- |
| 剑气回荡 · 20006 | 四道剑气按既有轨迹穿梭。`SwordQiEchoWhite.prefab` 有白色弧刃和拖尾，两份材质都未挂贴图。单机、本地联机和远端入口运行时缺少组件就新建 `SwordQiEchoPresenter`，仓库中的占位预制体也没有发现序列化引用。未配置 `_swordPrefab` 时会生成白色拖尾。 | 给本地和远端统一注入同一个回荡视觉预制体。剑头与尾迹共用同一组图，移动方向、弯曲、错时和回池清理由现有轨迹负责。 |
| 蓄力炎爆 · 20004 | `ChargeFire_SkillR.asset` 已引用 `Explosion_Charge.prefab`。释放预制体的粒子渲染器材质槽为空，贴图序列帧模块关闭，技能的 `CastVFX` 也为空。当前本地与远端都能实例化释放预制体。 | 先接火团和贴地冲击环。增强蓄力表现时，把聚火纹接到持有阶段，按进度调强弱，释放、打断和解绑时收掉。释放层级可以从已有蓄力进度驱动，本地进度和远端 `CastBroadcast.ChargeProgress` 都已有入口数据。 |

来源见 [回荡技能设计](2026-11-17-sword-qi-echo-design.md)、[回荡表现入口](../../../Assets/Scripts/Hotfix/GameSystems/VFX/SwordQiEchoPresenter.cs)、[回荡占位预制体](../../../Assets/PreRes/SkillsCfg/SwordQiEchoWhite.prefab)、[单机宿主](../../../Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SwordQiEchoLocalRunner.cs)、[联机宿主](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs)、[炎爆技能资产](../../../Assets/PreRes/SkillsCfg/ChargeFire_SkillR.asset)、[炎爆释放预制体](../../../Assets/PreRes/Skills/Explosion_Charge.prefab)、[本地释放](../../../Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs)和[远端释放](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs)。

## 交付清单

| 批次 | 文件 | 尺寸 | 用途 |
| --- | --- | --- | --- |
| 首批 | `SwordQiEchoHead.png` | 512 × 256 | 飞剑头的发光贴图 |
| 首批 | `SwordQiEchoTrail.png` | 512 × 128 | 随轨迹弯曲的拖尾贴图，横向展开 |
| 首批 | `ChargeFireBurstFlipbook.png` | 1024 × 1024，4 × 4，每格 256 × 256 | 炎爆释放火团序列帧 |
| 首批 | `ChargeFireGroundRing.png` | 512 × 512 | 炎爆贴地扩张的冲击环 |
| 第二批 | `ChargeFireGatherRibbon.png` | 512 × 256 | 蓄力期间吸向武器的聚火火丝 |
| 第二批 | `ChargeFireEmbers.png` | 256 × 256，2 × 2，每格 128 × 128 | 炎爆余烬粒子图集 |
| 第二批 | `SwordQiEchoContact.png` | 256 × 256 | 回荡实际命中时的短促裂光 |
| 可选 HUD | `SkillIconSwordQiEcho.png` | 256 × 256 | 回荡技能按钮的专属图标 |
| 可选 HUD | `SkillIconChargeFire.png` | 256 × 256 | 炎爆技能按钮的专属图标 |

## 通用交付规格

- 输出带真实透明通道的 RGBA PNG，使用直通 Alpha。空白像素 Alpha 为 0。外发光也要落在透明通道中，避免黑底、白底、棋盘格或整张矩形雾幕。允许先高分辨率生成，再裁切并缩放到表中尺寸。
- 特效按现有低多边形场景的战斗尺度制作。清楚的外轮廓优先于细碎纹理。剑气用银白刃芯和淡冰蓝边；炎爆用浅黄高光、暖橙火面和少量深红残焰。颜色都可以在 Unity 里再调。
- 每张图单独交付。图集内每格留透明边，发光不可跨格。生成器做不稳连续帧时，先按编号交付同规格单帧，由打包环节合成最终图集。
- 验收时在浅草地和暗处各看一遍实际混合效果。序列帧逐格预览，检查裁边、色跳、中心漂移和透明边；HUD 在 32–48 px 预览，确认形状仍易辨认。

## 剑气回荡

### 1. 飞剑头 · SwordQiEchoHead.png

一枚轻薄的能量剑锋。尖端朝右，尾根朝左，两翼略不对称，从银白高光过渡到淡冰蓝。只保留紧凑的剑头与短尾根，和拖尾贴图相接时不出现第二个亮点。可作为飞行根节点下的短窄双面网格或面片测试，尖端对齐飞行前方。

```text
Create one isolated real-time game VFX texture for the head of a fast flying sword-energy projectile. Export a 512 x 256 straight-alpha RGBA PNG on a genuinely transparent canvas. Side-on orthographic view. The sharp tip points directly right, the short dissolving root points left, and the energy follows a horizontal centerline through the middle of the image. Draw a slender, gently curved, slightly asymmetric silver-white energy blade with a soft pearl-white core, very light icy-blue rim (#ADDEFF), and a short semi-transparent root that can join a separate trail. Keep the tip clean and the outer glow contained. It must remain readable when small and when four copies cross. Exclude physical sword hardware, a hilt, a hand, large sparks, a long streak, ground, text, frames, and any visible background.
```

### 2. 拖尾 · SwordQiEchoTrail.png

横向平铺的单条细带，是给 `TrailRenderer` 的 UV 贴图。左端定义为新生端，右端定义为消散端。整条亮芯在水平中线附近，两侧柔和，只有少量纤丝在右端分散。接入时用 Stretch 模式，根据实际头尾方向对照 UV，必要时水平翻转材质采样；世界弧线和粗细曲线由 Unity 提供。

```text
Generate a 512 x 128 transparent straight-alpha RGBA PNG for a stretched Unity TrailRenderer. A single horizontal silver-white sword-energy ribbon occupies the middle of a wide, fully transparent texture, with restrained icy-blue edges (#ADDEFF). The left end is the fresh end, with a clean bright core compatible with the root of a silver energy blade; from left to right, the core thins and the ribbon gradually becomes a few faint wisps, reaching zero alpha on the right. Keep a consistent horizontal axis so it can bend cleanly with a world-space trail. Use subtle translucent fibers, soft top and bottom edges, and no visible rectangular boundary. Exclude a sword tip, curved flight path, hard lens flare, isolated star, texture tiling seams, opaque fill, labels, and backgrounds.
```

### 3. 接触裂光 · SwordQiEchoContact.png

一次很短的银白斩击裂光，中心清楚，外缘少量分叉。仅为既有命中反馈增加专属图层。联机按权威命中结果触发，单机按本地有效命中触发，剑气的回旋和擦过继续只画轨迹。

```text
Create a single 256 x 256 straight-alpha RGBA VFX sprite for the instant contact of a magical flying sword. Centered pivot, an elongated silver-white slash glint on a shallow diagonal from lower left to upper right, with two or three tiny icy-blue splinters close to the center. A sharp core, one soft glow falloff, clear transparent gaps, and generous transparent padding. The luminous shape should read as one brief cut at small scale. Exclude a circular explosion, lightning cloud, weapon, blood, character, text, UI border, shadow, background, and detached distant particles.
```

## 蓄力炎爆

### 4. 释放火团 · ChargeFireBurstFlipbook.png

施法点附近向外打开并向上翻卷的火团。用同一视角生成 16 帧，按从左到右、从上到下排入 4 × 4。每格的地面起点统一放在横向 50%、从上往下 82% 的位置，边缘至少留约 8 px 透明。抬升和扩散应发生在格内，避免逐帧重新居中。

| 帧 | 画面变化 |
| --- | --- |
| 01–03 | 紧凑暖白火核迅速点亮，轮廓保持集中。 |
| 04–07 | 下宽上窄的火团外翻，峰值亮度短促，出现几束大小不同的火舌。 |
| 08–12 | 火面外扩变薄，中心透开，橙红轮廓破成几缕。 |
| 13–16 | 残焰继续抬升并渐隐，最后一帧接近全透明。 |

Unity 侧给 `Explosion_Charge.prefab` 配透明粒子材质、开启 Texture Sheet Animation，并从画面上方检验首末帧顺序。建议把可见火团压在约 0.55–0.75 秒内，让起爆与余火有清晰节奏，并落在现有一次性特效回收时间内。

```text
Create a 16-frame stylized fire-burst flipbook for a mobile low-poly fantasy combat game. Deliver a 1024 x 1024 straight-alpha RGBA PNG, exactly four columns by four rows, 256 x 256 per frame. Read frames left to right, top to bottom. Use one fixed orthographic view and scale. In every cell the ground origin is 50 percent from the left and 82 percent from the top. Keep all glow at least 8 pixels from cell borders and keep every background transparent. Frames 1–3: a compact pale-gold ignition core. Frames 4–7: the core opens into a broad, low orange fire bloom with a few asymmetric rounded flame tongues rising and curling outward. Frames 8–12: the bloom thins and opens in the middle as flame tongues separate into orange-red wisps. Frames 13–16: the red-orange remnants lift and fade to almost zero alpha. Use warm ivory (#FFF1BF), orange (#FF9A37), and a restrained deep ember rim (#B94222). Readable painterly shapes and soft alpha falloff. Keep the ground ring out of this sheet. Exclude a mushroom cloud, heavy smoke, photoreal debris, cast shadows, ground texture, characters, labels, cell borders, backgrounds, and camera shifts.
```

### 5. 贴地冲击环 · ChargeFireGroundRing.png

严格俯视的同心火环，中间保留大面积透明。前沿有细暖黄高光，后缘几个大小错开的橙红断口逐渐淡出。用一张纹理驱动一个以释放点为中心、沿地面扩张的圆形表现层，外圈按当前技能判定或权威广播的范围缩放。

```text
Create a 512 x 512 straight-alpha RGBA game VFX texture for a ground-level fire shockwave. Render a strictly top-down orthographic, circular annulus with its exact center at the center of the canvas and a large fully transparent interior. The outward edge is a thin warm gold and pale-orange flash (#FFE1A1 and #FF9A37); the inner edge breaks into sparse, irregular orange-red flame wisps, with several different gap widths. Keep the footprint broadly round and reserve transparent padding around the outer glow. It should stay clean when enlarged across grass. Exclude an ellipse, perspective, symbols, rune writing, a filled center, ground cracks, rocks, shadows, words, UI markings, and any baked backdrop.
```

### 6. 聚火火丝 · ChargeFireGatherRibbon.png

用于蓄力期间的细带。沿横向从左侧散开的细纤丝聚向右侧较亮的一点，头部不做明显爆炸。Unity 用同一图做几条吸向武器的短火丝，运动、聚拢速度与满蓄亮度由粒子和材质参数实现。持续蓄力图层需要绑定施法者的实际蓄力状态，远端释放广播无法提供完整的远端蓄力过程。

```text
Generate a 512 x 256 straight-alpha RGBA particle ribbon for fire energy gathering into a spell. Show one narrow, gently S-curved stream, with two faint orange strands entering from the left and converging to one small warm-yellow highlight on the right. Leave the right tip soft and attachable so it can terminate beside a weapon. The left start feathers into transparent pixels; the orange-red outer glow is restrained and never fills the rectangle. Use broad readable curves with light taper and plenty of clear margins. Exclude an explosion, fireball, literal torch, bright starburst, full ring, heavy smoke, weapon, character, labels, and any visible background.
```

### 7. 余烬粒子 · ChargeFireEmbers.png

四枚形态不同的小火星，图集按左上短点、右上细梭、左下略弯的碎片、右下双瓣火星排放。每格内容居中，透明边至少约 12 px。粒子材质可统一调暖橙，少量随机抽取并向外散去。

```text
Create a 256 x 256 straight-alpha RGBA texture atlas, exactly 2 x 2 with 128 x 128 tiles, for sparse sparks after a stylized fire burst. Top-left: a tiny rounded ember. Top-right: a slender teardrop spark. Bottom-left: a small gently hooked ember. Bottom-right: a compact twin-lobed glowing fragment. Place one centered distinct silhouette per tile, all with ivory-yellow centers, orange-red edges and a restrained soft halo. Leave at least 12 pixels of clear transparent margin on each tile edge. Reserve true zero alpha in all empty areas. Exclude cell grid lines, labels, background, clouds, large flares, shadows, and piles of multiple particles in one tile.
```

## 可选技能图标

当前 DemoDay 的 `Icon` 子物体已经分别引用 `jt_xhz_03.png` 和 `04-burst-star.png`。这里是专属图标升级项，生成后替换各自的图标层即可。圆形按键底、按键字样、冷却遮罩和蓄力进度由现有 HUD 提供。[DemoDay 场景](../../../Assets/SimpleLowPolyNature/Scenes/DemoDay.unity)

### 8. 回荡图标 · SkillIconSwordQiEcho.png

三到四道纤长银白剑影以不同角度错位绕行，中心留出空白。只画中央图形，适配现有圆形按钮。

```text
Create a 256 x 256 straight-alpha RGBA HUD icon symbol for the fantasy skill Sword Qi Echo, on a transparent canvas. In a centered circle-safe composition draw three to four pointed silver-white energy strokes curving in staggered directions around a clear open center. Use cool icy-blue edges and strong gaps between strokes. Keep the motif within the middle 75 percent of the canvas and readable when viewed at 32 pixels on a gray circular button. Graphic stylized fantasy silhouette with a subtle luminous edge. Exclude a solid button base, enclosing rim, text, a hotkey, a cooldown mark, a literal sword hilt, detailed scenery, and any backdrop.
```

### 9. 炎爆图标 · SkillIconChargeFire.png

一个暖白火核从宽低的火团里冲起，底部两侧打开，两三瓣大火舌形成明确轮廓。中心亮、外沿橙红，用低细节和大色块保证手机上的辨识度。

```text
Create a 256 x 256 straight-alpha RGBA HUD icon symbol for the fantasy skill Charged Fire Burst, on a transparent canvas. One small ivory ignition core opens into a wide, low orange-red flame bloom with two or three large outward-curling flame lobes. Strong stylized silhouette and clear warm color blocks with restrained glow. Center it in a circle-safe area within the middle 75 percent of the canvas so it reads at 32 pixels on a gray round button. Exclude a forward-moving fireball or meteor, solid button base, enclosing rim, ground, charge meter, text, hotkey number, heavy smoke, and any opaque background.
```

## 接入后看这几处

- 回荡至少各看一次单目标和多目标。四道同时交叉时要能追踪各自剑尖，转弯时尾迹方向一致，回池后无残留。命中裂光只出现在有效命中。
- 炎爆分别短蓄和满蓄。短蓄要能看见起爆与扩张方向，满蓄要有更饱满的轮廓。火环中心、火团起点要和实际释放点重合。检查远端观察者看到的释放范围，以及取消蓄力后的聚火残留。
- 资源落地前先把首批四张放进同一次实机预览。重点核对亮草地上的透明边、默认镜头下的技能可读性及同屏叠加时的遮挡。
