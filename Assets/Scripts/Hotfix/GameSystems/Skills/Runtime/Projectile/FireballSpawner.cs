using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.Projectile
{
    /// <summary>
    /// 火球生成入口：懒初始化对象池。缺配置/缺预制体时跳过并告警（不影响技能本体）。
    /// 池挂在场景容器 GameRoot 下（运行时创建，不落场景文件）；场景卸载守卫同剑气。
    /// </summary>
    public static class FireballSpawner
    {
        /// <summary>池预热实例数（首个 Spawn 不产生实例化卡顿）</summary>
        public const int PrewarmCount = 2;

        /// <summary>场景容器名（池根挂其下，避免散落在场景根层级）</summary>
        private const string ContainerName = "GameRoot";

        private static ComponentPool<FireballProjectile> _pool;
        private static Transform _root;
        private static Transform _container;
        private static bool _ownsContainer;

        public static void Spawn(ProjectileSkillData data, DamageBlock damage, Vector3 origin, Vector3 direction)
        {
            if (data == null || data.ProjectilePrefab == null)
            {
                Debug.LogWarning("[FireballSpawner] data or prefab is null, skip spawn");
                return;
            }

            if (_root == null || _pool == null)
            {
                var prefabComp = data.ProjectilePrefab.GetComponent<FireballProjectile>();
                if (prefabComp == null)
                {
                    Debug.LogWarning("[FireballSpawner] prefab missing FireballProjectile component, skip spawn");
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

                _root = new GameObject("FireballPool").transform;
                _root.SetParent(_container, false);
                _pool = new ComponentPool<FireballProjectile>(prefabComp, _root, PrewarmCount);
            }

            var fb = _pool.Get();
            fb.OnSpawn(data, damage, origin, direction, _pool);
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
