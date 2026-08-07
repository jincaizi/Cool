using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.SwordQi
{
    /// <summary>
    /// 剑气生成入口：懒初始化对象池。缺配置/缺预制体时跳过并告警（不影响技能本体）。
    /// 剑气池挂在场景容器 GameRoot 下（运行时创建，不落场景文件）——保持场景层级干净；
    /// 场景卸载时剑气随场景销毁（OnDestroy 守卫防回池），容器销毁后惰性重建。
    /// </summary>
    public static class SwordQiSpawner
    {
        /// <summary>池预热实例数（首个 Spawn 不产生实例化卡顿）</summary>
        public const int PrewarmCount = 2;

        /// <summary>场景容器名（池根挂其下，避免散落在场景根层级）</summary>
        private const string ContainerName = "GameRoot";

        private static ComponentPool<SwordQiProjectile> _pool;
        private static Transform _root;
        private static Transform _container;
        private static bool _ownsContainer;

        public static void Spawn(SwordQiConfig config, DamageBlock damage, Vector3 origin, Vector3 direction)
        {
            if (config == null || config.Prefab == null)
            {
                Debug.LogWarning("[SwordQiSpawner] config or prefab is null, skip spawn");
                return;
            }

            if (_root == null || _pool == null)
            {
                var prefabComp = config.Prefab.GetComponent<SwordQiProjectile>();
                if (prefabComp == null)
                {
                    Debug.LogWarning("[SwordQiSpawner] prefab missing SwordQiProjectile component, skip spawn");
                    return;
                }

                if (_container == null)
                {
                    var go = GameObject.Find(ContainerName);
                    if (go == null)
                    {
                        go = new GameObject(ContainerName);
                        _ownsContainer = true;
                    }
                    _container = go.transform;
                }

                _root = new GameObject("SwordQiPool").transform;
                _root.SetParent(_container, false);
                _pool = new ComponentPool<SwordQiProjectile>(prefabComp, _root, PrewarmCount);
            }

            var qi = _pool.Get();
            qi.OnSpawn(config, damage, origin, direction, _pool);
        }

        /// <summary>测试专用：清空静态池状态（避免 EditMode 测试间串扰）</summary>
        public static void DisposeForTests()
        {
            if (_root != null)
                Object.DestroyImmediate(_root.gameObject);
            _pool = null;
            _root = null;
            if (_ownsContainer && _container != null)
                Object.DestroyImmediate(_container.gameObject);
            _container = null;
            _ownsContainer = false;
        }
    }
}
