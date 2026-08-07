using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.SwordQi
{
    /// <summary>
    /// 剑气生成入口：懒初始化对象池。缺配置/缺预制体时跳过并告警（不影响技能本体）。
    /// 剑气池挂在场景根下——场景卸载时剑气随场景销毁（OnDestroy 守卫防回池）。
    /// </summary>
    public static class SwordQiSpawner
    {
        /// <summary>池预热实例数（首个 Spawn 不产生实例化卡顿）</summary>
        public const int PrewarmCount = 2;

        private static ComponentPool<SwordQiProjectile> _pool;
        private static Transform _root;

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
                _root = new GameObject("SwordQiPool").transform;
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
        }
    }
}
