using System.Collections.Generic;
using Cool.SyncCore;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 远程实体插值器（spec D1：渲染时钟落后 2 ticks）——
    /// 快照缓冲按服务器时钟排序，渲染时刻 = now − renderDelayTicks，前后快照插值。
    /// 位置/朝向插值；状态（ActionState/Dead）是**阶梯函数**——取时间戳 ≤ renderAt 的最后一个样本，不插值。
    /// 时钟换算经 ClockSyncDriver.ToServerTime（D5 决策）。
    /// 单线程（主线程）使用：AddSample 与 TryGetRenderState 均由 ClientNetworkManager.Update 驱动。
    /// 缓冲有序性不变式由外部保证（KCP 有序投递 + 服务器时钟单调——本类不校验，勿直接乱序喂样）。
    /// </summary>
    public sealed class RemoteInterpolator
    {
        private readonly Dictionary<long, List<Sample>> _buffers = new Dictionary<long, List<Sample>>();
        private const int MaxSamplesPerEntity = 16;

        /// <summary>
        /// 入样本。actionState/dead 与位置样本同源同 tick —— 状态按阶梯采样，位置按插值。
        /// serverTicks = 所属 WorldSnapshot.ServerTimestamp（**绝对服务器时钟**——
        /// 与 ClockSyncDriver.ToServerTime 同基准；不得用 tick 序号换算的相对基准，否则插值错乱）
        /// </summary>
        public void AddSample(long playerId, long serverTicks, Vec3 position, float rotationY,
            byte actionState, bool dead)
        {
            if (!_buffers.TryGetValue(playerId, out var samples))
            {
                samples = new List<Sample>();
                _buffers[playerId] = samples;
            }
            samples.Add(new Sample
            {
                ServerTicks = serverTicks, Position = position, RotationY = rotationY,
                ActionState = actionState, Dead = dead
            });
            if (samples.Count > MaxSamplesPerEntity)
                samples.RemoveAt(0);
        }

        /// <summary>计算渲染状态：nowServerTicks 为换算后的服务器时钟，renderDelayTicks 为渲染延迟。
        /// 位置/朝向插值；actionState/dead 是**阶梯函数**——取时间戳 ≤ renderAt 的最后一个样本。</summary>
        public bool TryGetRenderState(long playerId, long nowServerTicks, long renderDelayTicks,
            out Vector3 position, out Quaternion rotation, out byte actionState, out bool dead)
        {
            position = default;
            rotation = default;
            actionState = 0;
            dead = false;
            if (!_buffers.TryGetValue(playerId, out var samples) || samples.Count == 0)
                return false;

            long renderAt = nowServerTicks - renderDelayTicks;

            // 最新样本仍早于渲染时刻 → 用最新（快照不足场景）
            if (renderAt >= samples[samples.Count - 1].ServerTicks)
            {
                var last = samples[samples.Count - 1];
                position = ToUnity(last.Position);
                rotation = Quaternion.Euler(0f, last.RotationY, 0f);
                actionState = last.ActionState;
                dead = last.Dead;
                return true;
            }

            // 找包围渲染时刻的相邻样本插值
            for (int i = 0; i < samples.Count - 1; i++)
            {
                var a = samples[i];
                var b = samples[i + 1];
                if (renderAt >= a.ServerTicks && renderAt <= b.ServerTicks)
                {
                    long span = b.ServerTicks - a.ServerTicks;
                    float t = span <= 0 ? 0f : (float)(renderAt - a.ServerTicks) / span;
                    position = Vector3.LerpUnclamped(ToUnity(a.Position), ToUnity(b.Position), t);
                    rotation = Quaternion.Slerp(Quaternion.Euler(0f, a.RotationY, 0f), Quaternion.Euler(0f, b.RotationY, 0f), t);
                    // 状态是阶梯：取较早样本（不随 t 混合）
                    actionState = a.ActionState;
                    dead = a.Dead;
                    return true;
                }
            }

            // 渲染时刻早于全部样本 → 用最旧（时钟尚未追平）
            var first = samples[0];
            position = ToUnity(first.Position);
            rotation = Quaternion.Euler(0f, first.RotationY, 0f);
            actionState = first.ActionState;
            dead = first.Dead;
            return true;
        }

        /// <summary>只取状态（阶梯语义，与 TryGetRenderState 同一规则）——供渲染循环按 renderAt 采样。</summary>
        public bool TryGetSampleState(long playerId, long renderAt, out byte actionState, out bool dead)
        {
            actionState = 0;
            dead = false;
            if (!_buffers.TryGetValue(playerId, out var samples) || samples.Count == 0)
                return false;

            // 时间戳 ≤ renderAt 的最后一个样本；全部晚于 renderAt 时用最旧（与位置路径的 fallback 一致）
            Sample chosen = samples[0];
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].ServerTicks <= renderAt) chosen = samples[i];
                else break;
            }
            actionState = chosen.ActionState;
            dead = chosen.Dead;
            return true;
        }

        public void Remove(long playerId) => _buffers.Remove(playerId);

        public void Clear() => _buffers.Clear();

        private static Vector3 ToUnity(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        private struct Sample
        {
            public long ServerTicks;
            public Vec3 Position;
            public float RotationY;
            public byte ActionState;
            public bool Dead;
        }
    }
}
