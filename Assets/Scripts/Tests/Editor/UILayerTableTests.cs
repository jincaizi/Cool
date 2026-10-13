using System;
using System.Linq;
using Hotfix.GameSystems.UI;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 1（清创）：层表是"层有多少个、叫什么、排序号多少"的唯一真值源。
    ///
    /// 守住的不变量是 **下标 == 枚举值** —— UIManager 用 (int)panel.Layer 当下标去索引
    /// _layers 数组，一旦表的行序与枚举值错位，面板会被静默塞进错误的 Canvas（不报错、
    /// 只是画在错误的层级上）。
    /// </summary>
    public class UILayerTableTests
    {
        [Test]
        public void test_layer_table_covers_every_enum_value_in_index_order()
        {
            // Arrange：Enum.GetValues 按底层值升序返回
            var values = (LayerType[])Enum.GetValues(typeof(LayerType));

            // Assert
            Assert.AreEqual(values.Length, UIConst.Layers.Length,
                "层表行数必须等于 LayerType 的取值个数");

            for (int i = 0; i < values.Length; i++)
            {
                Assert.AreEqual(values[i], UIConst.Layers[i].layer,
                    "层表第 " + i + " 行的层与枚举值不符");
                Assert.AreEqual(i, (int)UIConst.Layers[i].layer,
                    "层表下标必须等于枚举值（UIManager 拿它做数组下标）");
            }
        }

        [Test]
        public void test_layer_sort_orders_are_unique_and_unchanged_in_phase1()
        {
            // Arrange
            var actual = UIConst.Layers.Select(l => l.sortOrder).ToArray();

            // Assert
            Assert.AreEqual(actual.Length, actual.Distinct().Count(), "排序号不得重复");
            CollectionAssert.AreEqual(new[] { 1000, 2000, 3000, 4000, 5000 }, actual,
                "阶段 1 是零行为变化：排序号必须与重构前逐位相同。" +
                "数值重排属于阶段 3 —— 改这里说明改错了批次。");
        }
    }
}
