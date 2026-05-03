using UnityEngine;
using UnityEditor;

namespace Hotfix.GameSystems.Sys3C.Editor
{
    /// <summary>
    /// 技能配置资源生成器
    /// 自动创建默认技能配置
    /// </summary>
    public class SkillConfigGenerator : EditorWindow
    {
        private const string SKILLS_PATH = "Assets/Resources/Skills";

        [MenuItem("Game/3C System/Generate Skill Configs")]
        public static void ShowWindow()
        {
            var window = GetWindow<SkillConfigGenerator>("Skill Config Generator");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        public void OnGUI()
        {
            GUILayout.Label("3C System Skill Config Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "此工具将自动创建以下技能配置资源：\n" +
                "- NormalAttack1 (无CD, 可空中)\n" +
                "- NormalAttack2 (无CD, 可空中)\n" +
                "- SkillQ (CD 5s, 可空中)\n" +
                "- SkillR (CD 10s, 不可空中)",
                MessageType.Info);

            EditorGUILayout.Space();

            if (GUILayout.Button("Generate All Skill Configs", GUILayout.Height(40)))
            {
                GenerateAllSkills();
            }

            EditorGUILayout.Space();

            GUILayout.Label("Existing Skills:", EditorStyles.boldLabel);
            DisplayExistingSkills();
        }

        private static void GenerateAllSkills()
        {
            // 创建目录
            if (!AssetDatabase.IsValidFolder(SKILLS_PATH))
            {
                string[] folders = SKILLS_PATH.Split('/');
                string currentPath = folders[0];
                for (int i = 1; i < folders.Length; i++)
                {
                    string folderPath = currentPath + "/" + folders[i];
                    if (!AssetDatabase.IsValidFolder(folderPath))
                    {
                        AssetDatabase.CreateFolder(currentPath, folders[i]);
                    }
                    currentPath = folderPath;
                }
                UnityEngine.Debug.Log("[SkillConfigGenerator] Created folder: " + SKILLS_PATH);
            }

            // 创建 NormalAttack1
            CreateSkillConfig(
                skillId: "NormalAttack1",
                skillName: "普通攻击1",
                animationName: "Attack01",
                cooldown: 0f,
                canUseInAir: true,
                comboWindowStart: 0.3f,
                comboWindowEnd: 0.8f,
                comboFrameLock: 5
            );

            // 创建 NormalAttack2
            CreateSkillConfig(
                skillId: "NormalAttack2",
                skillName: "普通攻击2",
                animationName: "Attack02",
                cooldown: 0f,
                canUseInAir: true,
                comboWindowStart: 0f,
                comboWindowEnd: 0f,
                comboFrameLock: 0
            );

            // 创建 SkillQ
            CreateSkillConfig(
                skillId: "SkillQ",
                skillName: "突刺",
                animationName: "Attack03",
                cooldown: 5f,
                canUseInAir: true,
                comboWindowStart: 0f,
                comboWindowEnd: 0f,
                comboFrameLock: 0
            );

            // 创建 SkillR
            CreateSkillConfig(
                skillId: "SkillR",
                skillName: "技能R",
                animationName: "Attack04",
                cooldown: 10f,
                canUseInAir: false,
                comboWindowStart: 0f,
                comboWindowEnd: 0f,
                comboFrameLock: 0
            );

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            UnityEngine.Debug.Log("[SkillConfigGenerator] All skill configs generated!");
            EditorUtility.DisplayDialog("完成", "所有技能配置已生成！", "确定");
        }

        private static void CreateSkillConfig(
            string skillId,
            string skillName,
            string animationName,
            float cooldown,
            bool canUseInAir,
            float comboWindowStart,
            float comboWindowEnd,
            int comboFrameLock)
        {
            string path = $"{SKILLS_PATH}/{skillId}.asset";

            // 检查是否已存在
            Skill.SkillConfig existing = AssetDatabase.LoadAssetAtPath<Skill.SkillConfig>(path);
            if (existing != null)
            {
                UnityEngine.Debug.Log($"[SkillConfigGenerator] Skill already exists: {skillId}, updating...");

                existing.SkillName = skillName;
                existing.SkillId = skillId;
                existing.AnimationName = animationName;
                existing.Cooldown = cooldown;
                existing.CanUseInAir = canUseInAir;
                existing.ComboWindowStart = comboWindowStart;
                existing.ComboWindowEnd = comboWindowEnd;
                existing.ComboFrameLock = comboFrameLock;

                EditorUtility.SetDirty(existing);
                return;
            }

            // 创建新配置
            var config = CreateInstance<Skill.SkillConfig>();
            config.SkillName = skillName;
            config.SkillId = skillId;
            config.AnimationName = animationName;
            config.Cooldown = cooldown;
            config.CanUseInAir = canUseInAir;
            config.ComboWindowStart = comboWindowStart;
            config.ComboWindowEnd = comboWindowEnd;
            config.ComboFrameLock = comboFrameLock;

            AssetDatabase.CreateAsset(config, path);
            UnityEngine.Debug.Log($"[SkillConfigGenerator] Created: {path}");
        }

        private static void DisplayExistingSkills()
        {
            if (!AssetDatabase.IsValidFolder(SKILLS_PATH))
            {
                GUILayout.Label("Skills folder not found", EditorStyles.miniLabel);
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Skill.SkillConfig", new[] { SKILLS_PATH });
            if (guids.Length == 0)
            {
                GUILayout.Label("No skill configs found", EditorStyles.miniLabel);
                return;
            }

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<Skill.SkillConfig>(path);
                if (config != null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"• {config.SkillId}", GUILayout.Width(120));
                    GUILayout.Label($"CD: {config.Cooldown}s", GUILayout.Width(80));
                    GUILayout.Label($"Air: {(config.CanUseInAir ? "Yes" : "No")}");
                    GUILayout.EndHorizontal();
                }
            }
        }
    }
}