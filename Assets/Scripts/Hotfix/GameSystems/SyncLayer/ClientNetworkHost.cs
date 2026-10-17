using System;
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using Cysharp.Threading.Tasks;
using Hotfix.GameSystems.UI;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Sys3C.Network;
using UnityEngine;
using UnityEngine.SceneManagement;
using WebSocketNet;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 客户端网络宿主（挂载于世界场景的任意 GameObject）——游戏内网络接线的唯一入口：
    /// 创建 adapter/manager/远程实体渲染根并每帧驱动；联网模式（NetworkModeService）下
    /// Start 自动 Connect()，登录进世界后自动装配本地角色（Sys3CEntry）：
    /// MovementReporter 上报（节流 + 预测帧）、NetMovementPolicy 接管移动与平滑纠偏。
    /// 远程实体的渲染由 `RemoteEntityRegistry` 按 `EntityKind` 分两支（N5b → 2026-10-13 合并）：
    /// 玩家类（真人 + bot）= `_appearances` 外观目录里的角色预制体（目录留空则退回
    /// `_playerAvatarPrefab` 单条默认外观，再留空则退化占位）、怪物 = `_npcPrefabs` 目录里
    /// 按 templateId 取到的真模型（目录未命中/未接线则退化球体占位）。
    /// 本地角色被销毁重建（DemoDay 直跑时选人确认触发 PlayerSpawner 重建）经 PlayerSpawnedEvent 重装配。
    /// 接线步骤与边界见 docs/superpowers/guides/p2-client-network-host.md 与 network-playtest.md。
    /// 单机模式零影响：选单机或只挂不 Connect 即可纯本地游玩。
    /// </summary>
    public sealed class ClientNetworkHost : MonoBehaviour
    {
        /// <summary>断线返回的目标场景——必须与 BuildScript.Scenes[0] 一致</summary>
        private const string MainMenuSceneName = "CharacterSelect";

        /// <summary>
        /// 阵亡后等待服务端复活的兜底（N5）。**必须长于服务端的 8 秒复活延迟**
        /// （`PlayerState.RespawnDelayMs`）——留出"通知在路上"的余量，否则兜底会又变成
        /// 抢在服务端前面把玩家送回选人界面。等满 15 秒仍未收到通知 = 服务器那边出了问题
        /// （连接还活着但没人复活你），这时才复用 T12 的出口：提示 3 秒 → 回选人界面重登。
        /// </summary>
        private const float ServerRespawnFallbackSeconds = 15f;

        [Header("服务器")]
        // 以下三个字段**不再是连接来源**（D-P6-17）：Connect 每次调用时从 NetworkModeService
        // （PlayerPrefs "net.host"/"net.port"/"net.username"）解析。仅作 Inspector 兜底与默认值参考保留。
        [SerializeField] private string _serverHost = "127.0.0.1";
        [SerializeField] private int _serverPort = 8000;
        [SerializeField] private string _username = "Player";

        [Header("本地角色（可选：留空则运行时查找场景内首个 Sys3CEntry）")]
        [SerializeField] private Sys3CEntry _localEntry;

        [Header("远程玩家默认外观（**仅当 _appearances 目录为空时生效**，与目录二选一）")]
        [Tooltip("远程玩家类（真人 + bot）的默认角色预制体（与 PlayerSpawner 用的是同一个）。" +
                 "只在 _appearances 目录为空时生效——目录非空时本字段被静默忽略（目录是主路径，" +
                 "见 Awake 的接线）。两者都留空则退化为胶囊占位——测试宿主就是这种情况；" +
                 "怪物走 _npcPrefabs 目录")]
        [SerializeField] private GameObject _playerAvatarPrefab;

        [Header("远程玩家外观目录（Id 0 = 默认外观；bot 按 id 哈希选条目）")]
        [SerializeField] private AppearanceEntry[] _appearances = System.Array.Empty<AppearanceEntry>();

        /// <summary>
        /// 怪物模型目录的一条（Inspector 可编辑）。**不能换成 `Dictionary`**：
        /// Unity 不序列化字典，场景里配的值会静默丢光（Awake 再建表）。
        /// </summary>
        [Serializable]
        public class NpcPrefabEntry
        {
            public int TemplateId;
            public GameObject Prefab;
        }

        [Header("怪物模型（templateId → prefab；留空则该 template 退化为球体占位）")]
        [SerializeField] private NpcPrefabEntry[] _npcPrefabs = Array.Empty<NpcPrefabEntry>();

        [Tooltip("Npc 落地采样的层掩码。默认 = Default 层（本项目地形层：LowpolyTerrain 在 layer 0）。" +
                 "注意：树/石头也在 Default —— 掩码只作粗筛，命中体还要校验是地形，否则怪会吸到树冠上")]
        [SerializeField] private LayerMask _npcGroundMask = 1;

        [Header("调试")]
        [Tooltip("屏幕左下角连接状态浮层（模式/状态/玩家 id/远程实体数）")]
        [SerializeField] private bool _showDebugOverlay = true;

        // Task 1：字段类型改为接口——KCP 与 WS 两个实现共用
        private INetworkClient? _adapter;
        private ClientNetworkManager? _manager;
        private RemoteEntityRegistry? _remoteEntities;
        private NetMovementPolicy? _netPolicy;
        private bool _connectRequested;
        private bool _inWorld;
        private bool _attachFailed;
        private float _attachRetryTimer;
        private bool _entryLookedUp;
        private CombatNetworkBridge? _combatBridge;

        /// <summary>远程实体渲染根（Awake 建的 `RemotePlayers`）——本地角色查找要排除它整棵子树，
        /// 见 <see cref="FindLocalEntry"/></summary>
        private Transform? _remoteRoot;

        private string? _statusMessage;        // 屏幕浮层上的错误/断线文本
        private float _statusMessageUntil;     // 显示截止时刻（Time.unscaledTime）
        private bool _pendingReturnToMenu;

        // N5 服务端复活：通知在回调线程到达，Unity API 只能在主线程应用（见 HandleServerRespawn）
        private bool _hasServerRespawn;
        private float _serverRespawnHp;

        // N5 阵亡等待：回调线程只置位，计时与提示留主线程 Update
        private bool _awaitingServerRespawn;
        private float _respawnWaitElapsed = -1f;   // <0 = 提示还没上屏（要读 Time，只能在主线程）

        // 本次 Connect 实际解析出的端点——连接失败时上屏（收件人看不到 Console；
        // 读这里而非 PlayerPrefs：Connect 的参数可被显式覆盖，只有这里才是真正尝试的地址）
        private string _resolvedHost = string.Empty;
        private int _resolvedPort;

        public ClientNetworkManager? Manager => _manager;
        public RemoteEntityRegistry? RemoteEntities => _remoteEntities;
        public bool IsInWorld => _inWorld;

        /// <summary>进世界（登录成功，快照流可消费）——宿主在此刻装配本地角色</summary>
        public event Action? OnEnterWorld;

        /// <summary>断线通知（reason）</summary>
        public event Action<string>? OnDisconnected;

        private void Awake()
        {
            var root = new GameObject("RemotePlayers");
            root.transform.SetParent(transform, false);
            _remoteRoot = root.transform;   // 本地角色查找要排除这棵子树（见 FindLocalEntry）

            // Inspector 的数组 → 查询表（registry 只吃表；空目录传 null = 球体降级，与未接线同一条路）
            var catalog = new Dictionary<int, GameObject>();
            foreach (var entry in _npcPrefabs)
                if (entry != null && entry.Prefab != null)
                    catalog[entry.TemplateId] = entry.Prefab;

            _remoteEntities = new RemoteEntityRegistry(root.transform,
                _appearances.Length > 0
                    ? _appearances
                    : new[] { new AppearanceEntry { Id = 0, Full = _playerAvatarPrefab } },
                catalog.Count > 0 ? catalog : null);

            // Npc 视图落地吸附的采样掩码（服务端布局 y≡0 ⇒ 客户端贴地形，见 RemoteEntityRegistry.GroundY）
            _remoteEntities.GroundMask = _npcGroundMask;

            // 观察者位置 = 本地玩家（与 AOI 同心）。取不到（角色还没装配）⇒ 本帧返回 null，
            // registry 那一帧不分档——**不能返回 Vector3.zero**：那会让分档以原点为中心，距离全错。
            _remoteEntities.ObserverPositionProvider = () =>
            {
                if (_localEntry == null && !_entryLookedUp)
                    _localEntry = FindLocalEntry();
                return _localEntry != null ? _localEntry.transform.position : (Vector3?)null;
            };

            // 分档旋钮（URL 覆盖——手机上扫参数不用重新打包）
            string url = Application.absoluteURL;
            if (TierQuery.TryFull(url, out int budget)) _remoteEntities.TierPolicy.MaxFull = budget;
            if (TierQuery.TryFullDistance(url, out float distance)) _remoteEntities.TierPolicy.FullDistance = distance;
        }

        /// <summary>
        /// 场景内首个本地 `Sys3CEntry`。**必须排除远程实体根之下的候选**：远程头像预制体上
        /// 挂着同一个 `Sys3CEntry`（`RemoteEntityAnimator.Awake` 只把它 `enabled = false`），
        /// 而 `FindObjectOfType` **会返回启用 GameObject 上的禁用组件**（2026-10-13 实测：
        /// disabled 组件照样被找到）——在"远端视图已建、本地角色尚未装配"的窗口里，不排除的话
        /// 观察者位置（分档中心）与移动上报会接到**别人身上**。
        /// </summary>
        private Sys3CEntry? FindLocalEntry()
        {
            foreach (var entry in FindObjectsOfType<Sys3CEntry>())
                if (_remoteRoot == null || !entry.transform.IsChildOf(_remoteRoot))
                    return entry;
            return null;
        }

        private void Start()
        {
            // 模式由选人面板选择并持久化（NetworkModeService）：联网 → 进世界自动连接；单机 → 完全不触网
            if (NetworkModeService.Load() == NetworkMode.Online)
            {
                Debug.Log($"[NetHost] 联网模式——连接 {NetworkModeService.LoadHost()}:{NetworkModeService.LoadPort()}");
                Connect();
            }
        }

        private void OnEnable()
        {
            // 本地角色（重）生成通知——含 DemoDay 直跑时选人确认触发 PlayerSpawner 销毁重建
            EventBus.Subscribe<PlayerSpawnedEvent>(OnPlayerSpawned);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerSpawnedEvent>(OnPlayerSpawned);
        }

        /// <summary>
        /// 本地角色（重）生成：整体重装配。旧装配（reporter provider/policy 纠偏订阅/战斗桥）
        /// 指向已销毁对象——不摘会导致每帧 MissingReferenceException 且新角色永不上报。
        /// 注意：不在回调里 FindObjectOfType&lt;Sys3CEntry&gt;——Destroy 帧末才生效会抓到垂死旧对象；
        /// 事件载荷的 Animator 即新角色（与 PlayerSpawner 同款取组件方式）。
        /// </summary>
        private void OnPlayerSpawned(PlayerSpawnedEvent e)
        {
            if (e.PlayerAnimator == null) return;
            var entry = e.PlayerAnimator.GetComponent<Sys3CEntry>();
            if (entry == null) return;

            ResetAttachment();
            _localEntry = entry;
            _entryLookedUp = true; // 已定位——Update 的重试逻辑直接装配新角色

            // DemoDay 直跑路径：模式在选人面板确认时才落盘（宿主 Start 读到的仍是旧值/单机）——
            // 玩家（重）生成即确认动作的副作用，此处补连接（正常流程 Start 已连，_connectRequested 守卫）
            if (!_connectRequested && NetworkModeService.Load() == NetworkMode.Online)
            {
                Debug.Log($"[NetHost] 联网模式（选人确认）——连接 {NetworkModeService.LoadHost()}:{NetworkModeService.LoadPort()}");
                Connect();
            }
        }

        /// <summary>摘除本地角色装配（移动上报/纠偏订阅/战斗桥）——重装配前调用（幂等）</summary>
        private void ResetAttachment()
        {
            if (_manager != null)
            {
                _manager.DetachMovementReporter();
                if (_netPolicy != null)
                    _manager.OnCorrectionNeeded -= _netPolicy.ApplyServerCorrection;
            }
            DetachCombatBridge();
            _netPolicy = null;
            _attachFailed = false;
            _attachRetryTimer = 0f;
        }

        private void Update()
        {
            // —— 服务端复活（N5）——放在"回选人"判定**之前**：通知到了就不该再走返回路径。
            // 封送点：RespawnNotice 在回调线程到达，而 ApplyServerRespawn 要动 Animator/Transform。
            // 角色还没装配就**留着通知**、下帧再试：此刻消费掉它、又清掉等待/兜底，
            // 会留下"躺在死亡画面且再无出口"的死局（该会话不会再有第二次 OnServerDeath）。
            if (_hasServerRespawn && _localEntry != null)
            {
                _hasServerRespawn = false;
                ApplyServerRespawn(_serverRespawnHp);
            }

            // —— 阵亡后的等待与兜底（N5，主线程计时）——
            if (_awaitingServerRespawn)
            {
                if (_respawnWaitElapsed < 0f)
                {
                    // 提示在这里上屏（不在死亡回调里）：ShowStatus 读 Time，而那条回调的线程
                    // 取决于传输实现（见 HandleServerRespawn 的实测记录），不赌。
                    // 提示语带 ASCII 锚点：WebGL 出包里 IMGUI 的中文字形会被整段丢掉（T11/T8 已记录），
                    // 纯中文提示在浏览器里等于看不见——`dead: HP 0` / `server respawn` 保证可读。
                    ShowStatus("你已阵亡（dead: HP 0）——等待服务端复活（server respawn）",
                        ServerRespawnFallbackSeconds);
                    _respawnWaitElapsed = 0f;
                }

                _respawnWaitElapsed += Time.unscaledDeltaTime;
                if (_respawnWaitElapsed >= ServerRespawnFallbackSeconds)
                {
                    // 服务端 8 秒复活 + 7 秒余量都等不到通知 ⇒ 服务器出了别的问题
                    //（连接还在，但没人复活你）。复用 T12 的出口：提示 3 秒后回选人界面重登。
                    _awaitingServerRespawn = false;
                    _pendingReturnToMenu = true;
                    Debug.LogWarning("[NetHost] " + (int)ServerRespawnFallbackSeconds
                        + " 秒内未能应用服务端复活——返回选人界面重登");
                    ShowStatus("服务器未在 " + (int)ServerRespawnFallbackSeconds
                        + " 秒内复活你（no respawn notice）——返回选人界面重登（relogin = respawn）");
                    return;
                }
            }

            if (_pendingReturnToMenu && Time.unscaledTime >= _statusMessageUntil)
            {
                _pendingReturnToMenu = false;
                Disconnect();                                   // 清理 adapter / 战斗桥 / 装配
                SceneManager.LoadScene(MainMenuSceneName);
                ReopenCharacterSelectPanel();
                return;
            }

            if (_manager == null) return;

            // 每帧驱动：时钟采样/快照封送/纠偏响应/上报节流/插值渲染（manager 内部编排）
            _manager.Update(Time.deltaTime);

            // 进世界后装配本地角色（角色可能 Addressable 异步生成——就绪前每帧重试）
            if (_inWorld && !_attachFailed)
                TryAttachLocalCharacter();

            _combatBridge?.Update();
        }

        /// <summary>
        /// 发起登录（幂等）。连接参数**每次调用时**从 PlayerPrefs 读取——
        /// 不能只在 Start() 固化：直接跑 DemoDay 时选人确认发生在 Start() 之后
        /// （见 OnPlayerSpawned 的补连接逻辑），固化会读到旧地址（D-P6-17）。
        /// 显式传入的参数优先，供测试与代码路径覆盖。
        /// Task 13：端点决议在 `NetworkModeService.ResolveConnectEndpoint`（纯静态、有单测）——
        /// **URL 参数 `?host=` 不参与**：它的作用止于预填把地址填进选人面板的那个框，
        /// 连接拨的是框里的值。别在这里加"URL 优先"：那会让操作者手改的地址被链接里的值静默压掉，
        /// 而屏幕上（以及他记忆里）显示的是另一个。
        /// </summary>
        public void Connect(string? username = null, string? host = null, int? port = null)
        {
            if (_connectRequested || _remoteEntities == null) return;
            _connectRequested = true;

            var (resolvedHost, resolvedPort) = NetworkModeService.ResolveConnectEndpoint(
                ServerAddressQuery.CurrentPageUrl(), host, port);
            string resolvedUser = username ?? NetworkModeService.LoadUsername();

            _resolvedHost = resolvedHost;
            _resolvedPort = resolvedPort;

#if UNITY_WEBGL
            // WebGL 无 System.Net.Sockets —— 走浏览器 WebSocket
            _adapter = new WebSocketNet.WebSocketClientAdapter(
                new WebSocketNet.WebGLWebSocketChannel());
#else
            // D-W11：PC 也走 WebSocket（DotNetWebSocketChannel）。
            // 应用层 4 个发送点全部使用默认的 MessageFlags.Reliable，KCP 的不可靠
            // 通道从未被调用；保留它作为 PC 默认，只会让"自测路径"（PC=KCP）与
            // "演示路径"（浏览器=WS）分叉——那正是最不该分叉的两条路。
            _adapter = new WebSocketNet.WebSocketClientAdapter(
                new WebSocketNet.DotNetWebSocketChannel());
#endif
            _manager = new ClientNetworkManager(_adapter, _remoteEntities, resolvedUser);
            _manager.OnEnterWorld += HandleEnterWorld;
            _manager.OnDisconnected += HandleDisconnected;
            // 连接失败/登录被拒必须留日志——否则"无异常"会被误读为"已连上"（验收假阳性）
            _manager.OnError += HandleError;
            // N5：服务端复活——被复活者本人唯一的"你活了"信号（本地玩家不在自己的 AOI 快照里）
            _manager.OnServerRespawn += HandleServerRespawn;
            _manager.ConnectAndLogin(resolvedHost, resolvedPort);
        }

        /// <summary>
        /// 连接失败/登录被拒——评审看不到 Console，必须显示到屏幕上。
        /// 文案带上**本次实际尝试的端点**（reason 是管理器内部串，不含端点）；
        /// 同断线一样给出返回路径：玩家此刻停在世界场景，提示里指的选人界面否则不可达。
        /// </summary>
        private void HandleError(string reason)
        {
            Debug.LogError("[NetHost] error: " + reason);

            // I2：先把返回路径立起来，再上屏。
            // 本回调经 DirectMessageExecutor 在**网络驱动线程**触发（KcpNetClientAdapter.cs:55），
            // 而 ShowStatus 读 Time.unscaledTime——一旦它触发 Unity 的线程检查抛异常，
            // 后面这行就永远执行不到，玩家停在 DemoDay 且没有任何出口（正是 §6/D-P6-24 要消灭的"卡住"）。
            _pendingReturnToMenu = true;

            ShowStatus("连接失败：" + _resolvedHost + ":" + _resolvedPort
                + "（" + reason + "，可在选人界面修改服务器地址）");
        }

        /// <summary>
        /// P6 3.3：断线处理——评审看不到 Console，必须有屏幕可见出口。
        /// 先停留 3 秒显示原因（否则立刻跳场景玩家看不到任何反馈），再回选人界面。
        /// 不做自动重连（backlog）。
        /// </summary>
        private void HandleDisconnected(string reason)
        {
            Debug.LogWarning("[NetHost] 断线: " + reason);
            OnDisconnected?.Invoke(reason);
            ShowStatus("与服务器断开：" + reason + "，即将返回主界面");
            _pendingReturnToMenu = true;
        }

        /// <summary>连接失败 / 登录被拒——同样必须有屏幕出口（提示窗 3 秒）</summary>
        private void ShowStatus(string message) => ShowStatus(message, 3f);

        /// <summary>
        /// 带时长的提示窗。阵亡等待要给到兜底那一刻（N5）——3 秒的窗口会在等待期间熄灭，
        /// 屏幕上就没有任何"为什么动不了"的解释了。
        /// </summary>
        private void ShowStatus(string message, float durationSeconds)
        {
            _statusMessage = message;
            _statusMessageUntil = Time.unscaledTime + durationSeconds;
        }

        /// <summary>
        /// N5：服务端判定本地玩家阵亡（权威余血归零）——**等待服务端复活，不再自己回选人界面**。
        ///
        /// T12 乙-3 在这里 3 秒后把人送回选人界面：那时服务端没有复活（P8 范围，D-P4-8），
        /// 重登（重建 PlayerState = 满血）是唯一的出路。N2 之后服务端 8 秒自己复活并给**本人**
        /// 发 `RespawnNotice`——那 3 秒反过来成了障碍：通知到达前客户端已经离开世界，
        /// 服务端复活永远看不见。现在改为等通知，兜底 15 秒见 Update
        /// （连接断了仍是断线那条路：`HandleDisconnected` 立刻给出口）。
        ///
        /// **本回调的线程取决于传输**（CombatNetworkBridge 的 DamageResult 消费链走
        /// `INetworkClient.MessageReceived`：WS 实测在主线程，KCP 在驱动线程——见 HandleServerRespawn），
        /// 所以这里只置位、不赌实现——Time/Unity API 一概留到 Update（提示的延迟上屏见那里）。
        /// </summary>
        private void HandleLocalPlayerDeath()
        {
            if (_pendingReturnToMenu) return;             // 已在返回途中
            _awaitingServerRespawn = true;
            _respawnWaitElapsed = -1f;                    // <0：提示尚未上屏
            Debug.LogWarning("[NetHost] 本地玩家阵亡（服务端权威 HP 归零）——等待服务端复活（RespawnNotice）");
        }

        /// <summary>
        /// 服务端复活通知——只记账，真正动 Unity API（Animator/Transform 的 `ApplyServerRespawn`）
        /// 的是 Update 里的封送分支。
        ///
        /// 为什么要封送：`INetworkClient.MessageReceived` 的契约是"回调线程"，**不保证主线程**——
        /// 实测（2026-10-12，编辑器 + 本地服务器，`DotNetWebSocketChannel` 走 WebSocket）：
        /// `MessageReceived` 回调在主线程（thread=1）；但 KCP 实现
        /// （`KcpNetClientAdapter` + `DirectMessageExecutor`，PlayMode 测试用的那条）是驱动线程。
        /// 所以这里不赌传输实现，一律记账、由 Update 应用。
        /// </summary>
        private void HandleServerRespawn(float hp)
        {
            _serverRespawnHp = hp;
            _hasServerRespawn = true;
        }

        /// <summary>
        /// 应用服务端复活（**只在主线程**调用——Update 的封送分支；调用方已保证 `_localEntry` 非空：
        /// 装不上就压根不进来，见 Update 的 `_hasServerRespawn &amp;&amp; _localEntry != null`）。
        /// 血量直接采信服务端的（D-N4/D-5：客户端不自行判活）。
        /// </summary>
        private void ApplyServerRespawn(float hp)
        {
            _awaitingServerRespawn = false;
            // 通知到了 ⇒ 兜底撤回、死亡提示收走。这里敢直接清 _pendingReturnToMenu：
            // 连接失败/登录被拒/断线那三个入口之后都收不到任何消息，它只可能是死亡兜底设的。
            _pendingReturnToMenu = false;
            _statusMessage = null;
            Debug.Log("[NetHost] 服务端复活：HP=" + hp);
            _localEntry.ApplyServerRespawn(hp);
        }

        /// <summary>
        /// T12 Step 4（T8 §6.2）：回菜单后重新打开选人面板。面板在"确认"时被 CloseAsync 关掉，
        /// 而 UIManager 是 DontDestroyOnLoad 的——重进 CharacterSelect 场景不会重建它、
        /// LoadBuiltInPanels 也不再跑 ⇒ 此前回到的是**没有 UI 的选人场景**（只能刷新页面）。
        /// </summary>
        private static void ReopenCharacterSelectPanel()
        {
            var ui = UIManager.Instance;
            if (ui == null) return;
            ui.OpenAsync<CharacterSelectPanel>().Forget();
        }

        public void Disconnect()
        {
            ResetAttachment();
            _manager?.Dispose();
            _manager = null;
            _adapter = null;
            _connectRequested = false;
            _inWorld = false;
            _entryLookedUp = false;
            // 离开世界：阵亡等待随之作废（否则兜底计时会跨会话继续跑，15 秒后凭空把
            // 新会话的玩家送回选人界面）
            _awaitingServerRespawn = false;
            _hasServerRespawn = false;
        }

        private void OnGUI()
        {
            // I3：屏幕中部提示（连接失败/断线的唯一可见出口，验收 ⑥/⑨ 依赖它）
            // **必须**放在 `_showDebugOverlay` 守卫之前——它此前被关在调试开关之内，
            // 关掉调试位会连带关掉全部屏幕出口（而 §四/指南 承诺的是"屏幕中部提示"）。
            if (_statusMessage != null && Time.unscaledTime < _statusMessageUntil)
            {
                var style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 16,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };
                GUI.Box(new Rect(0f, Screen.height * 0.35f, Screen.width, 64f), _statusMessage, style);
            }

            // 屏幕左下角连接状态浮层（调试用——Inspector `_showDebugOverlay` 可关）。
            // **分两行**：竖屏手机约 390px 宽，旧的单行 620px 会把串尾（任务 10 的实测数据
            // 全部从这一行读：Full/Proxy/预算/质量档/FPS）整段截在屏外——档位不上屏，
            // 这次跑的是档 2 还是 Ultra 就无法自证。读数行全 ASCII 且短，保证永远可见
            //（WebGL 出包里 IMGUI 的中文字形会被整段丢掉，读数不能依赖中文）。
            if (!_showDebugOverlay) return;

            var mode = NetworkModeService.Load() == NetworkMode.Online ? "联网" : "单机";
            var state = _manager != null ? _manager.CurrentState.ToString() : "未连接";
            long playerId = _manager != null ? _manager.LocalPlayerId : 0L;
            int remoteCount = _remoteEntities != null ? _remoteEntities.Count : 0;
            float fps = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f;
            float overlayWidth = Mathf.Min(620f, Screen.width - 20f);
            GUI.Box(new Rect(10f, Screen.height - 60f, overlayWidth, 26f),
                $"[Net] 模式={mode}  状态={state}  玩家id={playerId}  远程实体={remoteCount}");
            GUI.Box(new Rect(10f, Screen.height - 34f, overlayWidth, 26f),
                FormatTierReadout(_remoteEntities, QualitySettings.GetQualityLevel(), fps));
        }

        /// <summary>
        /// 调试浮层第二行：分档读数 + 质量档 + FPS。**独立成纯函数**供单测钉住——
        /// 任务 10（手机实测）的全部数据都从这一行读，而竖屏手机约 390px 宽：读数缺一项、
        /// 或长到被截掉，实测就无从自证（当初单行 620px 正是把这一整段截在屏外）。
        /// `B` = 每帧 Full 预算（`TierPolicy.MaxFull`）、`Q` = 当前质量档
        /// （`QualitySettings.GetQualityLevel()`——实测记录必须写明档位）。
        /// 全 ASCII：WebGL 出包里 IMGUI 的中文字形会被整段丢掉（与阵亡提示同因）。
        /// registry 为 null（未联网/未接线）时只出质量档与 FPS，不炸。
        /// </summary>
        internal static string FormatTierReadout(RemoteEntityRegistry? registry, int qualityLevel, float fps)
        {
            string tiers = registry != null
                ? $"Full={registry.FullCount} Proxy={registry.ProxyCount} B={registry.TierPolicy.MaxFull}  "
                : string.Empty;
            return tiers + $"Q={qualityLevel}  FPS={fps:F0}";
        }

        private void HandleEnterWorld()
        {
            _inWorld = true;
            OnEnterWorld?.Invoke();
        }

        /// <summary>
        /// 装配本地角色移动上报与纠偏。entry.CoreController 就绪前每帧重试（上限 10s，
        /// 超时降级为纯观察者——仅渲染远程实体，本地角色不上报）。
        /// </summary>
        private void TryAttachLocalCharacter()
        {
            if (_netPolicy != null) return; // 已装配

            if (!_entryLookedUp)
            {
                _entryLookedUp = true;
                if (_localEntry == null)
                    _localEntry = FindLocalEntry();
                if (_localEntry == null)
                {
                    _attachFailed = true;
                    Debug.LogWarning("[NetHost] 场景中无 Sys3CEntry——以观察者模式运行（仅渲染远程实体）");
                    return;
                }
            }

            if (_localEntry.CoreController == null)
            {
                // 角色组件尚未初始化（Start 未跑 / 异步生成中）——下帧重试
                _attachRetryTimer += Time.deltaTime;
                if (_attachRetryTimer > 10f)
                {
                    _attachFailed = true;
                    Debug.LogWarning("[NetHost] 本地角色 10s 未就绪——降级为观察者模式");
                }
                return;
            }

            var controller = _localEntry.CoreController;
            _netPolicy = new NetMovementPolicy(controller);
            _localEntry.MovementPolicy = _netPolicy; // 接管本地物理 + 平滑拉回（Start 的 null 守卫兼容预置）

            // 上报数据源：位置/旋转读 CharacterData（每帧自 transform 同步）；
            // 速度 = 水平真实速度（_controller.velocity——超速钳制链路依赖真实值，勿用 0-1 动画参数）；
            // 防御状态 = 3C 的 `IsDefending` 真值源（服务端格挡减伤据此裁决——本地盾耐久不同步，
            // 服务端 PlayerState.ShieldDurability 是权威）
            _manager!.AttachMovementReporter(
                () =>
                {
                    var d = controller.Data;
                    return new Vec3(d.Position.x, d.Position.y, d.Position.z);
                },
                () => controller.Data.Rotation.eulerAngles.y,
                () =>
                {
                    var v = controller.Data.Velocity;
                    return Mathf.Sqrt(v.x * v.x + v.z * v.z);
                },
                defendStateProvider: () => controller.IsDefending);
            _manager.OnCorrectionNeeded += _netPolicy.ApplyServerCorrection;

            // P4 战斗桥：技能激活 → CastRequest；CastBroadcast/DamageResult → 表现/伤害挂现有事件链
            // （blocked = 服务端格挡裁决 → 本地走格挡受击表现，见 Sys3CEntry.ApplyServerDamage）
            _combatBridge = new CombatNetworkBridge(_adapter!, () => _manager!.LocalPlayerId, _remoteEntities!,
                (targetId, dmg, crit, dir, remainingHp, blocked) =>
                    _localEntry.ApplyServerDamage(dmg, dir, remainingHp, blocked));
            _adapter!.MessageReceived += _combatBridge.OnMessage;
            _localEntry.SkillCoordinator.OnSkillActivated += OnLocalSkillCast;
            _localEntry.SkillCoordinator.OnChargeReleased += OnLocalChargeReleased;
            // T12 乙-3：服务端判死 → 提示 + 回选人（重登即复活）
            _localEntry.OnServerDeath += HandleLocalPlayerDeath;
            // 施法广播除日志外还要**路由**到怪物排程：广播本身不含"这是哪只怪的哪一刀"之外的
            // 表现信息，registry 用发布者时间戳排成 renderAt 处的挥砍（本回调已在主线程——
            // CombatNetworkBridge.Update 消费，见该类线程模型）。
            _combatBridge.OnCastBroadcast += b =>
            {
                Debug.Log($"[NetHost] cast broadcast: caster={b.CasterPlayerId} skill={b.SkillId}");
                _remoteEntities!.ScheduleAttack(b.CasterPlayerId, b.ServerTimestamp);
            };

            // P5：联网模式下怪物由服务器驱动——禁用场景内全部本地 MonsterSpawner
            //（DemoDay 有 SlimeSpawner + TurtleShellSpawner 两个——单数 FindObjectOfType 会漏）
            var localSpawners = FindObjectsOfType<Hotfix.GameSystems.Monster.MonsterSpawner>();
            foreach (var spawner in localSpawners)
            {
                // P6 ⑥：必须真销毁，不能只 SetActive(false)——PhysicsRegistry 只查 IsAlive
                // 不查 activeInHierarchy，停用后怪物仍可被打中，且名牌残影
                spawner.DespawnAll();
                spawner.gameObject.SetActive(false);
                Debug.Log($"[NetHost] 本地 MonsterSpawner 已禁用：{spawner.name}（服务器驱动怪物）");
            }

            Debug.Log("[NetHost] 本地角色已接入移动上报（repoter + NetMovementPolicy）");
        }

        private void OnLocalSkillCast(SkillData skillData)
        {
            // 可蓄力技能（ChargedSkillData / MaxChargeTime>0 的投射物如 20003）激活时不发，
            // 由 OnChargeReleased 在释放时刻发；30001 带落点。策略见 SkillCastReporter
            Vector3 targetPosition = _localEntry.SkillCoordinator.CurrentSkill?.TargetPosition ?? Vector3.zero;
            SkillCastReporter.ReportActivation(_combatBridge, skillData, targetPosition, _localEntry.transform);
        }

        /// <summary>蓄力释放（松开/蓄满）→ 释放时刻的落点与进度随 CastRequest 上线</summary>
        private void OnLocalChargeReleased(SkillData skillData, float chargeProgress, Vector3 impactPoint)
            => SkillCastReporter.ReportChargeRelease(_combatBridge, skillData, chargeProgress, impactPoint);

        /// <summary>
        /// 摘除战斗桥订阅（adapter 消息 + 本地技能事件）。Disconnect 与 OnDestroy 共用——
        /// 宿主被销毁而本地 Sys3CEntry 存活时，避免桥对已销毁宿主留下悬空委托。
        /// </summary>
        private void DetachCombatBridge()
        {
            // T12 乙-3：死亡回调也不能漏摘（它挂在本地角色上，宿主可能比角色活得久）——
            // 放在早退守卫**之前**，没有战斗桥时同样要解开
            if (_localEntry != null)
                _localEntry.OnServerDeath -= HandleLocalPlayerDeath;

            if (_combatBridge == null || _adapter == null) return;
            _adapter.MessageReceived -= _combatBridge.OnMessage;
            if (_localEntry != null)
            {
                _localEntry.SkillCoordinator.OnSkillActivated -= OnLocalSkillCast;
                _localEntry.SkillCoordinator.OnChargeReleased -= OnLocalChargeReleased;
            }
            _combatBridge = null;
        }

        private void OnDestroy()
        {
            if (_manager != null)
            {
                _manager.OnEnterWorld -= HandleEnterWorld;
                _manager.OnServerRespawn -= HandleServerRespawn;
                _manager.Dispose();
                _manager = null;
            }
            DetachCombatBridge();
        }
    }
}
