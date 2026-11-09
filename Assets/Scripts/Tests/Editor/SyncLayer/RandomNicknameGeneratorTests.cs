using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class RandomNicknameGeneratorTests
    {
        [Test]
        public void test_generated_name_is_four_chinese_characters()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                string name = RandomNicknameGenerator.Generate(new System.Random(seed));
                Assert.AreEqual(4, name.Length);
                foreach (char c in name)
                    Assert.IsTrue(c >= '一' && c <= '鿿', name);
                Assert.AreNotEqual("Player", name);
            }
        }

        [Test]
        public void test_seeded_generator_is_deterministic()
        {
            Assert.AreEqual(
                RandomNicknameGenerator.Generate(new System.Random(42)),
                RandomNicknameGenerator.Generate(new System.Random(42)));
        }
    }
}
