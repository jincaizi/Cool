using Hotfix.GameSystems.Sys3C.Character;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class CharacterSelectionServiceTests
    {
        private const string PrefsKey = "character.gender";

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
        }

        [Test]
        public void test_character_selection_save_load_roundtrip_male_returns_male()
        {
            CharacterSelectionService.Save(CharacterGender.Male);
            Assert.AreEqual(CharacterGender.Male, CharacterSelectionService.Load());
        }

        [Test]
        public void test_character_selection_save_load_roundtrip_female_returns_female()
        {
            CharacterSelectionService.Save(CharacterGender.Female);
            Assert.AreEqual(CharacterGender.Female, CharacterSelectionService.Load());
        }

        [Test]
        public void test_character_selection_load_no_save_returns_male_default()
        {
            Assert.AreEqual(CharacterGender.Male, CharacterSelectionService.Load());
        }

        [Test]
        public void test_character_selection_load_corrupted_value_returns_male_default()
        {
            PlayerPrefs.SetInt(PrefsKey, 999);
            Assert.AreEqual(CharacterGender.Male, CharacterSelectionService.Load());
        }
    }
}
