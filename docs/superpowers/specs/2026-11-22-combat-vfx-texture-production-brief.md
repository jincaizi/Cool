# 战斗特效贴图需求与生图 Prompt

这批新增 **11 张贴图**。现有炎爆火团整套迁给火球术落地，已经可用的火焰资源继续复用。尺寸按当前 Unity Built-in、移动端和 WebGL 的使用场景建议。每个代码块是一条独立 prompt，可按清单的文件名分派给不同生成 agent。

## 六项需求与现有资源

| 项目 | 画面方向 | 现有素材与接入点 |
| --- | --- | --- |
| 火球术 · 20003 | 飞行火球触地时先亮一下，再出现橙红火团、矮火环和少量余烬。效果从实际落点发出。 | 原炎爆的 `ChargeFireBurstFlipbook.png`、`ChargeFireGroundRing.png`、`ChargeFireEmbers.png` 整套复用到火球 `ReleaseVFX`。火球当前的释放预制体已有挂点。 |
| 蓄力炎爆 · 20004，键盘 2 | 释放瞬间，以角色脚下为圆心扫出一圈薄、亮的热浪。光圈快速推远，边缘散开并淡出。保留蓄力火丝作为蓄力信号。 | 用新 `ChargeBlastShockRing.png` 做释放主体。`ChargeFireGatherRibbon.png` 继续用于蓄力。释放圆心记录在出手时，角色随后移动时光圈留在原位。 |
| 旋转斩 · 20002，键盘 R | 冰白细刃贴着剑身运行，尾端拉成渐隐的冷雾丝。连续旋转时能看清剑的方向和每圈的断点。 | 当前 `WeaponTrailRenderer` 动态创建材质和拖尾，使用新 `SpinFrostTrail.png`。由引擎记录刀路，贴图沿刀路拉伸。 |
| 陨石术 · 30001 | 先读到危险范围，再看到带石质主体的陨石落下，落地向外溅出碎屑、热光和低矮烟尘。 | 当前 `MeteorStrike` 有预警和落地两个时点，配置预警约 0.8 秒、半径 4 米。`WarnDecal` 使用单色材质，`Explosion_Meteor` 的粒子材质槽为空。可视下坠体和尾迹需接入预警末段。 |
| 防御 · F / 触控防御 | 成功进入防御后，围绕角色亮起细薄浅金护圈。成功格挡时短促提亮，退出防御后收掉。 | 本地 `IsDefending`、远端防御快照驱动开关；格挡瞬间复用现有金色火花，护圈用同一张图做亮度脉冲。 |
| 属性剑气命中状态 | 灼烧是贴身小火，冰霜是脚踝结霜，麻痹是断续紫电，易伤是断开的青色风带。 | 各状态图接在受影响目标的状态表现上。属性剑气项目已有冰、火、雷、风状态路径和程序化外观，可在对应状态预制体加贴图。飞行剑气沿用现有表现。 |

炎爆推开属于战斗逻辑。预期在释放时对范围内有效敌对目标沿角色圆心的地面径向推开。单机和联机需对齐目标过滤及推开规则，联机位移由服务端裁决。光圈的视觉终点按实际判定半径对齐。当前炎爆资产的击退力为 0，推力和免疫规则留到玩法接入时确定。

来源见 [技能编号](../../../Assets/Scripts/Hotfix/GameSystems/Skills/Definition/SkillType.cs)、[键盘输入](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs)、[火球落地逻辑](../../../Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Projectile/FireballProjectile.cs)、[炎爆技能资产](../../../Assets/PreRes/SkillsCfg/ChargeFire_SkillR.asset)、[R 武器拖尾](../../../Assets/Scripts/Hotfix/GameSystems/VFX/WeaponTrailRenderer.cs)、[陨石配置](../../../Assets/PreRes/SkillsCfg/MeteorConfig_Fire.asset)、[陨石表现逻辑](../../../Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/Meteor/MeteorStrike.cs)、[防御状态](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs)、[格挡反馈](../../../Assets/Scripts/Hotfix/GameSystems/VFX/HitFeedbackSystem.cs)和[属性剑气设计](2026-11-14-elemental-sword-qi-design.md)。

## 生图交付清单

| 优先顺序 | 文件名 | 尺寸 | 使用方式 |
| --- | --- | --- | --- |
| 1 | `ChargeBlastShockRing.png` | 512 × 512 | 炎爆地面环，释放时缩放和淡出 |
| 1 | `SpinFrostTrail.png` | 512 × 128 | R 挥剑拖尾，横向 UV，支持引擎染色 |
| 1 | `MeteorWarningRing.png` | 512 × 512 | 陨石俯视预警贴花 |
| 1 | `MeteorFallBody.png` | 512 × 512 | 陨石下坠主体面片 |
| 1 | `MeteorFallTrail.png` | 512 × 256 | 陨石下坠气尾，横向 UV |
| 1 | `MeteorImpactFlipbook.png` | 1024 × 1024，4 × 4 | 陨石落地的 16 帧冲击与尘浪 |
| 1 | `DefendHaloRing.png` | 512 × 512 | 防御护盾光圈 |
| 2 | `ElementBurnLoop.png` | 1024 × 512，4 × 2 | 目标灼烧，8 帧循环 |
| 2 | `ElementFrostFootRing.png` | 512 × 512 | 目标脚下结霜和短晶沿 |
| 2 | `ElementThunderArc.png` | 512 × 256 | 目标周身或头肩的短电弧 |
| 2 | `ElementWindRibbons.png` | 512 × 128 | 风易伤的轻薄环绕丝带 |

### 通用规格

- RGBA PNG，直通 Alpha，空白 Alpha 为 0。柔光写入透明通道。不要烘黑底、白底、棋盘格、字样或环境。每个文件单独交付。
- 从现有低多边形游戏镜头看，轮廓优先。主效果可有少量精致纹理，小粒子和状态图保留大块透明。避免整片白光盖住角色或目标。
- 图集的阅读顺序从左到右、从上到下，每格沿外缘留 **至少 8 px 真透明**。同一系列固定镜头、缩放和锚点。生成器难以一次生成连贯序列时，按编号输出单帧透明 PNG，由接入方打包。
- 环形贴图统一画正俯视圆，圆心在图像中心，无遮挡的中心区方便地面叠加。UV 横条的两端约定写在各条 prompt 中，接入后按实际渲染方向核对。

## 1. 蓄力炎爆 · 向外推开的热浪圈

**`ChargeBlastShockRing.png`**

正俯视的窄环，外沿是偏白金的亮刃，内侧是透薄的橙红热浪。中心大面积透明，周向有少量轻微断开和不均匀涟漪。成品只需一张图，快速扩张和淡出由 Unity 控制。环应能读出向外的一次冲击，避免高火舌遮挡被推开的敌人。

```text
Create a 512 x 512 straight-alpha RGBA PNG of a stylized radial heat shockwave for a low-poly fantasy game. Strict top-down orthographic view, perfectly centered circular footprint. Draw a thin fast-moving ivory-gold leading edge (#FFF1BE), with a narrow warm orange-red inner fringe (#FF923C) and a few irregular soft gaps around the circumference. The outer radius is about 43 percent of the image width. Keep the center fully transparent and the outer halo softly transparent. The shape should read as one horizontal outward pulse when enlarged across a grassy floor. Exclude tall flames, a central explosion, filled glow disks, smoke clouds, ground texture, runes, lettering, shadows and any opaque background.
```

## 2. R 旋转斩 · 冰霜武器拖尾

**`SpinFrostTrail.png`**

按横向伸展的拖尾 UV 绘制。左端是贴剑的紧凑亮刃，右端化为两三缕低亮冷雾并归零。长轴平直，弧度交给武器轨迹。颜色接近冷白以便现有材质染色，冰霜辨识来自细微晶纹和透明层次。接入时在剑身挥动窗口发射，停止后按尾迹寿命自然拖净。

```text
Create a 512 x 128 straight-alpha RGBA PNG for a stretched ice sword swing trail. Draw one flat horizontal ribbon with the fresh blade end at the left and the old fading end at the right. Keep the bright pearl-white cutting filament near the horizontal midline. Use near-neutral icy-white tones, a hint of pale blue and a few subtle crystal striations so a Unity material can tint it. The ribbon narrows into two or three faint wisps and reaches zero alpha at the right. Top and bottom fade smoothly into transparency. Exclude baked circular arcs, a sword or hilt, snowflake icons, chunky ice shards, a second flare, solid rectangular fill, text and background.
```

## 3. 陨石术 · 预警、飞行、落地

### 预警贴花 · MeteorWarningRing.png

正俯视、深橙红的圆形危险轮廓。外圈比炎爆更完整，内沿带分布稀疏的短刻痕，中心基本透明。采用低频呼吸或边缘亮度上升提示砸落，判定边界由引擎画准。

```text
Create a 512 x 512 straight-alpha RGBA PNG for a meteor warning decal in a stylized action RPG. Strictly top-down orthographic, exact circular center and marginal transparent padding. Design a thin, mostly continuous dark red-orange perimeter (#DE4A35) with small inward-facing ticks and sparse ember-toned cracks just inside the border. Leave the large interior transparent and make the perimeter legible on both light grass and dark stone. The art is a calm anticipation marker with controlled glow. Exclude flames rising off the floor, an impact flash, a filled disc, arrows, text, numerals, runes, an ellipse, perspective, and any opaque background.
```

### 下坠主体 · MeteorFallBody.png

独立的黑褐碎岩，轮廓有明显大块切面，熔光从不规则裂缝透出。岩体决定陨石的重量，尾焰使用独立贴图。画单个侧视岩石，运行时跟随下坠方向旋转，可用交叉面片组装。

```text
Create a 512 x 512 straight-alpha RGBA PNG of a single isolated molten meteor body for a small on-screen fantasy VFX billboard. Orthographic three-quarter side view of a compact irregular black-brown rock with a readable asymmetrical faceted silhouette. Use three or four large planes, a few glowing lava seams (#FF6B25) and a restrained warm rim. Keep the rocky mass prominent and center it with generous transparent borders. The shape must still look like a heavy stone at 40 pixels. Exclude a long flame tail, a fireball, smoke clouds, debris field, planet, landscape, drop shadow, text and opaque background.
```

### 下坠气尾 · MeteorFallTrail.png

横向 UV，左端连接下坠岩体，右端拉成断续尾焰和细灰尘。前段短而明亮，后段比火球尾焰更宽、颜色更深。材质根据陨石速度拉伸并转向。

```text
Create a 512 x 256 straight-alpha RGBA PNG for a stretched molten meteor wake. One horizontal ribbon on transparency, with the dense fresh attachment end at the left and the long dissipating end at the right. Start with a short gold-orange hot stream, stretch it into layered orange-red flame tongues with a few sparse charcoal-gray wisps, then break into faint tapered strands at zero alpha. Keep the energy axis near the center vertically. Convey a much heavier object than a small fireball while leaving ample transparent gaps. Exclude a drawn rock, diagonal preset path, large circular fireball, stars, ground, full-width smoke fill, labels and background.
```

### 落地冲击图集 · MeteorImpactFlipbook.png

冲击重心压低。暖白触地一闪后，横向尘浪迅速打开，有几块短程碎石向低处抛出。后半段留少量灰褐尘和橙色余热。图集每格 256 × 256，地面锚点统一为横向 50%、从顶部往下 82%。

| 帧 | 变化 |
| --- | --- |
| 01–03 | 地面接触点亮，出现贴地扁平冲击闪光。 |
| 04–07 | 橙金亮缘向两侧撑开，碎石从低处抛出。 |
| 08–12 | 亮边降低，向外扇开的灰褐尘浪形成明显透明空隙。 |
| 13–16 | 尘土贴地散开，只留几粒余光，末帧接近透明。 |

```text
Create a 16-frame stylized heavy meteor impact flipbook for a low-poly fantasy game. Deliver a 1024 x 1024 straight-alpha RGBA PNG with exactly four columns by four rows, 256 x 256 per tile, ordered left to right and top to bottom. Use a fixed orthographic three-quarter low view and a fixed ground origin at 50 percent of tile width and 82 percent down from the top. Leave at least 8 pixels of fully transparent border around each tile. Frames 1 through 3 show a low ivory-gold ground impact flash. Frames 4 through 7 spread a broad hot edge outward with a few large dark fragments jumping low. Frames 8 through 12 open into thin rolling warm-gray ground dust with visible gaps and sparse orange embers. Frames 13 through 16 disperse into almost zero alpha. Keep the main silhouette wide, low and weighty. Exclude tall fireball blooms, mushroom clouds, a crater background, terrain texture, circular UI marks, labels, cell borders, and camera movement.
```

## 4. 防御 · 护盾光圈

**`DefendHaloRing.png`**

细薄暖金护圈，外缘少量断开的象牙白高光。俯视圆环可以放在身体周围的水平环面，视觉高度对齐腰部附近。中心透明，防御者身形和持盾姿态保持清楚。常驻状态用柔和亮度，格挡成功时沿用这一张图提亮一次。

```text
Create a 512 x 512 straight-alpha RGBA PNG of a delicate protective halo for a held guard stance in a fantasy action game. Exact top-down circle, centered and evenly balanced. Draw a slim warm champagne-gold ring (#F7C878) with a second much fainter inner line and a few short ivory-white edge highlights. Keep the interior fully transparent and the glow soft, steady and understated so it can surround a visible character at waist height. Add small transparent gaps of differing lengths to keep the silhouette light. Exclude flames, solid bubble fill, a shield icon, a holy emblem, rune writing, a ground warning marker, text, opaque background, and lens-flare streaks.
```

## 5. 属性剑气 · 受击后的状态贴图

这组是挂在受影响目标身上的小尺寸状态资源。表现按实际状态开始与结束控制。灼烧循环期间只做小火起伏，伤害跳字沿用战斗结果；冰系附带减速时用脚踝霜纹表达，角色仍可能移动；雷麻痹和风易伤各用独立轮廓和色相。

### 灼烧循环 · ElementBurnLoop.png

8 帧小火循环，每格 256 × 256，适合围绕脚边和躯干外缘摆少量粒子。轮廓由两三束细小橙红火舌组成。首尾火势相近，循环接头不过亮。

```text
Create an 8-frame seamless looping burn status flipbook for small particles attached to a fantasy character. Deliver a 1024 x 512 straight-alpha RGBA PNG, four columns by two rows, 256 x 256 per tile, numbered left to right then top to bottom. Keep a fixed lower-center pivot, fixed view and consistent overall scale. Draw two or three small orange-red flame tongues with a subdued pale-gold core. They should lean, curl, split and gently return to the starting posture across the loop. Keep frames 8 and 1 close enough for a smooth wrap and leave at least 8 transparent pixels around each tile. Exclude an ignition flash, impact expansion, a floor ring, smoke blanket, outlined character, text, cell borders and any opaque background.
```

### 冰霜脚环 · ElementFrostFootRing.png

正俯视的不规则霜纹环。边缘浅蓝、冷白，几簇矮晶角贴在外围，中心足够空。随目标脚下移动，视觉像薄霜附着，适合表示冰系减速。

```text
Create a 512 x 512 straight-alpha RGBA PNG of a light frost status decal around a character's ankles. Strict top-down orthographic. Center an uneven near-circular scattering of thin translucent ice veins, small flattened crystal facets and a handful of bright pearl-blue tips (#CDEFFF). The middle remains clear and fully transparent. Let the frost edge break up organically with sparse, asymmetrical flakes and soft blue falloff. The decal should read as thin frost forming under moving feet on grass or stone. Exclude a solid ice plate, a frozen character, prison walls, thick spikes, snowflake symbols, opaque fills, text and background.
```

### 雷麻痹电弧 · ElementThunderArc.png

侧视的一条短折电，银紫芯，两三处小分叉。可以在角色肩头和周身错时闪现。宽大的主折线能在小尺寸读清，细碎分支只用作边缘点缀。

```text
Create a 512 x 256 straight-alpha RGBA PNG of a short paralysis electricity sprite for a fantasy combat status. Draw one clean broken zigzag arc running broadly left to right with two or three short asymmetric forks. Use a violet-white core (#F0DBFF) and contained lavender edges (#AD73FC). Give the sprite a clear angular rhythm and true transparent gaps, suitable for brief flashes around a character's shoulders. Low detail, crisp turns, tapered endpoints, no dense glow cloud. Exclude a vertical lightning strike from the sky, explosions, spherical bubbles, UI stun stars, a character, text, and any background.
```

### 风易伤断带 · ElementWindRibbons.png

两缕有明显间距的青白气流。横向展开采样，头尾都向透明收束。装到环绕带时留一个自然开口，可以轻轻流动。色彩与防御金环、冰霜蓝环有明显区分。

```text
Create a 512 x 128 straight-alpha RGBA PNG for a subtle wind vulnerability effect on a fantasy combat target. Draw two narrow interrupted horizontal currents, one pale mint and one soft teal (#88F3D6 and #39BDAE), separated by a clearly transparent gap. Make the strands gently curl and briefly converge before separating again. Both ends taper to zero alpha so the texture can wrap on a ribbon with a deliberate opening. Restrained glow, fluid silhouettes and large empty areas. Exclude a solid circular shield, green poison smoke, a tornado, opaque bands, arrows, text, numbers and any background.
```

## 接入验收重点

- 火球术一发只在实际落点出一次火团，移动镜头时能清楚读到落点。
- 炎爆的释放圆心跟判定一致。短蓄和满蓄都能看清扩散半径，敌对目标的推出方向与圆心一致。
- R 连续转动、再次按键取消及中断时，剑尾自然变细消散，角色主体依然清楚。
- 陨石预警边缘、下坠方向和爆炸中心三者连续。晚到的远端表现按已有时序补进度，落地亮点对齐判定。
- 防御光圈随移动和转身跟随角色；松开、精力耗尽、死亡或离场时正确收掉。属性状态要能在交战中区分，过期或目标离场时及时移除。
