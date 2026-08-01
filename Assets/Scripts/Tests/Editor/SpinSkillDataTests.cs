using System.Reflection;
using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;
using UnityEngine;
using Definition = Hotfix.GameSystems.Skills.Definition;

namespace GameSys.EditorTests
{
    public class SpinSkillDataTests
    {
        private static void SetField(object target, string name, object value)
        {
            typeof(SpinSkillData).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(target, value);
        }

        private static void CallOnValidate(SpinSkillData data)
        {
            typeof(SpinSkillData).GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(data, null);
        }

        private static SpinSkillData CreateData()
        {
            return ScriptableObject.CreateInstance<SpinSkillData>();
        }

        [Test]
        public void OnValidate_SetsSkillTypeToSpin()
        {
            var data = CreateData();
            CallOnValidate(data);
            Assert.AreEqual(Definition.SkillType.Spin, data.SkillType);
        }

        [Test]
        public void OnValidate_ClampsTickIntervalToMinimum()
        {
            var data = CreateData();
            SetField(data, "_tickInterval", 0f);
            CallOnValidate(data);
            Assert.AreEqual(0.01f, data.TickInterval, 0.0001f);
        }

        [Test]
        public void OnValidate_ClampsMaxDurationToMinDuration()
        {
            var data = CreateData();
            SetField(data, "_minDuration", 3f);
            SetField(data, "_maxDuration", 1f);
            CallOnValidate(data);
            Assert.AreEqual(3f, data.MaxDuration, 0.0001f);
        }

        [Test]
        public void OnValidate_ClampsMoveSpeedMultiplierToUnitRange()
        {
            var data = CreateData();
            SetField(data, "_moveSpeedMultiplier", 1.5f);
            CallOnValidate(data);
            Assert.AreEqual(1f, data.MoveSpeedMultiplier, 0.0001f);
        }

        [Test]
        public void IsInCancelWindow_Boundaries()
        {
            var data = CreateData();
            SetField(data, "_minDuration", 1f);
            SetField(data, "_maxDuration", 5f);

            Assert.IsFalse(data.IsInCancelWindow(0.999f), "min 之前不可取消");
            Assert.IsTrue(data.IsInCancelWindow(1f), "elapsed == min 可取消");
            Assert.IsTrue(data.IsInCancelWindow(4.999f), "max 之前可取消");
            Assert.IsFalse(data.IsInCancelWindow(5f), "elapsed == max 不可取消（已自动结束）");
        }
    }
}
