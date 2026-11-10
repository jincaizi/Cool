using DataDefinition;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class BuildVersionTests
    {
        [Test]
        public void test_format_adds_v_prefix_to_three_segments()
        {
            Assert.AreEqual("v0.1.0", BuildVersion.Format("0.1.0"));
        }

        [Test]
        public void test_format_keeps_four_segment_build_suffix()
        {
            Assert.AreEqual("v0.1.0.cbad", BuildVersion.Format("0.1.0.cbad"));
        }

        [Test]
        public void test_format_blank_returns_placeholder()
        {
            Assert.AreEqual("v?", BuildVersion.Format(null));
            Assert.AreEqual("v?", BuildVersion.Format("   "));
        }

        [Test]
        public void test_base_version_accepts_three_numeric_segments()
        {
            Assert.IsTrue(BuildVersion.IsBaseVersion("0.1.0"));
            Assert.IsTrue(BuildVersion.IsBaseVersion("12.34.56"));
        }

        [Test]
        public void test_base_version_rejects_two_and_four_segments()
        {
            Assert.IsFalse(BuildVersion.IsBaseVersion("0.1"));
            Assert.IsFalse(BuildVersion.IsBaseVersion("0.1.0.cbad"));
            Assert.IsFalse(BuildVersion.IsBaseVersion("v0.1.0"));
            Assert.IsFalse(BuildVersion.IsBaseVersion("0.1.x"));
        }

        [Test]
        public void test_compose_appends_sha4()
        {
            Assert.AreEqual("0.1.0.cbad", BuildVersion.Compose("0.1.0", "cbad"));
        }
    }
}
