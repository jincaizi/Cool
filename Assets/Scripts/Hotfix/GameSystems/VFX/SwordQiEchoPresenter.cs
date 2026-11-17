using System;
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>
    /// 「剑气回荡」表现：按首次接触时间表驱动多条剑气轨迹，穿过目标后在切线方向错时回旋。
    /// 不持有权威状态；本地/远端都可用同一套实例数据启动。Tick 可注入时钟，便于确定性测试。
    /// </summary>
    public sealed class SwordQiEchoPresenter : MonoBehaviour
    {
        private sealed class ScheduledPoint
        {
            public Vector3 Position;
            public double DueSeconds;
        }

        private sealed class SwordRoute
        {
            public GameObject Root = null!;
            public TrailRenderer Trail = null!;
            public int SwordIndex;
            public readonly List<ScheduledPoint> Points = new List<ScheduledPoint>();
            public int Next;
            public Vector3 Previous;
            public Vector3 Current;
            public Vector3 LastPoint;
            public double FinishedAt = -1d;
        }

        [SerializeField] private GameObject? _swordPrefab;
        [SerializeField] private float _trailTime = 0.18f;
        [SerializeField] private float _swordHeight = 1.1f;
        [SerializeField] private float _idleRadius = 1.5f;

        private readonly List<SwordRoute> _active = new List<SwordRoute>();
        private readonly List<SwordRoute> _pool = new List<SwordRoute>();
        private Transform? _owner;
        private double _startLocalTime;
        private double _endLocalTime;
        private Func<double> _now = () => Time.time;
        private Material? _fallbackTrailMaterial;
        private bool _playing;

        public bool IsPlaying => _playing;
        public int ActiveSwordCount => _active.Count;

        public Vector3 SwordPosition(int index)
            => index >= 0 && index < _active.Count ? _active[index].Current : Vector3.zero;

        public void Play(Transform owner, IReadOnlyList<SwordQiEchoContact> contacts,
            float durationSeconds, double elapsedSeconds = 0d, Func<double>? now = null)
        {
            Stop();
            if (owner == null || contacts == null) return;

            _owner = owner;
            _now = now ?? (() => Time.time);
            _startLocalTime = _now() - Math.Max(0d, elapsedSeconds);
            _endLocalTime = _startLocalTime + Math.Max(0.1f, durationSeconds);
            BuildRoutes(contacts);
            _playing = true;
        }

        public void Play(Transform owner, SwordQiEchoState state,
            double elapsedSeconds = 0d, Func<double>? now = null)
        {
            if (state == null) return;
            var contacts = new List<SwordQiEchoContact>(state.Contacts.Count);
            foreach (var contact in state.Contacts)
            {
                contacts.Add(new SwordQiEchoContact(contact.TargetId, contact.SwordIndex,
                    contact.OffsetTicks, new Vec3(contact.X, contact.Y, contact.Z)));
            }

            double duration = (state.EndTick - state.StartTick) / (double)CombatRules.TickRateHz;
            Play(owner, contacts, (float)duration, elapsedSeconds, now);
        }

        public void Stop()
        {
            _playing = false;
            foreach (var route in _active)
            {
                route.Trail.emitting = false;
                route.Trail.Clear();
                route.Root.SetActive(false);
                _pool.Add(route);
            }
            _active.Clear();
            _owner = null;
        }

        public void Tick(double now)
        {
            if (!_playing) return;
            if (now >= _endLocalTime)
            {
                Stop();
                return;
            }

            for (int i = 0; i < _active.Count; i++)
            {
                var route = _active[i];
                // 到点后把“上一位置”推进到目标点；未到点的轨迹向下一目标插值。
                while (route.Next < route.Points.Count && now >= route.Points[route.Next].DueSeconds)
                {
                    route.Previous = route.Points[route.Next].Position;
                    route.Current = route.Previous;
                    route.LastPoint = route.Previous;
                    route.Next++;
                    if (route.Next >= route.Points.Count)
                        route.FinishedAt = now;
                }

                if (route.Next < route.Points.Count)
                {
                    var target = route.Points[route.Next];
                    double from = route.Next == 0 ? _startLocalTime : route.Points[route.Next - 1].DueSeconds;
                    double span = Math.Max(0.0001d, target.DueSeconds - from);
                    float t = Mathf.Clamp01((float)((now - from) / span));
                    route.Current = Vector3.Lerp(route.Previous, target.Position, t);
                }
                else if (route.FinishedAt >= 0d && now - route.FinishedAt < 0.15d)
                {
                    // 接触后短暂保持穿透点，避免到点立刻跳回回旋轨迹。
                    route.Current = route.LastPoint;
                }
                else
                {
                    float angle = (float)now * 110f + route.SwordIndex * (360f / 4f);
                    Vector3 offset = Quaternion.Euler(0f, angle, 0f)
                                     * (_owner != null ? _owner.forward : Vector3.forward)
                                     * _idleRadius;
                    route.Current = (_owner != null ? _owner.position : Vector3.zero)
                                    + offset + Vector3.up * _swordHeight;
                }

                Vector3 delta = route.Current - route.Root.transform.position;
                route.Root.transform.position = route.Current;
                if (delta.sqrMagnitude > 0.0001f)
                    route.Root.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            }
        }

        private void Update() => Tick(_now());

        private void OnDisable() => Stop();

        private void BuildRoutes(IReadOnlyList<SwordQiEchoContact> contacts)
        {
            var grouped = new Dictionary<int, List<ScheduledPoint>>();
            foreach (var contact in contacts)
            {
                if (!grouped.TryGetValue(contact.SwordIndex, out var points))
                {
                    points = new List<ScheduledPoint>();
                    grouped[contact.SwordIndex] = points;
                }
                points.Add(new ScheduledPoint
                {
                    Position = new Vector3(contact.Position.X, contact.Position.Y + _swordHeight,
                        contact.Position.Z),
                    DueSeconds = _startLocalTime + contact.OffsetTicks / (double)CombatRules.TickRateHz
                });
            }

            int swordCount = Math.Max(SwordQiEchoRules.Spec.SwordCount, grouped.Count);
            for (int swordIndex = 0; swordIndex < swordCount; swordIndex++)
            {
                var route = GetRoute(swordIndex);
                if (grouped.TryGetValue(swordIndex, out var points))
                {
                    points.Sort((a, b) => a.DueSeconds.CompareTo(b.DueSeconds));
                    route.Points.AddRange(points);
                }

                route.FinishedAt = -1d;
                // 迟到消息：跳过已经过去的接触点，从当前时间继续播放。
                while (route.Next < route.Points.Count
                       && route.Points[route.Next].DueSeconds < _now())
                    route.Next++;

                route.Previous = StartPosition(swordIndex);
                route.Current = route.Previous;
                route.Root.transform.position = route.Previous;
                route.Trail.Clear();
                route.Trail.emitting = true;
                _active.Add(route);
            }
        }

        private SwordRoute GetRoute(int swordIndex)
        {
            SwordRoute? route = null;
            for (int i = _pool.Count - 1; i >= 0; i--)
            {
                if (_pool[i].SwordIndex != swordIndex) continue;
                route = _pool[i];
                _pool.RemoveAt(i);
                break;
            }

            if (route == null)
            {
                route = CreateRoute(swordIndex);
            }
            else
            {
                route.Points.Clear();
                route.Next = 0;
                route.Root.SetActive(true);
            }
            return route;
        }

        private SwordRoute CreateRoute(int swordIndex)
        {
            GameObject root;
            TrailRenderer? trail;
            if (_swordPrefab != null)
            {
                root = Instantiate(_swordPrefab, transform);
                trail = root.GetComponentInChildren<TrailRenderer>();
                if (trail == null) trail = root.AddComponent<TrailRenderer>();
            }
            else
            {
                root = new GameObject("SwordQiEchoSword_" + swordIndex);
                root.transform.SetParent(transform, false);
                trail = root.AddComponent<TrailRenderer>();
            }

            trail.time = _trailTime;
            trail.widthMultiplier = 0.08f;
            trail.minVertexDistance = 0.05f;
            trail.startColor = Color.white;
            trail.endColor = new Color(1f, 1f, 1f, 0f);
            if (trail.sharedMaterial == null)
                trail.sharedMaterial = GetFallbackTrailMaterial();

            return new SwordRoute
            {
                Root = root,
                Trail = trail,
                SwordIndex = swordIndex
            };
        }

        private Vector3 StartPosition(int swordIndex)
        {
            if (_owner == null) return Vector3.up * _swordHeight;
            float angle = swordIndex * (360f / 4f) + 45f;
            return _owner.position
                   + Quaternion.Euler(0f, angle, 0f) * _owner.forward * 0.6f
                   + Vector3.up * _swordHeight;
        }

        private Material GetFallbackTrailMaterial()
        {
            if (_fallbackTrailMaterial != null) return _fallbackTrailMaterial;
            var shader = Shader.Find("Sprites/Default");
            _fallbackTrailMaterial = new Material(shader) { color = Color.white };
            return _fallbackTrailMaterial;
        }
    }
}
