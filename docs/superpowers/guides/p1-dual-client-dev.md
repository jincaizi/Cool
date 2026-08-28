# P1 双客户端联调指南

## 方式 A：ParrelSync 克隆（推荐，若已安装）

1. Unity 包管理器添加 `com.unity.multiplayer.playmode` 或 ParrelSync（git URL：
   `https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync`）（需网络环境安装——
   本机当前网络不可达 GitHub，安装失败时用方式 B 兜底）
2. ParrelSync 窗口 → Create Clones → 打开克隆 Editor
3. 两个 Editor 各自运行场景；克隆 Editor 需单独配置服务器地址（见下文"双开注意"）

## 方式 B：手动双开（无插件兜底）

1. 复制仓库目录 `E:\CodeForJob\Cool` → `E:\CodeForJob\Cool-C2`（复制后删除其中
   `Library/`、`Temp/`、`Logs/`、`obj/`——只保留 `Assets/`、`Packages/`、`ProjectSettings/`、
   `Server/` 等源码；如目录含 `Library` 首次双开会冲突）
2. Unity Hub → Add → 选择 `Cool-C2` → 两个 Editor 同时打开（首开较慢：重新导入+编译）
3. 双方各自启动服务器或共用一台服务器（见下）

## 双开注意

- 服务器：`dotnet run --project Server/Server3C.csproj -- 8000`（一台机器跑一个即可，
  两客户端连 127.0.0.1:8000）
- 客户端连接入口：P1 阶段为 PlayMode 测试（TwoClientWorldTests）或挂
  `ClientNetworkManager` 的宿主脚本；游戏 UI 登录接线在后续阶段
- 两个 Editor 的 `Assets` 目录互相独立——改代码需同步（ParrelSync 自动同步；
  手动双开需自行复制或双份编辑）

## 验收清单（双开手测）

1. 两个 Editor 各自登录进同 Zone（服务器日志出现两行 `Player N logged in`）
2. A 场景中出现 B 的胶囊体占位（RemoteEntityRegistry 生成）；B 移动时 A 端位置更新
   （P1 为快照直接定位，无插值——P2 平滑；P1 阶段位置恒为出生点）
3. 关掉 B → 约 15s 后 A 端 B 实体消失（服务器 PeerSilence 检测窗口，T8 落地）；
   B 重开重连 → A 端 B 重新出现
