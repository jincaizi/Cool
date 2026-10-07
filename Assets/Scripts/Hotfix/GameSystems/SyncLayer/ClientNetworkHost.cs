using System;
using Cool.SyncCore;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Sys3C.Network;
using KcpNet;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 客户端网络宿主（挂载于世界场景的任意 GameObject）——游戏内网络接线的唯一入口：
    /// 创建 adapter/manager/远程实体渲染根并每帧驱动；联网模式（NetworkModeService）下
    /// Start 自动 Connect()，登录进世界后自动装配本地角色（Sys3CEntry）：
    /// MovementReporter 上报（节流 + 预测帧）、NetMovementPolicy 接管移动与平滑纠偏。
    /// 远程玩家以胶囊占位、怪物以球体占位渲染（RemoteEntityRegistry）。
    /// 本地角色被销毁重建（DemoDay 直跑时选人确认触发 PlayerSpawner 重建）经 PlayerSpawnedEvent 重装配。
    /// 接线步骤与边界见 docs/superpowers/guides/p2-client-network-host.md 与 network-playtest.md。
    /// 单机模式零影响：选单机或只挂不 Connect 即可纯本地游玩。
    /// </summary>
    public sealed class ClientNetworkHost : MonoBehaviour
    {
        [Header("服务器")]
        // 以下三个字段**不再是连接来源**（D-P6-17）：Connect 每次调用时从 NetworkModeService
        // （PlayerPrefs "net.host"/"net.port"/"net.username"）解析。仅作 Inspector 兜底与默认值参考保留。
        [SerializeField] private string _serverHost = "127.0.0.1";
        [SerializeField] private int _serverPort = 8000;
        [SerializeField] private string _username = "Player";

        [Header("本地角色（可选：留空则运行时查找场景内首个 Sys3CEntry）")]
        [SerializeField] private Sys3CEntry _localEntry;

        [Header("调试")]
        [Tooltip("屏幕左下角连接状态浮层（模式/状态/玩家 id/远程实体数）")]
        [SerializeField] private bool _showDebugOverlay = true;

        private KcpNetClientAdapter? _adapter;
        private ClientNetworkManager? _manager;
        private RemoteEntityRegistry? _remoteEntities;
        private NetMovementPolicy? _netPolicy;
        private bool _connectRequested;
        private bool _inWorld;
        private bool _attachFailed;
        private float _attachRetryTimer;
        private bool _entryLookedUp;
        private CombatNetworkBridge? _combatBridge;

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
            _remoteEntities = new RemoteEntityRegistry(root.transform);
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
        /// </summary>
        public void Connect(string? username = null, string? host = null, int? port = null)
        {
            if (_connectRequested || _remoteEntities == null) return;
            _connectRequested = true;

            string resolvedHost = host ?? NetworkModeService.LoadHost();
            int resolvedPort = port ?? NetworkModeService.LoadPort();
            string resolvedUser = username ?? NetworkModeService.LoadUsername();

            _adapter = new KcpNetClientAdapter();
            _manager = new ClientNetworkManager(_adapter, _remoteEntities, resolvedUser);
            _manager.OnEnterWorld += HandleEnterWorld;
            _manager.OnDisconnected += reason => OnDisconnected?.Invoke(reason);
            // 连接失败/登录被拒必须留日志——否则"无异常"会被误读为"已连上"（验收假阳性）
            _manager.OnError += HandleError;
            _manager.ConnectAndLogin(resolvedHost, resolvedPort);
        }

        private void HandleError(string reason)
        {
            Debug.LogError("[NetHost] error: " + reason);
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
        }

        /// <summary>屏幕左下角连接状态浮层（调试用——Inspector `_showDebugOverlay` 可关）</summary>
        private void OnGUI()
        {
            if (!_showDebugOverlay) return;

            var mode = NetworkModeService.Load() == NetworkMode.Online ? "联网" : "单机";
            var state = _manager != null ? _manager.CurrentState.ToString() : "未连接";
            long playerId = _manager != null ? _manager.LocalPlayerId : 0L;
            int remoteCount = _remoteEntities != null ? _remoteEntities.Count : 0;
            GUI.Box(new Rect(10f, Screen.height - 34f, 470f, 26f),
                $"[Net] 模式={mode}  状态={state}  玩家id={playerId}  远程实体={remoteCount}");
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
                    _localEntry = FindObjectOfType<Sys3CEntry>();
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
            // 速度 = 水平真实速度（_controller.velocity——超速钳制链路依赖真实值，勿用 0-1 动画参数）
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
                });
            _manager.OnCorrectionNeeded += _netPolicy.ApplyServerCorrection;

            // P4 战斗桥：技能激活 → CastRequest；CastBroadcast/DamageResult → 表现/伤害挂现有事件链
            _combatBridge = new CombatNetworkBridge(_adapter!, () => _manager!.LocalPlayerId, _remoteEntities!,
                (targetId, dmg, crit, dir) => _localEntry.ApplyServerDamage(dmg, dir));
            _adapter!.MessageReceived += _combatBridge.OnMessage;
            _localEntry.SkillCoordinator.OnSkillActivated += OnLocalSkillCast;
            _combatBridge.OnCastBroadcast += b =>
                Debug.Log($"[NetHost] cast broadcast: caster={b.CasterPlayerId} skill={b.SkillId}");

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
            if (_combatBridge == null) return;
            _combatBridge.NotifySkillCast(skillData.SkillId, HitShapeConverter.FromSkillData(skillData));
        }

        /// <summary>
        /// 摘除战斗桥订阅（adapter 消息 + 本地技能事件）。Disconnect 与 OnDestroy 共用——
        /// 宿主被销毁而本地 Sys3CEntry 存活时，避免桥对已销毁宿主留下悬空委托。
        /// </summary>
        private void DetachCombatBridge()
        {
            if (_combatBridge == null || _adapter == null) return;
            _adapter.MessageReceived -= _combatBridge.OnMessage;
            if (_localEntry != null)
                _localEntry.SkillCoordinator.OnSkillActivated -= OnLocalSkillCast;
            _combatBridge = null;
        }

        private void OnDestroy()
        {
            if (_manager != null)
            {
                _manager.OnEnterWorld -= HandleEnterWorld;
                _manager.Dispose();
                _manager = null;
            }
            DetachCombatBridge();
        }
    }
}
