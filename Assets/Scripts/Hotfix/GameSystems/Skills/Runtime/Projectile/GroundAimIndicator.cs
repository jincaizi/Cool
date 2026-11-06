using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.Projectile
{
    /// <summary>地面落点圈占位表现：只负责位置、半径与颜色，不做判定。</summary>
    public class GroundAimIndicator : MonoBehaviour
    {
        [SerializeField] private Renderer _renderer;
        [SerializeField] private Color _normalColor = new Color(1f, 0.2f, 0.1f, 0.35f);
        [SerializeField] private Color _cancelColor = new Color(1f, 0f, 0f, 0.6f);

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _block;

        private void Awake()
        {
            if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
        }

        public void Show(Vector3 position, float radius, bool cancel)
        {
            gameObject.SetActive(true);
            transform.position = position + Vector3.up * 0.05f;
            transform.rotation = Quaternion.identity;
            transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);

            if (_renderer != null)
            {
                _block ??= new MaterialPropertyBlock();
                _renderer.GetPropertyBlock(_block);
                _block.SetColor(ColorId, cancel ? _cancelColor : _normalColor);
                _renderer.SetPropertyBlock(_block);
            }
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
