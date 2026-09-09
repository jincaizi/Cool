using System;
using Cool.SyncCore;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Network;
using KcpNet;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 客户端网络宿主（挂载于世界场景的任意 GameObject）——游戏内网络接线的唯一入口：
    /// 创建 adapter/manager/远程实体渲染根并每帧驱动；Connect() 登录进世界后，
    /// 自动装配本地角色（Sys3CEntry）：MovementReporter 上报（节流 + 预测帧）、
    /// NetMovementPolicy 接管移动与平滑纠偏。远程玩家以胶囊占位渲染（RemoteEntityRegistry）。
    /// 接线步骤与边界见 docs/superpowers/guides/p2-client-network-host.md。
    /// 单机模式零影响：不挂此组件或只挂不 Connect 即可纯本地游玩。
    /// </summary>
    public sealed class ClientNetworkHost : MonoBehaviour
    {
        [Header("服务器")]
        [SerializeField] private string _serverHost = "127.0.0.1";
        [SerializeField] private int _serverPort = 8000;
        [SerializeField] private string _username = "Player";

        [Header("本地角色（可选：留空则运行时查找场景内首个 Sys3CEntry）")]
        [SerializeField] private Sys3CEntry _localEntry;

        private KcpNetClientAdapter? _adapter;
        private ClientNetworkManager? _manager;
        private RemoteEntityRegistry? _remoteEntities;
        private NetMovementPolicy? _netPolicy;
        private bool _connectRequested;
        private bool _inWorld;
        private bool _attachFailed;
        private float _attachRetryTimer;
        private bool _entryLookedUp;

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

        private void Update()
        {
            if (_manager == null) return;

            // 每帧驱动：时钟采样/快照封送/纠偏响应/上报节流/插值渲染（manager 内部编排）
            _manager.Update(Time.deltaTime);

            // 进世界后装配本地角色（角色可能 Addressable 异步生成——就绪前每帧重试）
            if (_inWorld && !_attachFailed)
                TryAttachLocalCharacter();
        }

        /// <summary>发起登录（幂等；可覆盖序列化默认值）。无需登录 UI——游戏启动流接线时调用</summary>
        public void Connect(string? username = null, string? host = null, int? port = null)
        {
            if (_connectRequested || _remoteEntities == null) return;
            _connectRequested = true;

            _adapter = new KcpNetClientAdapter();
            _manager = new ClientNetworkManager(_adapter, _remoteEntities, username ?? _username);
            _manager.OnEnterWorld += HandleEnterWorld;
            _manager.OnDisconnected += reason => OnDisconnected?.Invoke(reason);
            _manager.ConnectAndLogin(host ?? _serverHost, port ?? _serverPort);
        }

        public void Disconnect()
        {
            _manager?.Dispose();
            _manager = null;
            _adapter = null;
            _connectRequested = false;
            _inWorld = false;
            _netPolicy = null;
            _attachFailed = false;
            _attachRetryTimer = 0f;
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

            Debug.Log("[NetHost] 本地角色已接入移动上报（repoter + NetMovementPolicy）");
        }

        private void OnDestroy()
        {
            if (_manager != null)
            {
                _manager.OnEnterWorld -= HandleEnterWorld;
                _manager.Dispose();
                _manager = null;
            }
        }
    }
}
