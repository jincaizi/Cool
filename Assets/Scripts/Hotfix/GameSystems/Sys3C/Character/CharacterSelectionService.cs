using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 角色选择持久化：读写 PlayerPrefs（纯客户端，网络化时再扩展协议）
    /// </summary>
    public static class CharacterSelectionService
    {
        private const string PrefsKey = "character.gender";

        public static void Save(CharacterGender gender)
        {
            PlayerPrefs.SetInt(PrefsKey, (int)gender);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 无存档 / 非法值 → 默认 Male
        /// </summary>
        public static CharacterGender Load()
        {
            if (!PlayerPrefs.HasKey(PrefsKey)) return CharacterGender.Male;
            int raw = PlayerPrefs.GetInt(PrefsKey);
            return raw == (int)CharacterGender.Female ? CharacterGender.Female : CharacterGender.Male;
        }
    }
}
