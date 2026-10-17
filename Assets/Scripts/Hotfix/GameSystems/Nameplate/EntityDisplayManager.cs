using System;
using UnityEngine;
using UnityEngine.UI;
using DataDefinition;
using Hotfix.GameSystems.Stamina;

namespace Hotfix.GameSystems.Nameplate
{
    public class EntityDisplayManager : MonoBehaviour
    {
        [SerializeField] private NameplateSettings _nameplateSettings;
        [SerializeField] private FloatTextSettings _damageSettings;
        [SerializeField] private FloatTextSettings _critDamageSettings;
        [SerializeField] private FloatTextSettings _playerDamageSettings;
        [SerializeField] private FloatTextSettings _skillNameSettings;

        [Header("Stamina Ring")]
        [SerializeField] private float _staminaRingDiameter = 64f;
        [SerializeField] private float _staminaRingWidth = 10f;
        [SerializeField] private Vector2 _staminaRingOffset = new Vector2(64f, 24f);
        [SerializeField] private float _staminaRingHeadHeight = 1.7f;

        private Canvas _canvas;
        private Camera _camera;
        private NameplateRenderer _nameplate;
        private FloatTextRenderer _floatText;
        private DisplayEventBridge _eventBridge;
        private DamageScreenEffect _damageScreenEffect;
        private StaminaRingRenderer _staminaRing;

        public static EntityDisplayManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _camera = Camera.main;

            CreateCanvas();

            _nameplate = new NameplateRenderer(_nameplateSettings, _canvas.transform);
            _floatText = new FloatTextRenderer(_canvas.transform);
            _damageScreenEffect = new DamageScreenEffect(_canvas.transform);
            _floatText.Camera = _camera;

            _eventBridge = new DisplayEventBridge(
                _floatText,
                _damageSettings, _critDamageSettings, _playerDamageSettings);
        }

        private void CreateCanvas()
        {
            var go = new GameObject("EntityDisplayCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 4500;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = DataDefinition.GameSettings.Instance.ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
        }

        private void OnEnable()
        {
            _eventBridge?.Enable();
        }

        private void OnDisable()
        {
            _eventBridge?.Disable();
        }

        private void LateUpdate()
        {
            if (_camera == null) { _camera = Camera.main; if (_camera == null) return; }
            _floatText.Camera = _camera;
            _nameplate.Tick(_camera);
            _staminaRing?.Tick(_camera, Time.deltaTime);
            _floatText.PurgeExpiredMerges();
        }

        private void OnDestroy()
        {
            _nameplate?.Cleanup();
            _floatText?.Cleanup();
            _damageScreenEffect?.Cleanup();
            _staminaRing?.Cleanup();
        }

        // ===== Nameplate API =====

        public void Register(int entityId, Transform owner, NameplateConfig config)
        {
            _nameplate.Register(entityId, owner, config);
        }

        public void Unregister(int entityId)
        {
            _nameplate.Unregister(entityId);
        }

        public void UpdateName(int entityId, string newName)
        {
            _nameplate.UpdateName(entityId, newName);
        }

        public void SetNameplateVisible(int entityId, bool visible)
        {
            _nameplate.SetVisible(entityId, visible);
        }

        // ===== Float Text API =====

        public void ShowFloatingText(Vector3 worldPos, FloatTextSettings settings, string text)
        {
            _floatText.ShowFloatingText(worldPos, settings, text);
        }

        public void ShowDamageText(int entityId, Vector3 worldPos, FloatTextSettings settings, int value)
        {
            _floatText.ShowDamageText(entityId, worldPos, settings, value);
        }

        // ===== Stamina Ring API =====

        public void BindStamina(Transform owner, StaminaSystem stamina, Func<bool> visible)
        {
            if (_staminaRing == null)
            {
                _staminaRing = new StaminaRingRenderer(
                    _canvas, _staminaRingDiameter, _staminaRingWidth,
                    _staminaRingOffset, _staminaRingHeadHeight);
            }
            _staminaRing.Bind(owner, stamina, visible);
        }

        public void UnbindStamina(Transform owner)
        {
            _staminaRing?.Unbind(owner);
        }
    }
}
