using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 3a 入口项：**场景里不得再有 UIManager / EventSystem 对象**。
    ///
    /// 这是 R8 唯一"能抓住原 bug"的守卫 —— `UIBootstrapTests` 做不到（它先建再断言，
    /// 属性被删也照样绿）。做法是先例：`RemoteEntityRegistryTests` 就是读场景文本的。
    ///
    /// 为什么急：阶段 3b 要重开 `DemoDay.unity` 做层序号重排，那正是可能把对象带回来的动作。
    /// </summary>
    public class UISceneContentTests
    {
        private static readonly string[] Scenes =
        {
            "Assets/Scenes/CharacterSelect.unity",
            "Assets/SimpleLowPolyNature/Scenes/DemoDay.unity",
        };

        private static readonly string[] ForbiddenNames = { "UIManager", "EventSystem" };

        [Test]
        public void test_no_scene_carries_its_own_ui_manager_or_event_system()
        {
            foreach (var scenePath in Scenes)
            {
                // 从仓库根解析：EditMode 测试的 cwd 就是项目根
                Assert.IsTrue(File.Exists(scenePath),
                    "找不到场景文件 " + scenePath + "（cwd = " + Directory.GetCurrentDirectory() + "）");

                var text = File.ReadAllText(scenePath);
                foreach (var name in ForbiddenNames)
                {
                    var pattern = @"m_Name:\s*" + Regex.Escape(name) + @"\s*$";
                    Assert.IsFalse(Regex.IsMatch(text, pattern, RegexOptions.Multiline),
                        scenePath + " 里还有名为 " + name + " 的对象 —— " +
                        "R8 的原 bug 就是场景各带一份，它该由 UIBootstrap 在 AfterSceneLoad 建");
                }
            }
        }
    }
}
