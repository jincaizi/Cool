using DataDefinition;
using TMPro;
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 全局构建版本标签。挂在 Top 层，常驻右下角；不拦输入。
    /// </summary>
    public class BuildInfoPanel : UIPanel
    {
        public override LayerType Layer => LayerType.Top;
        public override VisibilityMode Mode => VisibilityMode.CanvasGroup;

        [SerializeField] private TMP_Text _versionText;

        protected override void Awake()
        {
            base.Awake();
            if (_versionText == null)
            {
                Debug.LogError("[BuildInfoPanel] _versionText 未绑定，版本号不会显示");
                return;
            }

            _versionText.text = BuildVersion.Display;
            Debug.Log("[Build] version=" + BuildVersion.Raw);
        }
    }
}
