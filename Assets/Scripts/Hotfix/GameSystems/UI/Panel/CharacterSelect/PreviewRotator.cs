using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 选人预览体慢速自转（纯 Transform 操作，无 GetComponent）
    /// </summary>
    public class PreviewRotator : MonoBehaviour
    {
        [SerializeField] private float _speed = 30f;

        private void Update()
        {
            transform.Rotate(0f, _speed * Time.deltaTime, 0f);
        }
    }
}
