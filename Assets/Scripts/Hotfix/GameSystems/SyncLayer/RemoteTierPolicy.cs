using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>表现档位（design §3）。Proxy = 代理：无 Animator、无蒙皮、1 个 draw call。
    /// **出视野仍是销毁，不是第三档**——生命周期与档位是两回事。</summary>
    public enum RemoteTier { Full = 0, Proxy = 1 }

    /// <summary>分档候选（每帧重建——位置每帧在变，缓存没意义）</summary>
    public struct TierCandidate
    {
        public long PlayerId;
        /// <summary>与观察者的 XZ 平面距离的平方（与 AOI 同心）</summary>
        public float DistanceSqr;
        /// <summary>当前是否已是 Full（迟滞的两半都用它）</summary>
        public bool CurrentlyFull;
    }

    /// <summary>
    /// 分档策略（design §4）。**距离保观感、预算控成本**：
    ///   · 进入 60m / 退出 69m（= 进入 × 1.15，与 AOI 的 80/90 同款迟滞）；
    ///   · 每帧最多 MaxFull 个 Full，其余降 Proxy——超出预算的按"有效距离"最近优先。
    ///
    /// 无分配：比较器在构造时建好（每帧 new 闭包会在 WebGL 上堆 GC）。
    /// </summary>
    public sealed class RemoteTierPolicy
    {
        /// <summary>进入 Full 的距离（米）。人形在 60m 处约 25px，切换看不出来</summary>
        public float FullDistance = 60f;
        /// <summary>退出 Full 的距离倍率（迟滞）</summary>
        public float ExitRatio = 1.15f;
        /// <summary>每帧 Full 档预算——**未经实测校准的保守起点**，任务 10 实测后回填</summary>
        public int MaxFull = 20;
        /// <summary>已在 Full 的候选在预算排序里的距离折扣（预算裁剪迟滞：距离相近不来回抢档）</summary>
        public float FullRetention = 0.9f;

        public float ExitDistance => FullDistance * ExitRatio;

        // 复用缓冲 + 缓存的比较器（无每帧分配）
        private readonly List<int> _eligible = new List<int>();
        private readonly Comparison<int> _indexOrder;
        private List<TierCandidate> _candidates = null!;

        public RemoteTierPolicy() => _indexOrder = CompareIndex;

        /// <summary>
        /// 逐候选定档，写入 <paramref name="results"/>（**与 candidates 同序**；调用方复用同一个 List）。
        /// </summary>
        public void Decide(List<TierCandidate> candidates, List<RemoteTier> results)
        {
            _candidates = candidates;
            _eligible.Clear();
            results.Clear();

            for (int i = 0; i < candidates.Count; i++)
            {
                results.Add(RemoteTier.Proxy);   // 默认降档，下面只把够格又排得上的抬回 Full
                var c = candidates[i];
                float limit = c.CurrentlyFull ? ExitDistance : FullDistance;
                if (c.DistanceSqr <= limit * limit) _eligible.Add(i);
            }

            if (_eligible.Count == 0) return;

            int keep = Mathf.Min(MaxFull, _eligible.Count);
            if (keep < _eligible.Count) _eligible.Sort(_indexOrder);

            for (int i = 0; i < keep; i++) results[_eligible[i]] = RemoteTier.Full;
        }

        private int CompareIndex(int a, int b)
        {
            float da = EffectiveDistance(_candidates[a]);
            float db = EffectiveDistance(_candidates[b]);
            int cmp = da.CompareTo(db);
            return cmp != 0 ? cmp : _candidates[a].PlayerId.CompareTo(_candidates[b].PlayerId);
        }

        /// <summary>有效距离：已在 Full 的 × FullRetention（比较用，故开方回距离域——几十次 sqrt 可忽略）</summary>
        private float EffectiveDistance(TierCandidate c)
        {
            float distance = Mathf.Sqrt(c.DistanceSqr);
            return c.CurrentlyFull ? distance * FullRetention : distance;
        }
    }
}
