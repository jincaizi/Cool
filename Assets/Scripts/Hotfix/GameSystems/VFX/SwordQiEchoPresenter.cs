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
            public Renderer[] Renderers = null!;
            public Color[] Colors = null!;
            public Vector3 Scale;
            public int SwordIndex;
            public readonly List<ScheduledPoint> Points = new List<ScheduledPoint>();
            public int Next;
            public Vector3 Previous;
            public Vector3 Current;
            public Vector3 LastPoint;
            public Vector3 Approach;
            public double FlightStart;
            public double FinishedAt = -1d;
        }

        [SerializeField] private GameObject? _swordPrefab;
        [SerializeField] private float _trailTime = 0.18f;
        [SerializeField] private float _swordHeight = 1.1f;
        [SerializeField] private float _orbitHeight = 0.62f;
        [SerializeField] private float _idleRadius = 1.5f;

        private readonly List<SwordRoute> _active = new List<SwordRoute>();
        private readonly List<SwordRoute> _pool = new List<SwordRoute>();
        private Transform? _owner;
        private double _startLocalTime;
        private double _endLocalTime;
        private double _orbitStart;
        private double _collapseAt;
        private Func<double> _now = () => Time.time;
        private Material? _fallbackTrailMaterial;
        private GameObject? _activeSwordPrefab;
        private bool _playing;
        private bool _preparing;
        private bool _returnAfterFlight;
        private const double FacingSampleSeconds = 0.012d;
        private static readonly int TintId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock? _fadeProperties;

        public bool IsPlaying => _playing;
        public int ActiveSwordCount => _active.Count;

        public Vector3 SwordPosition(int index)
            => index >= 0 && index < _active.Count ? _active[index].Current : Vector3.zero;

        public void Play(Transform owner, IReadOnlyList<SwordQiEchoContact> contacts,
            float durationSeconds, double elapsedSeconds = 0d, Func<double>? now = null,
            GameObject? swordPrefab = null)
            => BeginFlight(owner, contacts, durationSeconds, SwordQiEchoRules.Spec.SwordCount,
                elapsedSeconds, now, swordPrefab, returnAfterFlight: false);

        public void Prepare(Transform owner, int swordCount, double durationSeconds,
            double elapsedSeconds = 0d, Func<double>? now = null, GameObject? swordPrefab = null)
        {
            var prefab = swordPrefab != null ? swordPrefab : _swordPrefab;
            bool continuing = _playing && _preparing && _owner == owner && prefab == _activeSwordPrefab
                && _active.Count == swordCount;
            if (!continuing)
                Initialize(owner, prefab, now);
            if (_owner == null) return;

            _preparing = true;
            _returnAfterFlight = true;
            _collapseAt = _now() + durationSeconds - Math.Max(0d, elapsedSeconds);
            _endLocalTime = _collapseAt + SwordQiEchoRules.DissolveSeconds;
            if (!continuing)
            {
                _orbitStart -= Math.Max(0d, elapsedSeconds);
                BuildRoutes(Array.Empty<SwordQiEchoContact>(), swordCount);
            }
            Tick(_now());
        }

        public void Launch(Transform owner, IReadOnlyList<SwordQiEchoContact> contacts, float durationSeconds,
            int swordCount, double elapsedSeconds = 0d, Func<double>? now = null, GameObject? swordPrefab = null)
            => BeginFlight(owner, contacts, durationSeconds, swordCount, elapsedSeconds, now, swordPrefab,
                returnAfterFlight: true);

        private void BeginFlight(Transform owner, IReadOnlyList<SwordQiEchoContact> contacts,
            float durationSeconds, int swordCount, double elapsedSeconds, Func<double>? now,
            GameObject? swordPrefab, bool returnAfterFlight,
            (Vector3 Origin, double OrbitAge)? release = null)
        {
            var prefab = swordPrefab != null ? swordPrefab : _swordPrefab;
            bool continuing = returnAfterFlight && _playing && _preparing && _owner == owner
                && prefab == _activeSwordPrefab && _active.Count == swordCount;
            if (!continuing)
                Initialize(owner, prefab, now);
            if (_owner == null) return;

            _preparing = false;
            _returnAfterFlight = returnAfterFlight;
            _startLocalTime = _now() - Math.Max(0d, elapsedSeconds);
            if (release.HasValue) _orbitStart = _startLocalTime - release.Value.OrbitAge;
            _collapseAt = _startLocalTime + Math.Max(0.1f, durationSeconds);
            if (returnAfterFlight) _collapseAt += SwordQiEchoRules.FinishOrbitDelaySeconds;
            _endLocalTime = _collapseAt + (returnAfterFlight ? SwordQiEchoRules.DissolveSeconds : 0d);
            BuildRoutes(contacts, swordCount, continuing, release);
            Tick(_now());
        }

        private void Initialize(Transform owner, GameObject? prefab, Func<double>? clock)
        {
            Stop();
            if (owner == null) return;
            if (_activeSwordPrefab != prefab)
            {
                foreach (var route in _pool)
                {
                    if (Application.isPlaying) Destroy(route.Root);
                    else DestroyImmediate(route.Root);
                }
                _pool.Clear();
                _activeSwordPrefab = prefab;
            }

            _owner = owner;
            _now = clock ?? (() => Time.timeAsDouble);
            _orbitStart = _now();
            _playing = true;
        }

        public void Play(Transform owner, SwordQiEchoState state,
            double elapsedSeconds = 0d, Func<double>? now = null, GameObject? swordPrefab = null)
        {
            if (state == null) return;
            var contacts = new List<SwordQiEchoContact>(state.Contacts.Count);
            foreach (var contact in state.Contacts)
            {
                contacts.Add(new SwordQiEchoContact(contact.TargetId, contact.SwordIndex,
                    contact.OffsetTicks, new Vec3(contact.X, contact.Y, contact.Z)));
            }

            double duration = unchecked(state.EndTick - state.StartTick) / (double)CombatRules.TickRateHz;
            int swordCount = state.SwordCount > 0 ? state.SwordCount : SwordQiEchoRules.Spec.SwordCount;
            if (state.Phase == SwordQiEchoPhase.Prepared)
                Prepare(owner, swordCount, duration, elapsedSeconds, now, swordPrefab);
            else
            {
                double orbitAgeAtRelease = state.OrbitStartedTimestamp == 0 ? 0d
                    : Math.Max(0d, (state.ServerTimestamp - state.OrbitStartedTimestamp) / (double)TimeSpan.TicksPerSecond);
                BeginFlight(owner, contacts, (float)duration, swordCount, elapsedSeconds, now, swordPrefab,
                    returnAfterFlight: true,
                    release: (new Vector3(state.OriginX, state.OriginY, state.OriginZ), orbitAgeAtRelease));
            }
        }

        internal static void PlayContact(GameObject prefab, Vector3 point)
        {
            if (prefab == null || !Application.isPlaying) return;
            var effect = Instantiate(prefab, point, Quaternion.identity);
            Destroy(effect, 0.35f);
        }

        public void Stop()
        {
            _playing = false;
            _preparing = false;
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
            if (now >= _endLocalTime || _owner == null)
            {
                Stop();
                return;
            }

            float collapse = _returnAfterFlight
                ? Mathf.SmoothStep(0f, 1f, (float)((now - _collapseAt) / SwordQiEchoRules.DissolveSeconds))
                : 0f;
            for (int i = 0; i < _active.Count; i++)
            {
                var route = _active[i];
                Vector3 heading = Vector3.zero;
                // 到点后把“上一位置”推进到目标点；未到点的轨迹向下一目标插值。
                while (route.Next < route.Points.Count && now >= route.Points[route.Next].DueSeconds)
                {
                    route.Approach = (route.Points[route.Next].Position - route.Previous).normalized;
                    route.Previous = route.Points[route.Next].Position;
                    route.Current = route.Previous;
                    route.LastPoint = route.Previous;
                    route.Next++;
                    if (route.Next >= route.Points.Count)
                        route.FinishedAt = route.Points[route.Next - 1].DueSeconds;
                }

                if (route.Next < route.Points.Count)
                {
                    var target = route.Points[route.Next];
                    double from = route.Next == 0 ? route.FlightStart : route.Points[route.Next - 1].DueSeconds;
                    double span = Math.Max(0.0001d, target.DueSeconds - from);
                    float t = Mathf.Clamp01((float)((now - from) / span));
                    route.Current = Vector3.Lerp(route.Previous, target.Position, t);
                    if (_returnAfterFlight)
                    {
                        route.Current += Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.25f);
                        heading = target.Position - route.Previous
                            + Vector3.up * (Mathf.Cos(t * Mathf.PI) * (Mathf.PI * 0.25f));
                    }
                }
                else if (!_returnAfterFlight && route.FinishedAt >= 0d && now - route.FinishedAt < 0.15d)
                {
                    // 接触后短暂保持穿透点，避免到点立刻跳回回旋轨迹。
                    route.Current = route.LastPoint;
                }
                else
                {
                    Vector3 orbit = OrbitPoint(route.SwordIndex, now, collapse);
                    if (_returnAfterFlight)
                    {
                        double nextTime = now + FacingSampleSeconds;
                        Vector3 next = OrbitPoint(route.SwordIndex, nextTime, collapse);
                        if (route.FinishedAt >= 0d)
                        {
                            orbit = ReturnToOrbit(route, orbit, (float)(now - route.FinishedAt));
                            next = ReturnToOrbit(route, next, (float)(nextTime - route.FinishedAt));
                        }
                        heading = next - orbit;
                    }
                    route.Current = orbit;
                }

                if (collapse > 0f) SetOpacity(route, 1f - collapse);
                Vector3 delta = _returnAfterFlight ? heading : route.Current - route.Root.transform.position;
                route.Root.transform.position = route.Current;
                if (delta.sqrMagnitude > 0.0001f)
                    route.Root.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            }
        }

        private Vector3 OrbitPoint(int index, double now, float collapse)
            => OrbitPoint(_owner!.position, index, _active.Count, (float)(now - _orbitStart), collapse);

        private Vector3 OrbitPoint(Vector3 center, int index, int count, float age, float collapse)
        {
            float angle = age * 125f + index * (360f / count) + 45f;
            float intro = Mathf.SmoothStep(0.45f, 1f, age / 0.28f);
            float radius = _idleRadius * intro * (1f - collapse);
            return center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius
                + Vector3.up * (_orbitHeight + Mathf.Sin(angle * Mathf.Deg2Rad) * 0.035f * (1f - collapse));
        }

        private static Vector3 ReturnToOrbit(SwordRoute route, Vector3 orbit, float elapsed)
        {
            const float passSeconds = 0.10f;
            Vector3 through = route.LastPoint + route.Approach * SwordQiEchoRules.Spec.OvershootMeters;
            if (elapsed <= passSeconds)
                return Vector3.Lerp(route.LastPoint, through, elapsed / passSeconds);
            float t = Mathf.SmoothStep(0f, 1f,
                (elapsed - passSeconds) / (SwordQiEchoRules.ReturnSeconds - passSeconds));
            return Vector3.Lerp(through, orbit, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.36f);
        }

        private void SetOpacity(SwordRoute route, float opacity)
        {
            _fadeProperties ??= new MaterialPropertyBlock();
            route.Root.transform.localScale = route.Scale * Mathf.Lerp(0.75f, 1f, opacity);
            for (int i = 0; i < route.Renderers.Length; i++)
            {
                var renderer = route.Renderers[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(_fadeProperties);
                Color color = route.Colors[i];
                color.a *= opacity;
                _fadeProperties.SetColor(TintId, color);
                renderer.SetPropertyBlock(_fadeProperties);
            }
        }

        private void Update() => Tick(_now());

        private void OnDisable() => Stop();

        private void BuildRoutes(IReadOnlyList<SwordQiEchoContact> contacts, int swordCount, bool continuing = false,
            (Vector3 Origin, double OrbitAge)? release = null)
        {
            var grouped = new Dictionary<int, List<ScheduledPoint>>();
            foreach (var contact in contacts)
            {
                if (contact.SwordIndex < 0 || contact.SwordIndex >= swordCount) continue;
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

            for (int swordIndex = 0; swordIndex < swordCount; swordIndex++)
            {
                var route = continuing ? _active[swordIndex] : GetRoute(swordIndex);
                route.Points.Clear();
                route.Next = 0;
                if (grouped.TryGetValue(swordIndex, out var points))
                {
                    points.Sort((a, b) => a.DueSeconds.CompareTo(b.DueSeconds));
                    route.Points.AddRange(points);
                }

                route.FinishedAt = -1d;
                // 迟到消息：跳过已经过去的接触点，从当前时间继续播放。
                while (!release.HasValue && route.Next < route.Points.Count
                       && route.Points[route.Next].DueSeconds < _now())
                    route.Next++;

                route.FlightStart = release.HasValue ? _startLocalTime : Math.Max(_startLocalTime, _now());
                route.Previous = release.HasValue
                    ? OrbitPoint(release.Value.Origin, swordIndex, swordCount, (float)release.Value.OrbitAge, 0f)
                    : continuing ? route.Current : StartPosition(swordIndex, swordCount);
                route.Current = route.Previous;
                Vector3 direction = route.Next < route.Points.Count
                    ? route.Points[route.Next].Position - route.Previous
                    : Vector3.Cross(Vector3.up, route.Previous - _owner!.position);
                if (direction.sqrMagnitude > 0.0001f)
                    route.Root.transform.SetPositionAndRotation(route.Previous, Quaternion.LookRotation(direction));
                else
                    route.Root.transform.SetPositionAndRotation(route.Previous, _owner!.rotation);
                route.Trail.emitting = true;
                if (!continuing)
                {
                    route.Trail.Clear();
                    _active.Add(route);
                }
                else if (release.HasValue && route.Points.Count > 0)
                {
                    route.Trail.Clear();
                }
                SetOpacity(route, 1f);
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
            if (_activeSwordPrefab != null)
            {
                root = Instantiate(_activeSwordPrefab, transform);
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
            if (_activeSwordPrefab == null)
                trail.widthMultiplier = 0.08f;
            trail.minVertexDistance = 0.05f;
            trail.startColor = Color.white;
            trail.endColor = new Color(1f, 1f, 1f, 0f);
            if (trail.sharedMaterial == null)
                trail.sharedMaterial = GetFallbackTrailMaterial();

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var colors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var material = renderers[i].sharedMaterial;
                colors[i] = material != null && material.HasProperty(TintId)
                    ? material.GetColor(TintId) : Color.white;
            }
            return new SwordRoute
            {
                Root = root,
                Trail = trail,
                Renderers = renderers,
                Colors = colors,
                Scale = root.transform.localScale,
                SwordIndex = swordIndex
            };
        }

        private Vector3 StartPosition(int swordIndex, int swordCount)
        {
            if (_owner == null) return Vector3.up * _orbitHeight;
            float angle = swordIndex * (360f / swordCount) + 45f;
            return _owner.position
                   + Quaternion.Euler(0f, angle, 0f) * _owner.forward * 0.6f
                   + Vector3.up * _orbitHeight;
        }

        private Material GetFallbackTrailMaterial()
        {
            if (_fallbackTrailMaterial != null) return _fallbackTrailMaterial;
            var shader = Shader.Find("Sprites/Default");
            _fallbackTrailMaterial = new Material(shader) { color = Color.white };
            return _fallbackTrailMaterial;
        }

        private void OnDestroy()
        {
            if (_fallbackTrailMaterial == null) return;
            if (Application.isPlaying) Destroy(_fallbackTrailMaterial);
            else DestroyImmediate(_fallbackTrailMaterial);
        }
    }
}
