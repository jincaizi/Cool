using System;
using AOT.Core.GameEventDispatcher;
using AOT.Core.ObjectPool;
using AOT.Core.ResourceLoader;
using AOT.DataDefinition.Constants;
using AOT.DataDefinition.Interfaces;
using UnityEngine;

namespace AOT.Bridge
{
    /// <summary>
    /// 玩家桥接类，供热更新层调用AOT层功能
    /// </summary>
    public static class Bridge_Player
    {
        private static IPlayerController? _playerController;

        /// <summary>
        /// 设置玩家控制器实例
        /// </summary>
        public static void SetController(IPlayerController controller)
        {
            _playerController = controller;
        }

        /// <summary>
        /// 获取玩家控制器
        /// </summary>
        public static IPlayerController? GetController()
        {
            return _playerController;
        }

        /// <summary>
        /// 派发玩家状态改变事件
        /// </summary>
        public static void DispatchStateChanged(PlayerState oldState, PlayerState newState)
        {
            var data = new PlayerStateChangedData { OldState = oldState, NewState = newState };
            GameEventDispatcher.Instance.Dispatch(EventKeys.PlayerStateChanged, data);
        }

        /// <summary>
        /// 派发玩家移动事件
        /// </summary>
        public static void DispatchMoved(Vector3 position, float speed)
        {
            var data = new PlayerMovedData { Position = position, Speed = speed };
            GameEventDispatcher.Instance.Dispatch(EventKeys.PlayerMoved, data);
        }

        /// <summary>
        /// 派发玩家跳跃事件
        /// </summary>
        public static void DispatchJumped()
        {
            GameEventDispatcher.Instance.Dispatch(EventKeys.PlayerJumped, null);
        }

        /// <summary>
        /// 派发玩家攻击事件
        /// </summary>
        public static void DispatchAttacked()
        {
            GameEventDispatcher.Instance.Dispatch(EventKeys.PlayerAttacked, null);
        }

        /// <summary>
        /// 派发玩家受击事件
        /// </summary>
        public static void DispatchHit(float damage, Vector3 hitPoint)
        {
            var data = new PlayerHitData { Damage = damage, HitPoint = hitPoint };
            GameEventDispatcher.Instance.Dispatch(EventKeys.PlayerHit, data);
        }

        /// <summary>
        /// 派发玩家死亡事件
        /// </summary>
        public static void DispatchDied()
        {
            GameEventDispatcher.Instance.Dispatch(EventKeys.PlayerDied, null);
        }

        /// <summary>
        /// 从对象池获取对象
        /// </summary>
        public static GameObject GetFromPool(string prefabKey, Vector3 position, Quaternion rotation)
        {
            if (GameObjectPool.Instance != null && GameObjectPool.Instance is AOT.Core.ObjectPool.GameObjectPool pool)
            {
                return pool.Spawn(prefabKey, position, rotation);
            }
            return null;
        }

        /// <summary>
        /// 回收对象到对象池
        /// </summary>
        public static void ReturnToPool(GameObject obj)
        {
            if (GameObjectPool.Instance != null && GameObjectPool.Instance is AOT.Core.ObjectPool.GameObjectPool pool)
            {
                pool.Recycle(obj);
            }
            else
            {
                UnityEngine.Object.Destroy(obj);
            }
        }

        /// <summary>
        /// 异步加载玩家模型
        /// </summary>
        public static void LoadPlayerModel(string address, Action<GameObject> onComplete)
        {
            ResourceLoader.Instance.LoadGameObjectAsync(address, onComplete);
        }

        /// <summary>
        /// 玩家状态改变事件数据
        /// </summary>
        public struct PlayerStateChangedData
        {
            public PlayerState OldState;
            public PlayerState NewState;
        }

        /// <summary>
        /// 玩家移动事件数据
        /// </summary>
        public struct PlayerMovedData
        {
            public Vector3 Position;
            public float Speed;
        }

        /// <summary>
        /// 玩家受击事件数据
        /// </summary>
        public struct PlayerHitData
        {
            public float Damage;
            public Vector3 HitPoint;
        }
    }
}
