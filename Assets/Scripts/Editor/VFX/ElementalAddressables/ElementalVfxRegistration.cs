using System;
using Cool.SyncCore;
using Hotfix.GameSystems.VFX;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEngine;

namespace Cool.EditorTools
{
    public sealed class ElementalVfxRegistration : BuildPlayerProcessor
    {
        public const string GroupName = "Elemental VFX";
        private const string PrefabFolder = "Assets/Prefabs/VFX/Elemental/";

        // Addressables 1.21 prepares its content and streaming paths at order 1.
        public override int callbackOrder => -10;

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            Register();
        }

        [InitializeOnLoadMethod]
        private static void ScheduleRegistration()
        {
            EditorApplication.delayCall += Register;
        }

        [MenuItem("Cool/VFX/Register Elemental Addressables")]
        public static void Register()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var group = settings.FindGroup(GroupName);
            bool changed = false;
            if (group == null)
            {
                group = settings.CreateGroup(GroupName, false, false, false, null,
                    typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
                group.GetSchema<BundledAssetGroupSchema>().BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
                group.GetSchema<BundledAssetGroupSchema>().Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
                changed = true;
            }

            if (settings.BuildAddressablesWithPlayerBuild != AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer)
            {
                settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
                EditorUtility.SetDirty(settings);
                changed = true;
            }

            var catalog = ElementalCatalogDefaults.Create();
            foreach (var element in catalog.Elements)
                changed |= RegisterPrefab(settings, group, element.SwordQiViewKey);
            foreach (var status in catalog.Statuses)
                changed |= RegisterPrefab(settings, group, ElementVfxAssets.StatusKey(AuthoritativeStatusVfx.Map(status.EffectId)));

            if (changed) AssetDatabase.SaveAssets();
        }

        private static bool RegisterPrefab(AddressableAssetSettings settings, AddressableAssetGroup group, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            var path = PrefabFolder + key.Replace('/', '-') + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new BuildFailedException("Missing elemental VFX prefab: " + path);

            var guid = AssetDatabase.AssetPathToGUID(path);
            foreach (var existingGroup in settings.groups)
            {
                if (existingGroup == null) continue;
                foreach (var existing in existingGroup.entries)
                    if (string.Equals(existing.address, key, StringComparison.Ordinal) && existing.guid != guid)
                        throw new BuildFailedException("Duplicate elemental VFX address: " + key);
            }

            var entry = settings.FindAssetEntry(guid);
            if (entry != null && entry.parentGroup == group && entry.address == key) return false;
            settings.CreateOrMoveEntry(guid, group).SetAddress(key);
            return true;
        }
    }
}
