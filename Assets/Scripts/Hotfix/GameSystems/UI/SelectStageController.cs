using Cysharp.Threading.Tasks;
using Hotfix.GameSystems.Sys3C.Character;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 选人舞台控制器：切换预览体展示所选性别；面板关闭后加载游戏场景。
    /// 全序列化引用，无 GetComponent。
    /// </summary>
    public class SelectStageController : MonoBehaviour
    {
        [SerializeField] private GameObject _malePreview;
        [SerializeField] private GameObject _femalePreview;

        private bool _loading;   // 双保险：防重复加载场景
        private CharacterSelectPanel _panel;

        private async void Start()
        {
            var ui = UIManager.Instance;
            if (ui == null)
            {
                Debug.LogError("SelectStageController: 场景里没有 UIManager");
                return;
            }

            // 阶段 2：等就绪信号，不再轮询。WhenReady 必然完成 ⇒ 不会挂死；
            // 面板没起来时下面那条 null 检查会明确报出来（而不是原来的无限空转）。
            await ui.WhenReady();

            _panel = ui.GetPanel<CharacterSelectPanel>();
            if (_panel == null)
            {
                Debug.LogError("SelectStageController: CharacterSelectPanel 未注册");
                return;
            }

            ApplyPreview(CharacterSelectionService.Load());
            _panel.OnSelectionChanged += ApplyPreview;
            _panel.OnClosed += LoadGameScene;
        }

        private void ApplyPreview(CharacterGender gender)
        {
            if (_malePreview != null) _malePreview.SetActive(gender == CharacterGender.Male);
            if (_femalePreview != null) _femalePreview.SetActive(gender == CharacterGender.Female);
        }

        private void LoadGameScene()
        {
            if (_loading) return;
            _loading = true;
            SceneManager.LoadScene("DemoDay");
        }

        private void OnDestroy()
        {
            if (_panel != null)
            {
                _panel.OnSelectionChanged -= ApplyPreview;
                _panel.OnClosed -= LoadGameScene;
            }
        }
    }
}
