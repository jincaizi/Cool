using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace Hotfix.GameSystems.Nameplate
{
    public class FloatingTextPool : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset _fontAsset;
        [SerializeField] private Material _fontMaterial;
        [SerializeField] private int _preWarmCount = 10;

        private readonly Stack<TextMeshProUGUI> _free = new Stack<TextMeshProUGUI>();
        private readonly HashSet<TextMeshProUGUI> _active = new HashSet<TextMeshProUGUI>();
        private Canvas _canvas;
        private Camera _camera;
        private const int GrowSize = 10;

        public static FloatingTextPool Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _camera = Camera.main;
            CreateCanvas();
            PreWarm(_preWarmCount);
        }

        private void CreateCanvas()
        {
            var go = new GameObject("FloatingTextCanvas");
            go.transform.SetParent(transform);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 4500;
            go.AddComponent<UnityEngine.UI.CanvasScaler>();
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        private void PreWarm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var tmp = CreateTMP();
                tmp.gameObject.SetActive(false);
                _free.Push(tmp);
            }
        }

        private TextMeshProUGUI CreateTMP()
        {
            var go = new GameObject("FloatingText");
            go.transform.SetParent(_canvas.transform, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (_fontAsset != null) tmp.font = _fontAsset;
            if (_fontMaterial != null) tmp.fontMaterial = _fontMaterial;
            tmp.raycastTarget = false;
            tmp.alpha = 1f;
            return tmp;
        }

        private void Grow()
        {
            for (int i = 0; i < GrowSize; i++)
                _free.Push(CreateTMP());
        }

        public void Spawn(Vector3 worldPos, string text, FloatingTextConfig config)
        {
            if (_free.Count == 0) Grow();

            var tmp = _free.Pop();
            _active.Add(tmp);

            tmp.gameObject.SetActive(true);
            tmp.text = text;
            tmp.color = config.Color;
            tmp.fontSize = config.FontSize;
            tmp.alpha = 1f;

            // Position: WorldToScreenPoint
            if (_camera != null)
            {
                var screenPos = _camera.WorldToScreenPoint(worldPos);
                var rt = tmp.rectTransform;
                rt.position = screenPos;
                rt.localScale = Vector3.one * config.StartScale;
            }

            // DOTween animation
            var rt2 = tmp.rectTransform;
            var startY = rt2.anchoredPosition.y;
            var seq = DOTween.Sequence();

            if (config.PunchScale)
            {
                seq.Join(rt2.DOPunchScale(Vector3.one * 0.3f, config.Duration, 1, 0f));
            }
            if (config.StartScale != 1f)
            {
                rt2.localScale = Vector3.one * config.StartScale;
                seq.Join(rt2.DOScale(1f, config.Duration * 0.3f).SetEase(Ease.OutBack));
            }

            seq.Join(rt2.DOAnchorPosY(startY + config.MoveUpDistance, config.Duration)
                .SetEase(config.Ease));
            seq.Join(tmp.DOFade(0f, config.Duration * 0.7f).SetDelay(config.Duration * 0.3f));

            seq.OnKill(() =>
            {
                _active.Remove(tmp);
                tmp.alpha = 1f;
                tmp.text = "";
                rt2.localScale = Vector3.one;
                tmp.gameObject.SetActive(false);
                _free.Push(tmp);
            });

            seq.SetTarget(tmp.transform);
        }
    }
}
