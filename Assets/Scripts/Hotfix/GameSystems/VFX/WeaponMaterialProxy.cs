using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class WeaponMaterialProxy : MonoBehaviour
    {
        [SerializeField] private string _weaponBonePath = "weapon_r";

        private Renderer _weaponRenderer;
        private MaterialPropertyBlock _propBlock;

        private bool _dirty;
        private Color _glowEdgeColor = Color.black;
        private float _glowIntensity;
        private Color _frostColor = Color.white;
        private float _frostAmount;
        private float _frostFlowSpeed;

        private static readonly int EdgeColorId     = Shader.PropertyToID("_EdgeColor");
        private static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
        private static readonly int FrostColorId    = Shader.PropertyToID("_FrostColor");
        private static readonly int FrostAmountId   = Shader.PropertyToID("_FrostAmount");
        private static readonly int FrostFlowSpeedId = Shader.PropertyToID("_FrostFlowSpeed");

        public Renderer WeaponRenderer => _weaponRenderer;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            var t = transform.Find(_weaponBonePath);
            if (t != null)
            {
                _weaponRenderer = t.GetComponent<Renderer>();
                if (_weaponRenderer == null)
                    _weaponRenderer = t.GetComponentInChildren<Renderer>();
            }
            if (_weaponRenderer == null)
            {
                var allRenderers = GetComponentsInChildren<Renderer>();
                foreach (var r in allRenderers)
                {
                    var nm = r.name.ToLower();
                    if (nm.Contains("weapon") || nm.Contains("sword"))
                    { _weaponRenderer = r; break; }
                }
                if (_weaponRenderer == null && allRenderers.Length > 0)
                    _weaponRenderer = allRenderers[0];
            }
        }

        public void SetGlow(Color edgeColor, float intensity)
        {
            _glowEdgeColor = edgeColor;
            _glowIntensity = intensity;
            _dirty = true;
        }

        public void SetFrost(Color color, float amount, float flowSpeed)
        {
            _frostColor = color;
            _frostAmount = amount;
            _frostFlowSpeed = flowSpeed;
            _dirty = true;
        }

        public void Apply()
        {
            if (_weaponRenderer == null || !_dirty) return;
            _weaponRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(EdgeColorId, _glowEdgeColor);
            _propBlock.SetFloat(GlowIntensityId, _glowIntensity);
            _propBlock.SetColor(FrostColorId, _frostColor);
            _propBlock.SetFloat(FrostAmountId, _frostAmount);
            _propBlock.SetFloat(FrostFlowSpeedId, _frostFlowSpeed);
            _weaponRenderer.SetPropertyBlock(_propBlock);
            _dirty = false;
        }

        private void LateUpdate()
        {
            Apply();
        }
    }
}
