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
            // UIManager.Awake 异步注册面板（await Resources.Load），轮询等待
            while (UIManager.Instance == null)
                await UniTask.Delay(50);
            while (UIManager.Instance.GetPanel<CharacterSelectPanel>("CharacterSelectPanel") == null)
                await UniTask.Delay(50);

            _panel = UIManager.Instance.GetPanel<CharacterSelectPanel>("CharacterSelectPanel");
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
