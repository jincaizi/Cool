using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.Meteor
{
    /// <summary>
    /// 陨石生成入口：懒初始化对象池（镜像 SwordQiSpawner/FireballSpawner）。
    /// 池挂在场景容器 GameRoot 下；场景卸载守卫同剑气。
    /// </summary>
    public static class MeteorSpawner
    {
        public const int PrewarmCount = 1;

        private const string ContainerName = "GameRoot";

        private static ComponentPool<MeteorStrike> _pool;
        private static Transform _root;
        private static Transform _container;
        private static bool _ownsContainer;

        public static void Spawn(MeteorConfig config, DamageBlock damage, Vector3 impactPoint)
        {
            if (config == null || config.Prefab == null)
            {
                Debug.LogWarning("[MeteorSpawner] config or prefab is null, skip spawn");
                return;
            }

            if (_root == null || _pool == null)
            {
                var prefabComp = config.Prefab.GetComponent<MeteorStrike>();
                if (prefabComp == null)
                {
                    Debug.LogWarning("[MeteorSpawner] prefab missing MeteorStrike component, skip spawn");
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

                _root = new GameObject("MeteorPool").transform;
                _root.SetParent(_container, false);
                _pool = new ComponentPool<MeteorStrike>(prefabComp, _root, PrewarmCount);
            }

            var strike = _pool.Get();
            strike.OnSpawn(config, damage, impactPoint, _pool);
        }

        /// <summary>测试专用：清空静态池状态</summary>
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
