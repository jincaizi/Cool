using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillExecutorSpinTests
    {
        private GameObject _ownerGo;

        [TearDown]
        public void TearDown()
        {
            if (_ownerGo != null)
                Object.DestroyImmediate(_ownerGo);
        }

        private SkillExecutor CreateSpinExecutor(float min, float max)
        {
            var data = ScriptableObject.CreateInstance<SpinSkillData>();
            TestHelpers.SetField(data, "_minDuration", min);
            TestHelpers.SetField(data, "_maxDuration", max);
            TestHelpers.SetField(data, "_tickInterval", 0.2f);

            _ownerGo = new GameObject("SpinOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var executor = new SkillExecutor(owner, data);
            executor.TryStart();
            return executor;
        }

        [Test]
        public void CancelWindow_ZeroMin_AllowsImmediateCancel()
        {
            var executor = CreateSpinExecutor(0f, 5f);
            Assert.AreEqual(SkillSubState.Spinning, executor.CurrentSubState);

            Assert.IsTrue(executor.CanCancel(), "min=0 时按下即进入取消窗口");
            Assert.IsTrue(executor.Cancel());
            Assert.IsFalse(executor.IsActive, "取消后技能不再激活");
        }

        [Test]
        public void CancelWindow_OneMin_RejectsImmediateCancel()
        {
            var executor = CreateSpinExecutor(1f, 5f);
            Assert.IsFalse(executor.CanCancel(), "min=1 时刚按下不可取消");
            Assert.IsFalse(executor.Cancel());
            Assert.IsTrue(executor.IsActive, "取消被拒绝时技能保持激活");
        }
    }
}
