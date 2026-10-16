# WebGL 面试演示 —— 部署 Runbook

> **目标：面试官用自己的设备打开一个 URL 就能玩。**
>
> 目标机：**腾讯云轻量应用服务器，Windows**。
>
> ⚠️ **本文档里凡是标了「云服务器上执行」的步骤，写这份文档的人都没有做过** ——
> 没有凭据、没有网络路径，云上那台机器我碰不到。所以那些步骤是**给人类的操作清单**，
> 每条都配了"怎么知道它成功了"。能在这台开发机上按同样方式跑的，都真跑过了，
> 数字与证据在 §7 和 `production/verify/t9/`。

---

## 0. 一句话架构

```
面试官的浏览器
   │  GET http://<公网IP>:8080/          → StaticServer.exe   （静态站点，边传边 gzip）
   └  WS  ws://<公网IP>:8000/            → Server3C.exe       （游戏服务端，HttpListener）
```

两个进程、两个端口、同一台机器。没有反向代理，没有域名，没有证书。

### 为什么不需要域名 / 证书 / 备案（这是裁定，不是省略）

* `ProjectSettings` 里 `webGLThreadsSupport: 0` ⇒ **不用 `SharedArrayBuffer`** ⇒
  不需要跨源隔离（COOP/COEP）⇒ **不需要 HTTPS**。于是明文 `http://` + `ws://` 直接可用，
  浏览器不会因为"HTTPS 页面连 ws://"而拦截 —— 因为页面本身就是 http。
* **ICP 备案针对的是「域名 + 大陆服务器 + 80/443」**。裸 IP + 非标端口（8080/8000）不在此列。
* 代价要如实说：**没有 TLS，流量明文**，且**没有鉴权**（知道 IP 的人都能连）。
  这是演示形态的刻意取舍（见 `webgl-demo-boundaries.md` 第 ⑥ 条）。

---

## 1. 要放行的两个端口

| 端口 | 协议 | 谁在监听 | 腾讯云安全组 | Windows 防火墙 |
|---|---|---|---|---|
| **8080** | TCP | `StaticServer.exe`（WebGL 包） | **放行** | `setup-once.cmd` 自动加 |
| **8000** | TCP | `Server3C.exe` 的 WebSocket 监听 | **放行** | `setup-once.cmd` 自动加 |

* 安全组是**控制台侧**的一道门，脚本改不到它 —— 必须人工去腾讯云控制台
  「轻量应用服务器 → 防火墙」加这两条 TCP 规则。
* **8000 的 UDP 不用开**：服务端同时绑了 UDP 8000 给原生 KCP 客户端，但浏览器演示走的是
  TCP(WebSocket)。不开 UDP 只意味着 PC 原生客户端连不上，不影响面试官。
* 两个端口都必须**允许任意来源 IP** —— 面试官的设备不在你的白名单里。

---

## 2. 在开发机上：出包 + 打包

```bash
# 2.1 出 WebGL 包（Unity 菜单 Cool/Build/WebGL，或批处理调用
#     Cool.EditorTools.WebGLBuildScript.BuildWebGL）
#     构建脚本会临时把压缩设为 Disabled（部署层负责 gzip），出包后自动还原项目设置，
#     并自查产物里没有 .gz/.br、四个 payload 文件都在。
#
# 2.2 打包成"拷过去就能跑"的 dist/
bash production/deploy/webgl-demo/publish-server.sh
```

产出（`production/deploy/webgl-demo/dist/`）：

| 目录 | 内容 | 大小 |
|---|---|---|
| `dist/site/` | `Build/WebGL/` 的副本（**未压缩的原文件**，gzip 在传输层做） | 73 MB |
| `dist/server/` | 自包含 win-x64 发布：`Server3C.exe` + `StaticServer.exe` + .NET 运行时 + `Config/monster_config.json` | 72 MB |

**「自包含」的含义**：目标机**不需要装 .NET SDK、运行时、Node、Python、IIS**。
整个 `dist/` 拷过去就能跑。`publish-server.sh` 自带三条断言（产物里有 `Server3C.exe`、
有 `StaticServer.exe`、有 `Config/monster_config.json`），任何一条不满足就非零退出 ——
缺 `monster_config.json` 的失败模式是"服务端正常启动、怪物一条都不刷、没有任何报错"。

### 把什么拷到云服务器

把 **`production/deploy/webgl-demo/` 整个文件夹**（不含 `dist` 也可以，但要自己在云上重建）
拷过去。云上只需要这个文件夹：

```
webgl-demo/
  dist/                   ← 72 + 73 MB，拷过去
  StaticServer/           ← 源码，云上不需要，但留着便于排错
  static-server.js        ← Node 版对照实现，云上不需要（见 §9 备选）
  publish-server.sh       ← 开发机用
  setup-once.cmd          ← 云上跑一次（管理员）
  start-demo.cmd          ← 云上双击
  start-static.cmd / start-game-server.cmd
```

---

## 3. 云服务器上，一次性准备（**需人类在云服务器上执行**）

### 3.1 跑 `setup-once.cmd`（**右键 → 以管理员身份运行**）

它做两件 Windows 层面的事：

1. **URL ACL** —— `HttpListener` 绑非 localhost 前缀默认被拒。
   T8 在开发机上实测过这条：不加 ACL，服务进程直接抛
   `拒绝访问。` 然后退出（日志见 `production/verify/t8/_lanprefix-test.log`）。
   脚本等价于手工执行：
   ```
   netsh http add urlacl url=http://+:8000/ user=Everyone
   netsh http add urlacl url=http://+:8080/ user=Everyone
   ```
   > 为什么前缀里是 `+`：`HttpListener` 的 host 段是**字面匹配**请求的 `Host` 头的。
   > 写 `127.0.0.1` 就只认本机访问；面试官用 `<公网IP>:8000` 拨过来时 Host 头对不上，
   > **连接会被直接拒掉（连 TCP 都建不起来）**。`+` 是强通配，必须用它。

2. **Windows 防火墙**：给 TCP 8080 / 8000 加入站放行规则。

**怎么知道成功了**：脚本末尾会打印两行 URL ACL 的确认；没有 `[ERROR]` 字样。

### 3.2 腾讯云控制台放行安全组

「轻量应用服务器 → 防火墙 → 添加规则」：**TCP 8080**、**TCP 8000**，来源 `0.0.0.0/0`。

**怎么知道成功了**：在**另一台**设备上 `curl -v http://<公网IP>:8080/` 能拿到 HTML。
本机上能连不等于放行成功 —— 安全组只拦截来自外部的流量。

---

## 4. 演示当天（**需人类在云服务器上执行**）

### 4.1 起服务

双击 `start-demo.cmd`。它会开两个窗口：

* `静态站点 8080` → `StaticServer.exe --root dist\site --port 8080 --prefix http://+:8080/`
* `游戏服务端 8000` → `Server3C.exe 8000 --monsters 6 --bots 100 --ws-prefix http://+:8000/`

看到这两行才算起来了：

```
StaticServer listening on http://+:8080/
Server3C is running on port 8000
```

`--bots 100` 是 AOI 那条声称面要的规模。`--monsters 6` 是为了让两只新怪（DogKnight / SpiderBoss）出现在演示里——
**它只管场上有几只、看得到哪些，不管安全**（安全与数量无关）。出生点自 2026-10-17 第三次搬迁后是
**(-15, 0.3, -30)**（历史：08-27 原点系 → 10-13 湖东岸 (10,0.21,-40) → 现在是这里，布局同批收紧收进陆地），
对全部六只怪都安全：**站在出生点不动不会被任何怪发现**（几何：每个槽位距出生点 > 感知+巡逻，最小余量
[2] Slime +2.8m；行为侧 = 挂机 3 分钟零施法/0 伤害的用例）。但这只管出生点——**离出生点最近的怪是
(0,0,-35) 的 [2] Slime：朝它走出约 3m 就进它的感知圈被追**（[0] Wolf 现在距出生点 33.5m，巡逻圈够不着），
想活着就**动起来或打回去**（这是设计意图）；阵亡 = 回选人界面重登（重登即满血）。
详见 `webgl-demo-boundaries.md` 的"演示操作建议"。

> 顺带一条 2026-10-17 实测（与新布局无关、但与 `--bots` 有关）：**40 个 bot 的人群会把怪挡在半路**——
> `--bots 40 --bots-radius 60` 下挂机观察者 60s 内零施法（狼最近只到 8.6m/14.0m，走不进来）；
> 同一构建去掉 bots 后 0.6~19s 就被追上打。怪物 e2e（`MonsterWorldTests`）请对着**不带 bots**的服务端跑。

### 4.2 给面试官的话

> 打开 **`http://<公网IP>:8080/?host=<公网IP>`** —— 第一次要等几十秒（数字见 §7）。
> 进了选人界面后：**右边选「联网」，再点确认**（服务器地址已经由链接带进来了，不用填）。

**地址由 URL 参数 `?host=` 带进页面**（Task 13）——发链接的人把地址写进去，收链接的人一个字都不用打。
必须如此的原因：客户端默认值是 `127.0.0.1`（`NetworkModeService.DefaultHost`），
在面试官的浏览器里那指的是**他自己的机器**；手填公网 IP 是现场最容易出岔子的一步。

| 链接形态 | 生效地址 |
|---|---|
| `?host=<公网IP>` | `<公网IP>:8000`（不带端口 → 默认端口 8000） |
| `?host=<公网IP>:8002` | `<公网IP>:8002`（§9 的代理路线用这个） |
| 不带 `?host=` | 与加这个功能之前**完全一致**：存档值 → 默认 `127.0.0.1:8000` |

* 参数值畸形（`?host=`、`?host=:9000`、端口不是数字或越界）一律**忽略**、绝不报错——
  退回上面的"不带 `?host=`"行为，不会把页面卡住。规则与单测见 `ServerAddressQuery` /
  `ServerAddressQueryTests`。
* 选人面板里的地址框会**预填**链接带来的地址（框里显示的就是实际会连的），
  点「确认」后它会像手填一样落进存档。
* 手机演示两个参数一起带：**`http://<公网IP>:8080/?touch=1&host=<公网IP>`**
  （`?touch=1` 的说明见 boundaries 第 ④ 条；两个参数的顺序无所谓）。

### 4.3 停服

关掉那两个窗口即可（`Ctrl+C` 也行 —— `Program.cs` 注册了 `CancelKeyPress`，会走优雅关闭）。
窗口留着别关，面试官问"服务器怎么样"的时候日志就在眼前。

---

## 5. 云上必须自己验的四件事（**本地验不了**）

| # | 做什么 | 期望 |
|---|---|---|
| 1 | 用**另一台设备**（手机 4G / 另一台电脑）打开 `http://<公网IP>:8080/?host=<公网IP>` | 能加载、进度条走到头、进选人界面，且**地址框里已经是 `<公网IP>`** |
| 2 | 选「联网」+ 确认（不用填地址） | 左下浮层出现 `状态=InWorld`（中文字符在 WebGL 包里不显示，看英文锚点 `InWorld` 和数字） |
| 3 | 按 W 走动、点一下挥砍 | 角色移动、有攻击动作 |
| 4 | 观察浮层的"远程实体"计数（`--bots 100` 下） | 从 0 跳到 8–9，证明 AOI 可见集合在服务端算 |

第 1 条**不能用本机浏览器代替** —— 本机连得通只证明 `127.0.0.1` 通。

---

## 6. 排错

| 症状 | 原因 | 处置 |
|---|---|---|
| `StaticServer` 窗口一闪就报 `无法监听 ... 拒绝访问` | URL ACL 没加 | 管理员跑 `setup-once.cmd` |
| 本机能玩、面试官打不开页面 | 安全组没放行 8080 | 控制台加 TCP 8080 |
| 页面能打开、点确认连不上 | 安全组没放行 8000，或服务端前缀不是 `http://+:8000/` | 两处都查；`netstat -ano \| findstr :8000` 看有没有在听 |
| 连接被拒、服务端日志**什么都没有** | 前缀 host 段不匹配（写成了 `127.0.0.1`） | 用 `--ws-prefix http://+:8000/` |
| 进度条走完但画面全黑 | 场景显卡驱动/WebGL2 兼容性 | 换浏览器试；SwiftShader 软渲是黑地形伪影的下限参照 |
| 面试官手机触控 UI 没出来 | 移动端判定不可靠 | URL 加 `?touch=1` |
| 地址框里不是链接里的地址 | `?host=` 拼错 / 值畸形（畸形一律被忽略，退回存档或默认） | 核对链接；**选人面板的地址框显示的就是实际会连的地址**，以它为准 |

---

## 7. 实测数字（本地口径，`production/verify/t9/`）

带宽用 CDP 的 `Network.emulateNetworkConditions` 加在**浏览器这一侧**模拟；
传输量取 `Network.loadingFinished.encodedDataLength` 之和（**上线字节数**，含响应头、
已计入 `Content-Encoding`）；"首帧可用"= `#unity-loading-bar` 被隐藏的时刻
（= wasm 编译完、资源解包完、Unity 出了第一帧）。口径的实现在 `t9-measure-load.js` 顶部。

| 配置 | 传输量 | 首帧可用 |
|---|---|---|
| **未压缩**，5 Mbps / 200 ms RTT | 76,397,416 B（72.9 MiB） | **123.5 s** |
| **gzip**，5 Mbps / 200 ms RTT | 25,515,863 B（24.3 MiB） | **42.1 s** |
| **未压缩**，3 Mbps / 200 ms RTT | 76,394,952 B（72.9 MiB） | **204.9 s** |
| **gzip**，3 Mbps / 200 ms RTT | 25,513,399 B（24.3 MiB） | **69.3 s** |
| gzip，本机环回（不模拟带宽） | 25,515,863 B | 0.84 s |

（原始记录：`production/verify/t9/load-5mbps-{gzip,plain}.json`、`load-3mbps-{gzip,plain}.json`、
`load-loopback-gzip.json`。）

**结论：面试官要等 42 秒（5 Mbps）/ 69 秒（3 Mbps），而不是 2 分钟 / 3 分半。**
腾讯云入门套餐峰值 3–6 Mbps，所以 42–69 s 是要预告给他的数字（§4.2 那句话里的"几十秒"）。

环回那行是分解用的：**0.84 s 是本地 CPU 成本（wasm 编译 + 解包），42 s 里剩下的 41 s 是带宽**——
所以面试官那边慢，慢在网络；压包是唯一有效的杠杆。同时它也说明：**这些数字里的网络部分
是可移植的，CPU 部分取决于面试官的机器**（换台更弱的笔记本，0.84 s 会变长，但相对可忽略）。

---

## 8. 压缩为什么在服务器做而不是在 Unity 里做

`WebGLBuildScript` 出包时**显式把压缩关掉**（`WebGLCompressionFormat.Disabled`），
并且**出包后还原项目设置**（仓库里 `ProjectSettings.asset` 的值仍是 `Gzip`；出包不会把它改掉）。
理由：

* **双重压缩是坏的**：Unity 侧预压缩 + 服务器侧 gzip = 浏览器把 gzip 过的字节当 wasm 执行。
* **预压缩要求服务器改名映射**：Unity 产出 `WebGL.wasm.gz`，服务器得为每个文件维护
  「请求 `X` 就给 `X.gz` + `Content-Encoding`」。任何一处对不上就是 404。
* **预压缩漏掉首屏最慢的那段**：`index.html` / `loader.js` / `framework.js`
  Unity 不压，而它们恰好是"每次都要拉、又不可能缓存热"的那几个。
* 服务器侧压缩的产物形状与 Unity 设置**解耦**：`Build/WebGL/` 永远是原文件。

实测每一文件的压缩率（`production/verify/t9/_gzip_sizes.txt`）：

| 文件 | 原始 | gzip | 比例 |
|---|---|---|---|
| `WebGL.wasm` | 55,103,832 | 13,262,161 | 24.1% |
| `WebGL.data` | 20,820,769 | 12,145,784 | 58.3% |
| `WebGL.framework.js` | 435,338 | 87,326 | 20.1% |
| `WebGL.loader.js` | 20,642 | 7,416 | 35.9% |
| `index.html` | 4,794 | 1,813 | 37.8% |

`WebGL.data` 只压到 58% —— 里面是已经压缩过的贴图资源，这是**下一轮优化真正的杠杆**
（清未使用资源，见 D-W4）。

---

## 9. 备选：拿不到管理员权限时用代理暴露 127.0.0.1

如果 URL ACL 加不上（没有管理员权限），可以不改服务端前缀，改用一层 TCP 代理把
只监听 `127.0.0.1:8000` 的服务端暴露到外网 —— 代理做的是把首个请求块里的 `Host` 头
改写成 `127.0.0.1:8000`，握手之后纯字节透传：

```bash
node production/verify/t8/t8-ws-lan-proxy.js --listen 8002 --target 8000
```

面试官那边服务器地址就要填 **`<公网IP>:8002`（不是 8000）**，安全组要放行 **8002** 而不是 8000。
这条 T8 实测有效（开发机上手机连通过）。**代价**：多一个 Node 依赖和一个进程 ——
所以它是备选，不是主路。

静态站点（8080）没有对应的免管理员方案：页面本身必须监听在一个能被外部访问的地址上，
`HttpListener` 的 `127.0.0.1` 前缀不认外部 Host 头。真拿不到管理员，就得换一个静态服务器
（Node 版在 `production/deploy/webgl-demo/static-server.js`，用法见文件头注释）。
