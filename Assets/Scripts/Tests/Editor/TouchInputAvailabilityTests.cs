using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    /// <summary>
    /// Task 6 修复轮：触屏判定的可测部分。
    /// `Application.isMobilePlatform` 单独不可靠（iPad 桌面视图会报 false），因此判定叠了
    /// "URL 参数 > 人工档位 > 自动判定" 三层。URL 解析是纯字符串逻辑（本地可测），
    /// jslib 特性检测是浏览器边界（本地按构造测不到，只能出包验）。
    /// </summary>
    public class TouchInputAvailabilityTests
    {
        [Test]
        public void test_url_query_parses_touch_flag()
        {
            // Arrange & Act & Assert
            Assert.IsNull(TouchInputAvailability.ParseTouchQuery(null), "空 URL 不裁决");
            Assert.IsNull(TouchInputAvailability.ParseTouchQuery(""), "空串不裁决");
            Assert.IsNull(TouchInputAvailability.ParseTouchQuery("https://host/index.html"), "无参数不裁决");
            Assert.IsNull(TouchInputAvailability.ParseTouchQuery("https://host/index.html?touch="), "空值不裁决");
            Assert.IsTrue(TouchInputAvailability.ParseTouchQuery("https://host/index.html?touch=1"), "?touch=1 应强制开");
            Assert.IsFalse(TouchInputAvailability.ParseTouchQuery("https://host/index.html?touch=0"), "?touch=0 应强制关");
            Assert.IsTrue(TouchInputAvailability.ParseTouchQuery("https://host/index.html?a=2&touch=1&b=3"),
                "参数夹在中间也要认（面试现场手打 URL 的顺序不可控）");
        }

        [Test]
        public void test_url_query_touch_flag_semantics_locked()
        {
            // Task 13：`?host=` 与 `?touch=` 走**同一条解析路径**（`UrlQuery.TryGetValue`），
            // 所以这条路径的既有语义要钉在这里 —— 抽取共享实现不得改变任何一条裁决。
            Assert.IsTrue(TouchInputAvailability.ParseTouchQuery("https://host/index.html?touch=10"),
                "既有语义是「只看首字符」：?touch=10 仍判开");
            Assert.IsNull(TouchInputAvailability.ParseTouchQuery("https://host/index.html?touch=&x=1"),
                "空值（后面跟别的参数）不裁决");
            Assert.IsNull(TouchInputAvailability.ParseTouchQuery("https://host/index.html?touch=abc"),
                "非 0/1 不裁决");
            Assert.IsTrue(TouchInputAvailability.ParseTouchQuery("https://host/index.html?touch=1#frag"),
                "值在锚点处结束，?touch=1 仍判开");
        }

        [Test]
        public void test_manual_override_beats_auto_detection()
        {
            // 人工档位必须能压过自动判定 —— 自动判据失效时这是唯一可靠的入口
            Assert.IsTrue(TouchInputAvailability.Resolve(TouchOverride.ForceTouch), "ForceTouch 必须强制进触屏分支");
            Assert.IsFalse(TouchInputAvailability.Resolve(TouchOverride.ForceNoTouch), "ForceNoTouch 必须强制走键鼠");
        }
    }
}
