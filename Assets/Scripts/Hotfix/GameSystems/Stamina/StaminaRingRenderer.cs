using System;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.Stamina
{
    /// <summary>
    /// 角色右上方的精力环：运行时生成白环贴图，Image.Filled 径向填充。
    /// 由 EntityDisplayManager 持有；每帧 Tick 读取 StaminaSystem。
    /// </summary>
    public sealed class StaminaRingRenderer
    {
        private const float FadeInSeconds = 0.1f;
        private const float FadeOutSeconds = 0.25f;
        private const float FlashSeconds = 0.35f;
        private const int TextureResolution = 128;

        private static readonly Color FillColor = new Color(0.365f, 1f, 0.416f, 1f); // #5DFF6A
        private static readonly Color TrackColor = new Color(0.04f, 0.08f, 0.05f, 0.55f);
        private static readonly Color FlashColor = new Color(1f, 0.25f, 0.2f, 1f);

        private readonly Canvas _canvas;
        private readonly float _headHeight;
        private readonly Vector2 _offset;

        private readonly GameObject _root;
        private readonly RectTransform _rect;
        private readonly CanvasGroup _group;
        private readonly Image _track;
        private readonly Image _fill;
        private readonly Sprite _sprite;

        private Transform _owner;
        private StaminaSystem _stamina;
        private Func<bool> _visible;
        private float _alpha;
        private float _flashTimer;
        private bool _wasExhausted;
        private bool _active;

        public StaminaRingRenderer(Canvas canvas, float diameter = 64f, float ringWidth = 10f,
            Vector2? offset = null, float headHeight = 1.7f)
        {
            _canvas = canvas;
            _headHeight = Mathf.Max(0f, headHeight);
            _offset = offset ?? new Vector2(64f, 24f);

            float size = Mathf.Max(8f, diameter);
            float thickness = Mathf.Clamp(ringWidth, 1f, size * 0.45f);

            _root = new GameObject("StaminaRing", typeof(RectTransform));
            _root.transform.SetParent(canvas.transform, false);

            _rect = (RectTransform)_root.transform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.sizeDelta = new Vector2(size, size);

            _group = _root.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            _sprite = CreateRingSprite(size, thickness);
            _track = CreateImage("Track", Image.Type.Simple, TrackColor);
            _fill = CreateImage("Fill", Image.Type.Filled, FillColor);
            _fill.fillMethod = Image.FillMethod.Radial360;
            _fill.fillOrigin = (int)Image.Origin360.Top;
            _fill.fillClockwise = false;
            _fill.fillAmount = 1f;

            _root.SetActive(false);
        }

        public float FillAmount => _fill != null ? _fill.fillAmount : 0f;
        public bool IsVisible => _active && _alpha > 0.001f;
        public float Alpha => _alpha;
        public bool IsFlashing => _flashTimer > 0f;

        public void Bind(Transform owner, StaminaSystem stamina, Func<bool> visible)
        {
            _owner = owner;
            _stamina = stamina;
            _visible = visible;

            _alpha = 0f;
            _flashTimer = 0f;
            _wasExhausted = stamina != null && stamina.IsExhausted;
            _group.alpha = 0f;
            _fill.color = FillColor;
            _track.color = TrackColor;
            _fill.fillAmount = stamina != null ? stamina.Ratio : 0f;
            _root.SetActive(false);
            _active = false;
        }

        public void Unbind(Transform owner)
        {
            if (_owner != owner) return;
            _owner = null;
            _stamina = null;
            _visible = null;
            HideImmediate();
        }

        public void Tick(Camera camera, float deltaTime)
        {
            // 先判空/谓词，再访问任何可能已销毁的对象
            if (_owner == null || _stamina == null || camera == null
                || (_visible != null && !_visible()))
            {
                HideImmediate();
                return;
            }

            Vector3 world = _owner.position + Vector3.up * _headHeight;
            Vector3 screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0f
                || screen.x < 0f || screen.x > Screen.width
                || screen.y < 0f || screen.y > Screen.height)
            {
                HideImmediate();
                return;
            }

            float dt = !float.IsNaN(deltaTime) && !float.IsInfinity(deltaTime)
                ? Mathf.Max(0f, deltaTime)
                : 0f;

            float ratio = _stamina.Ratio;
            bool notFull = ratio < 1f - 0.001f;
            float targetAlpha = notFull ? 1f : 0f;
            float duration = targetAlpha > _alpha ? FadeInSeconds : FadeOutSeconds;
            _alpha = Mathf.MoveTowards(_alpha, targetAlpha, duration > 0f ? dt / duration : 1f);

            if (_stamina.IsExhausted && !_wasExhausted)
                _flashTimer = FlashSeconds;
            _wasExhausted = _stamina.IsExhausted;
            if (_flashTimer > 0f) _flashTimer = Mathf.Max(0f, _flashTimer - dt);

            if (_alpha > 0f)
            {
                if (!_root.activeSelf)
                {
                    _root.SetActive(true);
                    _root.transform.SetAsLastSibling();
                }
                _active = true;
            }
            else
            {
                _root.SetActive(false);
                _active = false;
                return;
            }

            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            _rect.position = screen + new Vector3(_offset.x * scale, _offset.y * scale, 0f);
            _fill.fillAmount = ratio;
            // 力竭瞬间 ratio=0，Fill 被缩成 0 像素；只染 Fill 的闪烁不可见。
            // Track 是完整环，闪烁期间两环一起染红才能看到整环红闪。
            bool flashing = _flashTimer > 0f;
            _fill.color = flashing ? FlashColor : FillColor;
            _track.color = flashing ? FlashColor : TrackColor;
            _group.alpha = _alpha;
        }

        public void Cleanup()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_sprite != null)
            {
                var texture = _sprite.texture;
                UnityEngine.Object.Destroy(_sprite);
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
        }

        private void HideImmediate()
        {
            _alpha = 0f;
            _flashTimer = 0f;
            _active = false;
            if (_group != null) _group.alpha = 0f;
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            if (_stamina != null) _wasExhausted = _stamina.IsExhausted;
        }

        private Image CreateImage(string name, Image.Type type, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var image = go.AddComponent<Image>();
            image.sprite = _sprite;
            image.type = type;
            image.color = color;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return image;
        }

        private static Sprite CreateRingSprite(float diameter, float ringWidth)
        {
            float thickness = TextureResolution * (ringWidth / diameter);
            float outer = TextureResolution * 0.5f - 1f;
            float inner = Mathf.Max(0f, outer - thickness);

            var texture = new Texture2D(TextureResolution, TextureResolution,
                TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[TextureResolution * TextureResolution];
            for (int y = 0; y < TextureResolution; y++)
            {
                for (int x = 0; x < TextureResolution; x++)
                {
                    float dx = x + 0.5f - TextureResolution * 0.5f;
                    float dy = y + 0.5f - TextureResolution * 0.5f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(outer - distance + 0.5f)
                                * Mathf.Clamp01(distance - inner + 0.5f);
                    pixels[y * TextureResolution + x] =
                        new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture,
                new Rect(0f, 0f, TextureResolution, TextureResolution),
                new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
