using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI.Components
{
    /// <summary>
    /// Loading mask component.
    /// Shows blocking overlay with optional tips text.
    /// </summary>
    public class Loading : MonoBehaviour
    {
        [SerializeField] private RectTransform _rect;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Image _background;
        [SerializeField] private Image _spinner;
        [SerializeField] private Text _tipsText;
        [SerializeField] private Transform _spinnerTransform;

        private static Loading _instance;
        private int _loadingCount;
        private Tween _spinTween;

        public static Loading Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = CreateInstance();
                }
                return _instance;
            }
        }

        private static Loading CreateInstance()
        {
            var go = new GameObject("Loading");
            var loading = go.AddComponent<Loading>();
            loading.CreateLayout();

            DontDestroyOnLoad(loading.gameObject);
            loading.gameObject.SetActive(false);
            return loading;
        }

        private void CreateLayout()
        {
            _rect = gameObject.AddComponent<RectTransform>();
            _rect.StretchParent();

            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            _background = CreateImage("Background");
            _background.color = new Color(0, 0, 0, 0.5f);
            _background.raycastTarget = true;

            var content = new GameObject("Content");
            content.transform.SetParent(transform);

            _spinner = CreateImage("Spinner", content.transform);
            _spinner.color = Color.white;
            var spinnerRect = _spinner.GetComponent<RectTransform>();
            spinnerRect.anchoredPosition = Vector2.zero;
            spinnerRect.sizeDelta = new Vector2(60, 60);

            _tipsText = CreateText("Tips", content.transform);
            _tipsText.fontSize = 24;
            _tipsText.color = Color.white;
            _tipsText.alignment = TextAnchor.MiddleCenter;
            var tipsRect = _tipsText.GetComponent<RectTransform>();
            tipsRect.anchoredPosition = new Vector2(0, -60);
            tipsRect.sizeDelta = new Vector2(300, 40);

            _spinnerTransform = _spinner.transform;
        }

        private Image CreateImage(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? transform);
            var img = go.AddComponent<Image>();
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.StretchParent();
            return img;
        }

        private Text CreateText(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? transform);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return text;
        }

        public static void Show(string tips = null)
        {
            Instance.ShowLoading(tips);
        }

        public static void Hide()
        {
            Instance.HideLoading();
        }

        private void ShowLoading(string tips)
        {
            _loadingCount++;

            if (_loadingCount == 1)
            {
                gameObject.SetActive(true);
                _canvasGroup.blocksRaycasts = true;

                if (!string.IsNullOrEmpty(tips))
                {
                    _tipsText.text = tips;
                    _tipsText.gameObject.SetActive(true);
                }
                else
                {
                    _tipsText.gameObject.SetActive(false);
                }

                _canvasGroup.FadeIn(0.2f);
                StartSpinAnimation();
            }
        }

        private void HideLoading()
        {
            _loadingCount = Math.Max(0, _loadingCount - 1);

            if (_loadingCount == 0)
            {
                StopSpinAnimation();
                _canvasGroup.FadeOut(0.2f, () =>
                {
                    if (_loadingCount == 0)
                    {
                        gameObject.SetActive(false);
                    }
                });
            }
        }

        private void StartSpinAnimation()
        {
            _spinTween = _spinnerTransform.DOLocalRotate(Vector3.forward * -360f, 1f, RotateMode.LocalAxisAdd)
                .SetEase(Ease.Linear)
                .SetLoops(-1);
        }

        private void StopSpinAnimation()
        {
            _spinTween?.Kill();
            _spinnerTransform.localRotation = Quaternion.identity;
        }

        private void OnDestroy()
        {
            _spinTween?.Kill();
        }
    }
}
