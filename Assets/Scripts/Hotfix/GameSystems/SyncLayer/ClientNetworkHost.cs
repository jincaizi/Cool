using System;
using System.Collections.Generic;
using System.Threading;
using Cool.Protocol;
using Cool.SyncCore;
using Cysharp.Threading.Tasks;
using Hotfix.GameSystems.Bag.Data;
using Hotfix.GameSystems.Bag.Runtime;
using Hotfix.GameSystems.Bag.UI;
using Hotfix.GameSystems.Nameplate;
using Hotfix.GameSystems.UI;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.Sys3C.Network;
using Hotfix.GameSystems.VFX;
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
    /// 单机使用共享武器表发放本地背包并供技能读取装备，不建立连接。
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
            /// <summary>名牌显示名；留空 = registry 退回 "怪物 {templateId}"</summary>
            public string DisplayName;
        }

        [Header("怪物模型（templateId → prefab；留空则该 template 退化为球体占位）")]
        [SerializeField] private NpcPrefabEntry[] _npcPrefabs = Array.Empty<NpcPrefabEntry>();

        [Tooltip("Npc 落地采样的层掩码。默认 = Default 层（本项目地形层：LowpolyTerrain 在 layer 0）。" +
                 "注意：树/石头也在 Default —— 掩码只作粗筛，命中体还要校验是地形，否则怪会吸到树冠上")]
        [SerializeField] private LayerMask _npcGroundMask = 1;

        [Header("远程施法表现（技能表现目录；留空 = 不渲染远程施法动画/投射物）")]
        [Tooltip("Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset——与本地职业无关，" +
                 "按 SkillId 提供远程施法的动画触发器与 VFX")]
        [SerializeField] private SkillPresentationCatalog _skillCatalog;

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
        private RemoteCastPresenter? _remoteCastPresenter;
        /// <summary>权威状态投影 → 染色事件（远端经 registry.StatusesChanged，本人经 318 本地分支）</summary>
        private AuthoritativeStatusVfx? _statusVfx;
        /// <summary>最近一次本人 318 的状态列表——本地角色重建后对新 transform 重放（断开清空）</summary>
        private IList<StatusProjection>? _localStatuses;
        private WeaponInventoryBaseline? _pendingWeaponBaseline;
        private bool _bagEquipSubscribed;
        private WeaponBagController? _weaponBagController;
        private ClientEquipmentState? _offlineEquipment;
        private readonly Dictionary<long, WeaponDefinition> _offlineWeapons = new();
        private bool _useOfflineWeapons;
        private Sys3CEntry? _offlineWeaponOwner;

        private ClientEquipmentState? CurrentEquipment => _combatBridge?.Equipment ?? _offlineEquipment;
        private int _weaponBagRenderedRevision = -1;
        private NetworkTargetSelector? _networkTargetSelector;

        // 剑气回荡请求归属（连接代次/角色绑定代次/请求序号）
        private long _echoConnectionGeneration;
        private ulong _echoBindingId = 1;
        private ulong _nextEchoRequestId;
        private SwordQiEchoSkillData? _pendingEchoSkill;
        private ulong _pendingEchoRequestId;
        private long _pendingEchoConnectionGeneration;
        private float _pendingEchoDeadline;
        private bool _echoRequestPending;
        private ulong _lastAppliedEchoCastId;
        private uint _lastAppliedEchoStartTick;
        private ulong _lastAppliedEchoRequestId;
        private ulong _endedEchoCastId;
        private SwordQiEchoPresenter? _localEchoPresenter;

        private sealed class RemoteEchoInstance
        {
            public long CasterId;
            public SwordQiEchoPresenter Presenter = null!;
        }

        private readonly Dictionary<ulong, RemoteEchoInstance> _remoteEchoPresenters =
            new Dictionary<ulong, RemoteEchoInstance>();

        // 联网 HUD 读数（FPS + RTT）：HUD 面板缓存与 0.5 秒统计窗口
        private PlayerHudPanel? _hudPanel;
        private float _fpsWindowSeconds;
        private int _fpsFrames;
        private int _displayedFps;
        private float _netStatsPushTimer;
        private bool _netStatsVisible;

        /// <summary>远程实体渲染根（Awake 建的 `RemotePlayers`）——本地角色查找要排除它整棵子树，
        /// 见 <see cref="FindLocalEntry"/></summary>
        private Transform? _remoteRoot;

        private string? _statusMessage;        // 屏幕浮层上的错误/断线文本
        private float _statusMessageUntil;     // 显示截止时刻（Time.unscaledTime）
        private bool _pendingReturnToMenu;

        // N5 服务端复活：通知在回调线程到达，Unity API 只能在主线程应用（见 HandleServerRespawn）
        private RespawnNotice? _pendingServerRespawn;

        // N5 阵亡等待：回调线程只置位，计时与提示留主线程 Update
        private bool _awaitingServerRespawn;
        private float _respawnWaitElapsed = -1f;   // <0 = 提示还没上屏（要读 Time，只能在主线程）

        // 任务 10：本地阵亡 → selector 清锁/暂停切换。死亡回调的线程取决于传输
        // （见 HandleLocalPlayerDeath 注释），回调只置位，
        // UI/Transform 操作留到主线程 Update 消费。
        private bool _pendingTargetOwnerDeath;

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

            // 换装会话跟随连接（角色重建保留，Disconnect 重置）。委托在调用时读取当前装配，
            // 因此可以在本地角色/战斗桥创建前构造。
            _weaponBagController = new WeaponBagController(
                isDead: () => _localEntry == null || _localEntry.IsDead,
                isSkillActive: () => _localEntry != null && _localEntry.SkillCoordinator != null
                    && _localEntry.SkillCoordinator.IsSkillActive,
                requestEquip: RequestWeapon,
                applyAppearance: ApplySwordAppearance);

            // Inspector 的数组 → 查询表（registry 只吃表；空目录传 null = 球体降级，与未接线同一条路）
            var catalog = new Dictionary<int, GameObject>();
            foreach (var entry in _npcPrefabs)
                if (entry != null && entry.Prefab != null)
                    catalog[entry.TemplateId] = entry.Prefab;

            // 怪物显示名：只收配置了名字的条目；空表 = registry 退回 "怪物 {templateId}"
            var npcDisplayNames = new Dictionary<int, string>();
            foreach (var entry in _npcPrefabs)
                if (entry != null && !string.IsNullOrEmpty(entry.DisplayName))
                    npcDisplayNames[entry.TemplateId] = entry.DisplayName;

            _remoteEntities = new RemoteEntityRegistry(root.transform,
                _appearances.Length > 0
                    ? _appearances
                    : new[] { new AppearanceEntry { Id = 0, Full = _playerAvatarPrefab } },
                catalog.Count > 0 ? catalog : null,
                npcDisplayNames.Count > 0 ? npcDisplayNames : null,
                labelSink: new NameplateLabelSink(),
                playerNameResolver: ResolvePlayerName);

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

            // 活动施法投影统一走 registry 事件（基线快照的 Combat.ActiveCast 与增量更新同路径）。
            // 订阅跟随 registry 生命周期：DetachCombatBridge 不退订——角色重建/重连后同一 registry
            // 还要继续用，退订会让新 presenter 永远收不到基线施法；handler 对 presenter 为 null 安全。
            _remoteEntities.ActiveCastApplied += HandleActiveCastApplied;

            // 权威状态表现：投影成功合并且状态列表变化才发（远端）。本人投影在 318
            // 本地分支直接消费（见 HandleCombatStateUpdate）。
            _statusVfx = new AuthoritativeStatusVfx();
            _remoteEntities.StatusesChanged += HandleRemoteStatusesChanged;
        }

        /// <summary>远端状态列表变化 → diff 后按组发应用/移除事件（AuthoritativeStatusVfx）。</summary>
        private void HandleRemoteStatusesChanged(long entityId, GameObject target,
            IList<StatusProjection> statuses)
        {
            if (target == null) return;
            _statusVfx?.Apply(entityId, target.transform, statuses);
        }

        /// <summary>
        /// 本地角色重建/首次装配后按缓存重放本人状态（新身体需要重新染色）：
        /// AuthoritativeStatusVfx 检测到 target 变化会先移除旧身体、再应用新身体。
        /// </summary>
        private void ReplayLocalStatuses()
        {
            if (_localStatuses == null || _localEntry == null || _statusVfx == null) return;
            long entityId = _manager?.LocalPlayerId ?? 0;
            if (entityId == 0) return;
            _statusVfx.Apply(entityId, _localEntry.transform, _localStatuses);
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
            _useOfflineWeapons = NetworkModeService.Load() == NetworkMode.SinglePlayer;
            if (!_useOfflineWeapons)
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

            bool offline = NetworkModeService.Load() == NetworkMode.SinglePlayer;
            if ((offline && _connectRequested) || (!offline && _offlineEquipment != null))
                Disconnect();
            else
                ResetAttachment(keepConnection: true);
            _useOfflineWeapons = offline;
            _localEntry = entry;
            _entryLookedUp = true; // 已定位——Update 的重试逻辑直接装配新角色
            ReplayLocalStatuses(); // 新身体重放权威状态（旧身体随之清染色）
            _echoBindingId++;      // 角色重绑：旧绑定请求/回包失效

            // DemoDay 直跑路径：模式在选人面板确认时才落盘（宿主 Start 读到的仍是旧值/单机）——
            // 玩家（重）生成即确认动作的副作用，此处补连接（正常流程 Start 已连，_connectRequested 守卫）
            if (!_connectRequested && !_useOfflineWeapons)
            {
                Debug.Log($"[NetHost] 联网模式（选人确认）——连接 {NetworkModeService.LoadHost()}:{NetworkModeService.LoadPort()}");
                Connect();
            }
        }

        /// <summary>解绑角色；同一连接重建时保留消息队列、请求序号及远端表现。</summary>
        private void ResetAttachment(bool keepConnection = false)
        {
            if (_manager != null)
            {
                _manager.DetachMovementReporter();
                if (_netPolicy != null)
                    _manager.OnCorrectionNeeded -= _netPolicy.ApplyServerCorrection;
            }
            DetachCombatBridge(keepConnection);
            DetachTargetSelector();
            _pendingTargetOwnerDeath = false;   // 旧装配的死亡通知不得跨会话作用到新 selector
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
            if (_localEntry != null && Interlocked.Exchange(ref _pendingServerRespawn, null) is RespawnNotice notice)
                ApplyServerRespawn(notice);

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

            if (_manager == null)
            {
                if (_useOfflineWeapons)
                {
                    TryAttachOfflineWeapons();
                    TickWeaponBag();
                }
                return;
            }

            // 每帧驱动：时钟采样/快照封送/纠偏响应/上报节流/插值渲染（manager 内部编排）
            _manager.Update(Time.deltaTime);

            UpdateNetStats(Time.unscaledDeltaTime);

            // 进世界后装配本地角色（角色可能 Addressable 异步生成——就绪前每帧重试）
            if (_inWorld && !_attachFailed)
                TryAttachLocalCharacter();

            if (_netPolicy != null) _combatBridge?.Update();
            TickWeaponBag();
            TickEchoRequest();

            // 死亡回调可能来自网络线程：封送到主线程后才动 selector（UI/选中环）
            if (_pendingTargetOwnerDeath)
            {
                _pendingTargetOwnerDeath = false;
                if (_networkTargetSelector != null) _networkTargetSelector.SetOwnerAlive(false);
            }

            // 目标锁定：T/按钮切换 + 每帧有效性（manager.Update 之后——用本帧渲染位姿判定）
            if (_networkTargetSelector != null) _networkTargetSelector.Tick();
            // 投射物停点/爆炸已在 OnDamageResult 里对齐；这里只清理已回收的视觉
            _remoteCastPresenter?.Update(Time.deltaTime);
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
            _echoConnectionGeneration++;
            _nextEchoRequestId = 0;
            ClearEchoRequest();

            var (resolvedHost, resolvedPort) = NetworkModeService.ResolveConnectEndpoint(
                ServerAddressQuery.CurrentPageUrl(), host, port);
            string resolvedUser = username ?? NetworkModeService.LoadUsername();

            _resolvedHost = resolvedHost;
            _resolvedPort = resolvedPort;

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL 无 System.Net.Sockets —— 走浏览器 WebSocket
            // Editor 即使在 WebGL target 下也不走浏览器 jslib（__Internal 在 Editor 不存在），
            // 否则 Play Mode 会 EntryPointNotFoundException；见下方 #else。
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
            // 名字名册（主线程落库）→ 已存在的远程视图/名牌；晚到的视图由 resolver 自查名册
            _manager.Names.OnNameChanged += HandlePlayerNameChanged;
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
            // 本地死亡：清锁并暂停切换（spec §8.6）。**只置位**——本回调可能在网络线程，
            // selector 要动 UI/Transform，统一由 Update（主线程）消费。
            _pendingTargetOwnerDeath = true;
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
        private void HandleServerRespawn(RespawnNotice notice)
        {
            Interlocked.Exchange(ref _pendingServerRespawn, notice);
        }

        /// <summary>
        /// 应用服务端复活（**只在主线程**调用——Update 的封送分支；调用方已保证 `_localEntry` 非空：
        /// Update 在本地角色就绪后才消费通知）。
        /// 血量直接采信服务端的（D-N4/D-5：客户端不自行判活）。
        /// </summary>
        private void ApplyServerRespawn(RespawnNotice notice)
        {
            _awaitingServerRespawn = false;
            // 复活：恢复目标切换（锁已在本死亡时清掉，玩家可重新 T 选目标）
            if (_networkTargetSelector != null) _networkTargetSelector.SetOwnerAlive(true);
            // 通知到了 ⇒ 兜底撤回、死亡提示收走。这里敢直接清 _pendingReturnToMenu：
            // 连接失败/登录被拒/断线那三个入口之后都收不到任何消息，它只可能是死亡兜底设的。
            _pendingReturnToMenu = false;
            _statusMessage = null;
            _manager?.ResetMovementForRespawn(notice.HasSpawnPosition ? (uint?)notice.Tick : null);
            _netPolicy?.ClearServerCorrection();
            Vector3? spawn = notice.HasSpawnPosition
                ? new Vector3(notice.SpawnX, notice.SpawnY, notice.SpawnZ)
                : (Vector3?)null;
            Debug.Log("[NetHost] 服务端复活：HP=" + notice.Hp);
            _combatBridge?.Equipment.SetLifeId(notice.LifeId);
            _localEntry.ApplyServerRespawn(notice.Hp, spawn);
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
            HideNetStats();
            ResetAttachment();
            if (_manager != null)
            {
                _manager.Names.OnNameChanged -= HandlePlayerNameChanged;
                _manager.Dispose();
                _manager = null;
            }
            _adapter = null;
            _connectRequested = false;
            _inWorld = false;
            _entryLookedUp = false;
            _pendingWeaponBaseline = null;   // 旧会话基线不得泄漏到下一次登录
            _echoConnectionGeneration++;
            ClearEchoRequest();
            // 离开世界：阵亡等待随之作废（否则兜底计时会跨会话继续跑，15 秒后凭空把
            // 新会话的玩家送回选人界面）
            _awaitingServerRespawn = false;
            Interlocked.Exchange(ref _pendingServerRespawn, null);
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

        /// <summary>联网 HUD 读数的显示条件。抽成纯函数供单测钉住回菜单/单机/未进世界三个早退。</summary>
        internal static bool ShouldShowNetStats(bool hasManager, bool inWorld, bool pendingReturnToMenu)
            => hasManager && inWorld && !pendingReturnToMenu;

        /// <summary>
        /// 联网 HUD 读数：0.5 秒窗口算 FPS，每 0.5 秒推一次文本（TMP 每帧赋值会触发网格重建）。
        /// 未进世界/单机/回菜单途中一律隐藏。HUD 面板懒获取并缓存，UIManager 未就绪时静默跳过。
        /// </summary>
        private void UpdateNetStats(float unscaledDeltaSeconds)
        {
            if (!ShouldShowNetStats(_manager != null, _inWorld, _pendingReturnToMenu))
            {
                HideNetStats();
                return;
            }

            _fpsWindowSeconds += unscaledDeltaSeconds;
            _fpsFrames++;
            _netStatsPushTimer += unscaledDeltaSeconds;

            if (_fpsWindowSeconds >= 0.5f)
            {
                _displayedFps = Mathf.RoundToInt(_fpsFrames / _fpsWindowSeconds);
                _fpsWindowSeconds = 0f;
                _fpsFrames = 0;
            }

            if (_netStatsPushTimer < 0.5f) return;
            _netStatsPushTimer = 0f;

            var hud = EnsureHudPanel();
            if (hud == null) return;

            hud.SetNetStats(FormatNetStats(_displayedFps, _manager.RttMs));
            _netStatsVisible = true;
        }

        private PlayerHudPanel? EnsureHudPanel()
        {
            if (_hudPanel != null) return _hudPanel;

            var ui = UIManager.Instance;
            if (ui == null) return null;

            _hudPanel = ui.GetPanel<PlayerHudPanel>();
            return _hudPanel;
        }

        /// <summary>隐藏读数并清空统计窗口（幂等；单机/断线/销毁共用）。</summary>
        private void HideNetStats()
        {
            _fpsWindowSeconds = 0f;
            _fpsFrames = 0;
            _netStatsPushTimer = 0f;
            _displayedFps = 0;

            if (!_netStatsVisible) return;
            _netStatsVisible = false;
            _hudPanel?.SetNetStats(null);
        }

        /// <summary>HUD 读数文本。抽成纯函数供单测钉住格式。</summary>
        internal static string FormatNetStats(int fps, int? rttMs)
            => "FPS " + fps + "  延迟 " + (rttMs.HasValue ? rttMs.Value + "ms" : "--");

        private void HandleEnterWorld()
        {
            _inWorld = true;

            // 属性剑气登录基线：权威武器背包 + 默认装备。桥可能尚未装配（角色异步生成），
            // 基线留到 TryAttachLocalCharacter 再应用装备版本。
            var baseline = _manager?.WeaponInventory;
            _pendingWeaponBaseline = baseline;
            if (baseline != null)
                BagManager.Instance.ApplyServerWeapons(baseline.Weapons);

            OnEnterWorld?.Invoke();
        }

        /// <summary>名册变化（主线程，`ClientNetworkManager.Update` 落库后触发）</summary>
        private void HandlePlayerNameChanged(long playerId, string displayName)
        {
            _remoteEntities?.UpdatePlayerName(playerId, displayName);
            // 名字晚到：正好锁定着它 → 重绑面板（TargetPanel.Bind 先 Clear，幂等）
            if (_networkTargetSelector != null && _networkTargetSelector.LockedTargetId == playerId)
                _networkTargetSelector.RefreshTargetPanel();
        }

        /// <summary>建视图时查名册——名册可能先于快照到达（登录名册就是这种顺序）</summary>
        private string? ResolvePlayerName(long playerId)
        {
            if (_manager != null && _manager.Names.TryGetName(playerId, out var name))
                return name;
            return null;
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
                ReplayLocalStatuses(); // 首次定位到本地角色（状态可能先于角色到达）
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
            // 防御状态 = 3C 的 `IsDefending` 真值源（服务端格挡减伤据此裁决——
            // 盾资源只由客户端精力控制，服务端不持有盾耐久）
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
                defendStateProvider: () => controller.IsDefending,
                stateProvider: () =>
                {
                    var d = controller.Data;
                    ulong castId = 0;
                    if (_localEntry.IsDashing && _combatBridge != null)
                    {
                        _combatBridge.TryGetAcceptedCastId(_localEntry.CurrentSkillId,
                            _combatBridge.Equipment.LifeId, out castId);
                    }
                    return new MovementReportState
                    {
                        Mode = d.IsSprint ? LocomotionMode.Sprint : LocomotionMode.Walk,
                        JumpSeq = controller.JumpSeq,
                        Airborne = controller.IsAirborne,
                        Grounded = controller.IsGrounded,
                        VerticalVelocity = controller.Data.VerticalVelocity,
                        DisplacementSkillId = _localEntry.IsDashing ? _localEntry.CurrentSkillId : 0,
                        CastId = castId,
                        LifeId = _combatBridge?.Equipment.LifeId ?? 0u,
                        ControlVersion = _localEntry.ControlVersion
                    };
                });
            _manager.OnCorrectionNeeded += _netPolicy.ApplyServerCorrection;

            if (_combatBridge == null) AttachConnectionCombat();
            ApplyEquipmentState();
            _localEntry.SkillCoordinator.AttackSpeedProvider = () => _combatBridge?.Equipment.AttackSpeedMultiplier ?? 1f;
            _localEntry.SkillCoordinator.IsWeaponChangePending = () => _weaponBagController?.HasPending == true;
            _localEntry.SkillCoordinator.OnSkillActivated += OnLocalSkillCast;
            _localEntry.SkillCoordinator.OnChargeReleased += OnLocalChargeReleased;
            _localEntry.SkillCoordinator.OnSkillCancelled += OnLocalSkillCancelled;
            bool echoEnabled = (_manager.EnabledFeatures & ProtocolFeatures.SwordQiEcho) != 0;
            if (echoEnabled)
                _localEntry.SkillCoordinator.OnEchoRequested += HandleEchoRequested;
            var localInput = _localEntry.GetComponent<InputManager>();
            if (localInput != null) localInput.SetSwordQiEchoEnabled(echoEnabled);
            _localEntry.OnServerDeath += HandleLocalPlayerDeath;

            // 启用属性剑气后关闭本地剑气生成：视觉统一由服务端发射描述创建。
            NetworkSwordQiGate.SuppressLocalSpawn =
                (_manager.EnabledFeatures & ProtocolFeatures.ElementalSwordQiV1) != 0;
            if (NetworkSwordQiGate.SuppressLocalSpawn)
            {
                // 元素会话服务端按共享表给 720 HP；这里先对齐 HUD/本地上限，
                // 权威余血仍由 DamageResult/RespawnNotice 驱动。
                _localEntry.SetMaxHp(ElementalCatalogDefaults.Create().Pvp.PlayerMaxHp, fill: true);
            }

            // 任务 10：目标锁定（T/移动端按钮）。只在联网装配时创建——单机零影响。
            // UI 绑定照 CharacterAttackHandler.SelectTarget；UIManager 未就绪时回调内 ?. 静默跳过。
            // 组件可能随本地角色复用时先 GetComponent，避免重装配在同一 GameObject 上叠第二个 selector。
            var selector = _localEntry.GetComponent<NetworkTargetSelector>();
            if (selector == null)
                selector = _localEntry.gameObject.AddComponent<NetworkTargetSelector>();
            selector.Initialize(
                _remoteEntities!,
                _localEntry.transform,
                _localEntry.GetComponent<InputManager>(),
                _localEntry.GetComponentInChildren<SelectionRing>(true),
                target =>
                {
                    var ui = UIManager.Instance;
                    if (ui == null) return;
                    var panel = ui.GetPanel<TargetPanel>();
                    if (panel == null) return;   // 面板未注册（测试宿主/别的场景）：静默跳过
                    panel.Bind(target);
                    ui.ShowAlwaysAsync<TargetPanel>().Forget();
                },
                () =>
                {
                    var ui = UIManager.Instance;
                    if (ui == null) return;
                    var panel = ui.GetPanel<TargetPanel>();
                    if (panel == null) return;
                    panel.Clear();
                    ui.HideAlwaysAsync<TargetPanel>().Forget();
                },
                () => _manager?.LocalPlayerId ?? 0L);
            _networkTargetSelector = selector;
            // 点按瞄准的目标来源：本地角色瞄准时向 selector 查询当前锁定目标位置
            _localEntry.SetSelectedTargetPositionProvider(() =>
                selector != null && selector.TryGetLockedPosition(out var p) ? p : (Vector3?)null);
            // AOI 出界：ViewRemoved 先于视图销毁触发——先清锁（解绑选中环），否则环会随目标一起被销毁
            _remoteEntities!.ViewRemoved += HandleRemoteViewRemoved;

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

        private void AttachConnectionCombat()
        {
            _combatBridge = new CombatNetworkBridge(_adapter!, () => _manager!.LocalPlayerId, _remoteEntities!,
                onDamageToLocalPlayer: (targetId, damage, crit, direction, hp, blocked, sourceIsPlayer) =>
                    _localEntry?.ApplyServerDamage(damage, direction, hp, blocked, sourceIsPlayer),
                onDotDamageToLocalPlayer: (targetId, damage, hp) =>
                    _localEntry?.ApplyDotDamage(damage, hp));
            _adapter!.MessageReceived += _combatBridge.OnMessage;
            if (_pendingWeaponBaseline != null)
                _combatBridge.Equipment.InitializeFromBaseline(_pendingWeaponBaseline);
            _pendingWeaponBaseline = null;
            _combatBridge.OnEquipResult += HandleEquipResult;
            _combatBridge.OnCastConfirm += HandleCastConfirm;
            SubscribeWeaponBag();

            // 远程施法表现（玩家类）。localPlayerId 取登录后由服务端分配的 id——
            // presenter 用它跳过本地施法者（本地由 SkillExecutor 播放）。
            _remoteCastPresenter = new RemoteCastPresenter(
                _remoteEntities!, _manager!.LocalPlayerId, _skillCatalog,
                sceneIdProvider: () => _manager?.ZoneId ?? "",
                serverNowTicksProvider: () => _manager?.ServerNowTicks() ?? 0,
                serverTickProvider: () => _manager?.ServerTick ?? 0);
            _combatBridge.OnDamageResult += HandleRemoteDamageResult;
            _combatBridge.OnProjectileLaunch += _remoteCastPresenter.OnProjectileLaunch;
            _combatBridge.OnProjectileStop += _remoteCastPresenter.OnProjectileStop;
            _combatBridge.OnCastCancel += HandleCastCancel;
            _combatBridge.OnNpcAction += HandleNpcAction;
            _combatBridge.OnCombatStateUpdate += HandleCombatStateUpdate;
            _remoteEntities!.ViewRemoved += _remoteCastPresenter.ClearForEntity;

            // 施法广播除日志外还要**路由**：NPC 由 registry 按发布者时间戳 + skillId 排成 renderAt 处的挥砍，
            // 玩家类交给 presenter 立即播攻击/施法表现（本回调已在主线程——CombatNetworkBridge.Update
            // 消费，见该类线程模型）。两条支路各自按 Kind 过滤，互不干扰。
            _combatBridge.OnCastBroadcast += b =>
            {
                Debug.Log($"[NetHost] cast broadcast: caster={b.CasterPlayerId} skill={b.SkillId}");
                _remoteCastPresenter?.OnCastBroadcast(b);
                _remoteEntities!.ScheduleAttack(b.CasterPlayerId, b.ServerTimestamp, b.SkillId);
            };
            _combatBridge.OnLineProjectileStopped += HandleLineProjectileStopped;
            _combatBridge.OnSwordQiEchoState += HandleSwordQiEchoState;

        }

        private void SubscribeWeaponBag()
        {
            if (_bagEquipSubscribed) return;
            BagManager.Instance.OnEquipRequested += HandleBagEquipRequested;
            _bagEquipSubscribed = true;
        }

        private void TryAttachOfflineWeapons()
        {
            if (_localEntry == null || _localEntry.SkillCoordinator == null || _offlineWeaponOwner == _localEntry)
                return;

            if (_offlineEquipment == null)
            {
                var catalog = ElementalCatalogDefaults.Create();
                var baseline = new WeaponInventoryBaseline { TemplateId = catalog.DefaultWeaponTemplateId };
                foreach (int templateId in catalog.StartingWeaponTemplateIds)
                {
                    var weapon = catalog.Weapons.Find(entry => entry.TemplateId == templateId);
                    if (weapon == null) continue;
                    long instanceId = baseline.Weapons.Count + 1L;
                    _offlineWeapons.Add(instanceId, weapon);
                    baseline.Weapons.Add(new WeaponInstanceSummary
                    {
                        InstanceId = instanceId, TemplateId = templateId, ElementId = weapon.ElementId
                    });
                    if (templateId == baseline.TemplateId)
                    {
                        baseline.WeaponInstanceId = instanceId;
                        baseline.ElementId = weapon.ElementId;
                    }
                }
                _offlineEquipment = new ClientEquipmentState(catalog);
                _offlineEquipment.InitializeFromBaseline(baseline);
                BagManager.Instance.ApplyServerWeapons(baseline.Weapons);
                SubscribeWeaponBag();
            }

            _offlineWeaponOwner = _localEntry;
            _localEntry.SkillCoordinator.OfflineWeaponProvider = () =>
                _offlineEquipment != null
                && _offlineWeapons.TryGetValue(_offlineEquipment.WeaponInstanceId, out var weapon) ? weapon : null;
            ApplyEquipmentState();
        }

        private uint RequestWeapon(long instanceId)
        {
            if (_combatBridge != null) return _combatBridge.RequestEquip(instanceId);
            if (_offlineEquipment == null || !_offlineWeapons.ContainsKey(instanceId)) return 0;
            return _offlineEquipment.Revision + 1u;
        }

        private void HandleCastConfirm(CastConfirm confirm)
        {
            var equipment = _combatBridge?.Equipment;
            if (equipment == null || confirm.Accepted || confirm.RejectReason != "stale-equipment"
                || confirm.CasterLifeId != equipment.LifeId || confirm.WeaponInstanceId == 0) return;
            if (equipment.TryApply(new EquipWeaponConfirm
                {
                    EquipmentRevision = confirm.EquipmentRevision,
                    WeaponInstanceId = confirm.WeaponInstanceId,
                    TemplateId = confirm.TemplateId,
                    ElementId = confirm.ElementId
                }))
                ApplyEquipmentState();
        }

        private void HandleBagEquipRequested(ItemData item)
        {
            if (_weaponBagController == null || item == null) return;
            if (_weaponBagController.TryRequest(item.InstanceId, Time.unscaledTime)
                && _offlineEquipment != null && _offlineWeapons.TryGetValue(item.InstanceId, out var weapon))
            {
                uint revision = _offlineEquipment.Revision + 1u;
                var confirm = new EquipWeaponConfirm
                {
                    ClientEquipSeq = revision,
                    EquipmentRevision = revision,
                    Accepted = true,
                    WeaponInstanceId = item.InstanceId,
                    TemplateId = weapon.TemplateId,
                    ElementId = weapon.ElementId
                };
                _offlineEquipment.TryApply(confirm);
                HandleEquipResult(confirm);
            }
            RefreshWeaponBag();
        }

        [SerializeField] private Font _weaponBagFont;
        private WeaponBagView _weaponBag;

        private void RefreshWeaponBag()
        {
            if (_weaponBagFont == null || _weaponBagController == null
                || CurrentEquipment?.HasEquipment != true || UIManager.Instance == null) return;
            if (_weaponBagRenderedRevision == _weaponBagController.Revision) return;
            if (_weaponBag == null)
            {
                var bagObject = new GameObject("WeaponBag", typeof(RectTransform));
                bagObject.transform.SetParent(UIManager.Instance.GetLayerRoot(LayerType.Popup), false);
                _weaponBag = bagObject.AddComponent<WeaponBagView>();
            }
            _weaponBagRenderedRevision = _weaponBagController.Revision;
            _weaponBag.Refresh(BagManager.Instance.Data,
                _weaponBagController.EquippedInstanceId,
                _weaponBagController.PendingInstanceId,
                _weaponBagController.DisplayStatus,
                _weaponBagFont);
        }

        private void HandleEquipResult(EquipWeaponConfirm confirm)
        {
            var equipment = CurrentEquipment;
            if (_weaponBagController == null || equipment == null) return;
            if (!confirm.Accepted)
                Debug.LogWarning("[NetHost] equip rejected: " + confirm.RejectReason
                    + " rev=" + confirm.EquipmentRevision);
            var outcome = _weaponBagController.ApplyConfirm(confirm, equipment.WeaponInstanceId,
                equipment.ElementId, equipment.AttackSpeedMultiplier);
            if (outcome != WeaponEquipOutcome.Stale && _localEntry != null)
                _localEntry.FinishWeaponChange();
            RefreshWeaponBag();
        }

        private void TickWeaponBag()
        {
            if (_weaponBagController == null || CurrentEquipment == null) return;
            bool wasPending = _weaponBagController.HasPending;
            _weaponBagController.Tick(Time.unscaledTime);
            if (wasPending && !_weaponBagController.HasPending)
                _localEntry?.SkillCoordinator?.FinishWeaponChange(replay: false);
            RefreshWeaponBag();
        }

        private void ApplyEquipmentState()
        {
            if (_weaponBagController == null) return;
            var equipment = CurrentEquipment;
            if (equipment?.HasEquipment == true)
                _weaponBagController.ApplyAuthoritative(equipment.WeaponInstanceId,
                    equipment.ElementId, equipment.AttackSpeedMultiplier, force: true);
            else
                _weaponBagController.Reapply();
            _weaponBagRenderedRevision = -1; // 基线/角色重建后强制重绘列表
            RefreshWeaponBag();
        }

        private WeaponAppearanceResult ApplySwordAppearance(int elementId, float attackSpeed)
        {
            if (_localEntry == null || CurrentEquipment?.HasEquipment != true)
                return WeaponAppearanceResult.Unavailable;

            bool applied = _localEntry.ApplySwordView(elementId);
            if (elementId == 0) return WeaponAppearanceResult.Applied;
            return applied ? WeaponAppearanceResult.Applied : WeaponAppearanceResult.Missing;
        }

        private void OnLocalSkillCast(SkillData skillData)
        {
            // 可蓄力技能（ChargedSkillData / MaxChargeTime>0 的投射物如 20003）激活时不发，
            // 由 OnChargeReleased 在释放时刻发；30001 带落点。策略见 SkillCastReporter
            if (_networkTargetSelector != null) _networkTargetSelector.EnsureTargetFor(skillData);
            Vector3 targetPosition = _localEntry.SkillCoordinator.CurrentSkill?.TargetPosition ?? Vector3.zero;
            SkillCastReporter.ReportActivation(_combatBridge, skillData, targetPosition, _localEntry.transform,
                _networkTargetSelector != null ? _networkTargetSelector.LockedTargetId : 0L);
        }

        /// <summary>蓄力释放（松开/蓄满）→ 释放时刻的落点与进度随 CastRequest 上线</summary>
        private void OnLocalChargeReleased(SkillData skillData, float chargeProgress, Vector3 impactPoint)
        {
            if (_networkTargetSelector != null) _networkTargetSelector.EnsureTargetFor(skillData);
            SkillCastReporter.ReportChargeRelease(_combatBridge, skillData, chargeProgress, impactPoint,
                _networkTargetSelector != null ? _networkTargetSelector.LockedTargetId : 0L);
        }

        /// <summary>旋转取消/被打断 → 服务端丢弃未到期判定段（B3）</summary>
        private void OnLocalSkillCancelled(SkillData skillData)
            => SkillCastReporter.ReportCancel(_combatBridge, skillData);

        /// <summary>联网请求阶段：发 CastRequest，暂锁新主动施法并等待确认。</summary>
        private void HandleEchoRequested(SwordQiEchoSkillData data)
        {
            if (_combatBridge == null || _manager == null || _localEntry == null
                || (_manager.EnabledFeatures & ProtocolFeatures.SwordQiEcho) == 0)
            {
                _localEntry?.SkillCoordinator.CancelEchoRequest();
                return;
            }

            _nextEchoRequestId++;
            _echoRequestPending = true;
            _pendingEchoSkill = data;
            _pendingEchoRequestId = _nextEchoRequestId;
            _pendingEchoConnectionGeneration = _echoConnectionGeneration;
            _pendingEchoDeadline = Time.unscaledTime + data.ConfirmationTimeoutSeconds;
            SkillCastReporter.ReportEchoRequest(_combatBridge, data, _nextEchoRequestId, _echoBindingId);
        }

        private void TickEchoRequest()
        {
            if (!_echoRequestPending) return;
            if (Time.unscaledTime < _pendingEchoDeadline) return;

            // 超时释放本地等待/施法锁，但保留请求归属记录以接住迟到接受。
            _echoRequestPending = false;
            _localEntry?.SkillCoordinator?.CancelEchoRequest();
        }

        private void ClearEchoRequest()
        {
            _echoRequestPending = false;
            _pendingEchoSkill = null;
            _pendingEchoRequestId = 0;
            _pendingEchoConnectionGeneration = 0;
            // 角色可能已生成但 Start 未跑（OnPlayerSpawned → Connect 的真实窗口）：
            // 此时 SkillCoordinator 尚未建立，清理路径不得因此抛 NRE。
            _localEntry?.SkillCoordinator?.CancelEchoRequest();
        }

        private void HandleSwordQiEchoState(SwordQiEchoState state)
        {
            if (_manager == null) return;
            if (state.CasterId != _manager.LocalPlayerId)
            {
                HandleRemoteSwordQiEchoState(state);
                return;
            }
            if (_localEntry == null) return;
            if (state.EchoBindingId != 0 && state.EchoBindingId != _echoBindingId) return;
            if (_echoRequestPending
                && _pendingEchoConnectionGeneration != _echoConnectionGeneration) return;
            if (state.RequestId != 0 && state.RequestId < _lastAppliedEchoRequestId) return;

            switch (state.Phase)
            {
                case SwordQiEchoPhase.Rejected:
                    HandleEchoRejected(state);
                    break;
                case SwordQiEchoPhase.Started:
                    HandleEchoStarted(state);
                    break;
                case SwordQiEchoPhase.Ended:
                    HandleEchoEnded(state);
                    break;
            }
        }

        private void HandleEchoRejected(SwordQiEchoState state)
        {
            if (state.RequestId != 0 && state.RequestId != _pendingEchoRequestId) return;

            _echoRequestPending = false;
            _pendingEchoSkill = null;
            _pendingEchoRequestId = 0;
            _localEntry?.SkillCoordinator.CancelEchoRequest();
        }

        private void HandleEchoStarted(SwordQiEchoState state)
        {
            if (state.CastId != 0 && state.CastId == _endedEchoCastId) return;

            if (!IsNewerEchoState(state))
            {
                if (state.RequestId != 0 && state.RequestId == _pendingEchoRequestId)
                {
                    _echoRequestPending = false;
                    _pendingEchoSkill = null;
                    _pendingEchoRequestId = 0;
                    _localEntry?.SkillCoordinator.CancelEchoRequest();
                }
                return;
            }

            var data = (state.RequestId != 0 && state.RequestId == _pendingEchoRequestId
                    ? _pendingEchoSkill
                    : null)
                ?? _localEntry.SkillCoordinator.GetSkillData((int)SkillID.SwordQiEcho) as SwordQiEchoSkillData;
            if (data == null) return;

            long nowServer = _manager.ServerNowTicks();
            double durationSeconds = (state.EndTick - state.StartTick) / (double)CombatRules.TickRateHz;
            double elapsedSeconds = Math.Max(0d,
                (nowServer - state.ServerTimestamp) / (double)TimeSpan.TicksPerSecond);
            double remainingSeconds = durationSeconds - elapsedSeconds;
            float cooldownTotal = state.CooldownTicks / CombatRules.TickRateHz;
            float cooldownElapsed = (float)Math.Min(cooldownTotal, elapsedSeconds);

            if (remainingSeconds > 0d)
            {
                double endLocal = Time.time + remainingSeconds;
                bool committed = _localEntry.SkillCoordinator.CommitEchoRequest(
                    data, endLocal, cooldownTotal, cooldownElapsed, () => (double)Time.time);
                if (committed)
                {
                    _localEchoPresenter = _localEntry.GetComponent<SwordQiEchoPresenter>();
                    if (_localEchoPresenter == null)
                        _localEchoPresenter = _localEntry.gameObject.AddComponent<SwordQiEchoPresenter>();
                    _localEchoPresenter.Play(_localEntry.transform, state, elapsedSeconds, () => (double)Time.time);
                }
            }
            else
            {
                _localEntry.SkillCoordinator.ApplyAuthoritativeCooldown(
                    data.SkillId, cooldownTotal, cooldownElapsed);
            }

            _lastAppliedEchoCastId = state.CastId;
            _lastAppliedEchoStartTick = state.StartTick;
            _lastAppliedEchoRequestId = state.RequestId;
            _echoRequestPending = false;
            _pendingEchoSkill = null;
            _pendingEchoRequestId = 0;
        }

        private void HandleEchoEnded(SwordQiEchoState state)
        {
            if (state.CastId != 0 && state.CastId == _endedEchoCastId) return;
            _endedEchoCastId = state.CastId;

            if (state.CastId != 0 && state.CastId == _lastAppliedEchoCastId)
            {
                if (_localEntry.SkillCoordinator.CurrentSkill?.Data is SwordQiEchoSkillData)
                    _localEntry.SkillCoordinator.CurrentSkill.ForceComplete();
                _localEchoPresenter?.Stop();
            }

            if (state.RequestId != 0 && state.RequestId == _pendingEchoRequestId)
            {
                _echoRequestPending = false;
                _pendingEchoSkill = null;
                _pendingEchoRequestId = 0;
                _localEntry.SkillCoordinator.CancelEchoRequest();
            }
        }

        private bool IsNewerEchoState(SwordQiEchoState state)
        {
            if (_lastAppliedEchoCastId == 0) return true;
            return state.StartTick > _lastAppliedEchoStartTick
                || (state.StartTick == _lastAppliedEchoStartTick
                    && state.CastId > _lastAppliedEchoCastId);
        }

        private void HandleRemoteSwordQiEchoState(SwordQiEchoState state)
        {
            if (state.Phase == SwordQiEchoPhase.Rejected) return;
            if (state.Phase == SwordQiEchoPhase.Ended)
            {
                if (_remoteEchoPresenters.TryGetValue(state.CastId, out var ended))
                {
                    ended.Presenter.Stop();
                    _remoteEchoPresenters.Remove(state.CastId);
                }
                return;
            }

            if (state.Phase != SwordQiEchoPhase.Started) return;
            if (_remoteEchoPresenters.ContainsKey(state.CastId)) return;

            var view = _remoteEntities?.GetView(state.CasterId);
            if (view == null) return;

            long nowServer = _manager!.ServerNowTicks();
            double elapsedSeconds = Math.Max(0d,
                (nowServer - state.ServerTimestamp) / (double)TimeSpan.TicksPerSecond);
            double durationSeconds = (state.EndTick - state.StartTick) / (double)CombatRules.TickRateHz;
            if (elapsedSeconds >= durationSeconds) return;

            var presenter = view.GameObject.GetComponent<SwordQiEchoPresenter>();
            if (presenter == null)
                presenter = view.GameObject.AddComponent<SwordQiEchoPresenter>();
            presenter.Play(view.GameObject.transform, state, elapsedSeconds, () => (double)Time.time);
            _remoteEchoPresenters[state.CastId] = new RemoteEchoInstance
            {
                CasterId = state.CasterId,
                Presenter = presenter
            };
        }

        /// <summary>远程视图移除（AOI 出界/离线）：目标就是当前锁定时先清锁（ViewRemoved 在 DestroyView 之前）</summary>
        private void HandleRemoteViewRemoved(long entityId)
        {
            // 视图销毁前清掉状态表现（ViewRemoved 在 DestroyView 之前触发）
            _statusVfx?.Remove(entityId);
            if (_networkTargetSelector != null && _networkTargetSelector.LockedTargetId == entityId)
                _networkTargetSelector.ClearLock();

            List<ulong>? remove = null;
            foreach (var pair in _remoteEchoPresenters)
            {
                if (pair.Value.CasterId != entityId) continue;
                pair.Value.Presenter.Stop();
                (remove ??= new List<ulong>()).Add(pair.Key);
            }
            if (remove != null)
                foreach (var castId in remove) _remoteEchoPresenters.Remove(castId);
        }

        private void HandleLineProjectileStopped(Vector3 point)
            => Hotfix.GameSystems.Skills.Runtime.SwordQi.SwordQiSpawner.StopNearest(point);

        /// <summary>远程 DamageResult → 投射物视觉停点/爆炸（本地施法者由 presenter 自行跳过）</summary>
        private void HandleRemoteDamageResult(DamageResult result)
            => _remoteCastPresenter?.OnDamageResult(result);

        /// <summary>服务端取消前摇/持续施法 → 停止施法者攻击表现（已发射剑气继续飞）</summary>
        private void HandleCastCancel(CastCancelBroadcast cancel)
            => _remoteCastPresenter?.OnCastCancel(cancel);

        /// <summary>Npc 动作阶段广播 → registry 的动作存储（视图未建先缓存，建好后回放）</summary>
        private void HandleNpcAction(NpcActionEvent evt)
            => _remoteEntities?.ApplyNpcActionEvent(evt);

        /// <summary>本人投影的分流判据：登录前（localPlayerId == 0）一律不算本人——
        /// 否则未分配的 id 会把所有实体都当成自己，投影永远进不了 registry。</summary>
        internal static bool IsLocalProjection(long localPlayerId, long entityId)
            => localPlayerId != 0 && localPlayerId == entityId;

        /// <summary>战斗投影 → registry 版本合并（活动施法经 ActiveCastApplied 事件交 presenter）。
        /// 本人的投影**只在本地消费**（Control → 权威控制、MaxHp → 上限对齐），
        /// 不能交给 registry 的未知实体缓存（会一直滞留到换世界）；远端仍走 registry。</summary>
        private void HandleCombatStateUpdate(CombatStateUpdate update)
        {
            if (update == null) return;
            if (IsLocalProjection(_manager?.LocalPlayerId ?? 0, update.EntityId))
            {
                var projection = update.Projection;
                if (projection == null) return;
                if (projection.Control != null)
                    _localEntry?.ApplyAuthoritativeControl(
                        projection.Control.SpeedScale,
                        projection.Control.ActionLock,
                        projection.Control.ControlVersion);
                if (projection.MaxHp > 0f)
                    _localEntry?.SetMaxHp(projection.MaxHp, fill: false);
                // 缓存状态列表：本地角色重建后新身体不会再收到同一条投影（CombatVersion 已消费），
                // 必须按缓存对新 transform 重放染色。
                _localStatuses = projection.Statuses;
                if (_localEntry != null)
                    _statusVfx?.Apply(update.EntityId, _localEntry.transform, projection.Statuses);
                return;
            }
            _remoteEntities?.ApplyCombatStateUpdate(update);
        }

        /// <summary>投影成功合并且携带活动施法 → 远程施法表现（presenter 内按 CastId 去重）</summary>
        private void HandleActiveCastApplied(long entityId, ActiveCastProjection activeCast)
            => _remoteCastPresenter?.OnActiveCast(entityId, activeCast);

        /// <summary>
        /// 摘除目标锁定（断线 / 本地角色重建 / 宿主销毁共用）：先退订 AOI 视图移除，
        /// 再让 selector 清锁解绑并断开引用——组件本身留在本地角色上（可复用），
        /// Shutdown 后它自身的 Update 在 `_registry == null` 处早退。
        /// </summary>
        private void DetachTargetSelector()
        {
            if (_remoteEntities != null)
                _remoteEntities.ViewRemoved -= HandleRemoteViewRemoved;
            if (_networkTargetSelector != null)
            {
                _networkTargetSelector.Shutdown();
                _localEntry?.SetSelectedTargetPositionProvider(null);
                _networkTargetSelector = null;
            }
        }

        /// <summary>先解绑角色，断线时再释放连接级消息与视图。</summary>
        private void DetachCombatBridge(bool keepConnection = false)
        {
            if (_localEntry != null)
            {
                _localEntry.OnServerDeath -= HandleLocalPlayerDeath;
                var coordinator = _localEntry.SkillCoordinator;
                if (coordinator != null)
                {
                    coordinator.OnSkillActivated -= OnLocalSkillCast;
                    coordinator.OnChargeReleased -= OnLocalChargeReleased;
                    coordinator.OnSkillCancelled -= OnLocalSkillCancelled;
                    coordinator.OnEchoRequested -= HandleEchoRequested;
                    coordinator.AttackSpeedProvider = null;
                    coordinator.OfflineWeaponProvider = null;
                    coordinator.IsWeaponChangePending = null;
                    coordinator.FinishWeaponChange(replay: false);
                }
            }
            _offlineWeaponOwner = null;
            _localEchoPresenter?.Stop();
            _localEchoPresenter = null;
            ClearEchoRequest();
            if (keepConnection) return;

            _weaponBagRenderedRevision = -1;
            _weaponBagController?.Reset();
            _offlineEquipment = null;
            _offlineWeapons.Clear();
            _localStatuses = null;
            NetworkSwordQiGate.SuppressLocalSpawn = false;
            if (_weaponBag != null)
            {
                _weaponBag.gameObject.SetActive(false);
                Destroy(_weaponBag.gameObject);
                _weaponBag = null;
            }
            if (_bagEquipSubscribed)
            {
                BagManager.Instance.OnEquipRequested -= HandleBagEquipRequested;
                _bagEquipSubscribed = false;
            }

            if (_combatBridge != null && _adapter != null)
                _adapter.MessageReceived -= _combatBridge.OnMessage;
            _combatBridge = null;
            if (_remoteEntities != null && _remoteCastPresenter != null)
                _remoteEntities.ViewRemoved -= _remoteCastPresenter.ClearForEntity;
            _remoteCastPresenter?.Clear();
            _remoteCastPresenter = null;
            foreach (var instance in _remoteEchoPresenters.Values)
                instance.Presenter.Stop();
            _remoteEchoPresenters.Clear();
        }

        /// <summary>
        /// `INetworkLabelSink` 的生产实现：把 registry 的注册/更名/血量/注销转到场景的单例
        /// `EntityDisplayManager`。单例不存在（未进世界/测试宿主）时全部静默跳过——注册只发生在
        /// `RemoteEntityRegistry` 建出真人玩家视图时，bot 在 registry 侧就已被跳过。
        /// </summary>
        private sealed class NameplateLabelSink : INetworkLabelSink
        {
            public void Register(long entityId, Transform owner, string displayName)
            {
                var mgr = EntityDisplayManager.Instance;
                if (mgr == null) return;   // Unity 重载 ==：已销毁实例也判空
                mgr.RegisterWithHealth((int)entityId, owner, new NameplateConfig(displayName, showHPBar: true));
            }

            public void UpdateName(long entityId, string displayName)
            {
                var mgr = EntityDisplayManager.Instance;
                if (mgr == null) return;
                mgr.UpdateName((int)entityId, displayName);
            }

            public void UpdateHp(long entityId, float percent)
            {
                var mgr = EntityDisplayManager.Instance;
                if (mgr == null) return;
                mgr.UpdateHealth((int)entityId, percent);
            }

            public void Unregister(long entityId)
            {
                var mgr = EntityDisplayManager.Instance;
                if (mgr == null) return;
                mgr.Unregister((int)entityId);
            }
        }

        private void OnDestroy()
        {
            HideNetStats();
            if (_remoteEntities != null)
            {
                _remoteEntities.ActiveCastApplied -= HandleActiveCastApplied;
                _remoteEntities.StatusesChanged -= HandleRemoteStatusesChanged;
            }
            _statusVfx = null;
            // 先摘订阅、再 Dispose（与 Disconnect → ResetAttachment 同序）：
            // manager.Dispose 里的 registry.Clear 会发 ViewRemoved，此时 selector 若仍在订阅
            // 会去动 UI/选中环（销毁过程中的 Unity 对象操作）。
            DetachCombatBridge();
            DetachTargetSelector();
            if (_manager != null)
            {
                _manager.OnEnterWorld -= HandleEnterWorld;
                _manager.OnServerRespawn -= HandleServerRespawn;
                _manager.Names.OnNameChanged -= HandlePlayerNameChanged;
                _manager.Dispose();
                _manager = null;
            }
        }
    }
}
