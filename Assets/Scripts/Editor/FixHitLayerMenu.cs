using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// 修复 Character3C Controller 的 Hit 层问题
/// 菜单路径：Tools → 3C → Fix Hit Layer
/// </summary>
public static class FixHitLayerMenu
{
    [MenuItem("Tools/3C/Fix Hit Layer")]
    public static void FixHitLayer()
    {
        // 查找 Character3C.controller（精确匹配）
        var guids = AssetDatabase.FindAssets("Character3C t:AnimatorController");
        if (guids.Length == 0)
        {
            Debug.LogError("[FixHitLayer] Character3C.controller not found! Please ensure it's in Assets/RpgDuo/Animator/");
            return;
        }

        // 找到精确匹配的路径
        string targetPath = null;
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            // 匹配 "Character3C.controller" 但不是 "Character3C-manual.controller"
            if (path.Contains("Character3C.controller") && !path.Contains("manual"))
            {
                targetPath = path;
                break;
            }
        }

        if (string.IsNullOrEmpty(targetPath))
        {
            Debug.LogError("[FixHitLayer] Character3C.controller (not manual) not found!");
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(targetPath);
        if (controller == null)
        {
            Debug.LogError("[FixHitLayer] Failed to load controller at: " + targetPath);
            return;
        }

        Debug.Log("[FixHitLayer] Processing: " + targetPath);

        // 查找 Hit 层（兼容 "Hit" 或 "HitLayer"）
        int hitLayerIndex = -1;
        string hitLayerName = null;
        for (int i = 0; i < controller.layers.Length; i++)
        {
            string layerName = controller.layers[i].name;
            if (layerName == "Hit" || layerName == "HitLayer")
            {
                hitLayerIndex = i;
                hitLayerName = layerName;
                break;
            }
        }

        if (hitLayerIndex < 0)
        {
            return;
        }

        Debug.Log($"[FixHitLayer] Found Hit layer at index {hitLayerIndex} (name: {hitLayerName})");

        var hitLayer = controller.layers[hitLayerIndex];
        hitLayer.blendingMode = AnimatorLayerBlendingMode.Override;
        hitLayer.defaultWeight = 0f;

        var sm = hitLayer.stateMachine;

        // 找到或创建 Hit 状态
        AnimatorState hitState = null;
        AnimatorState emptyState = null;
        foreach (var s in sm.states)
        {
            if (s.state.name == "Hit")
                hitState = s.state;
            else if (s.state.name == "Empty")
                emptyState = s.state;
        }

        if (hitState == null)
        {
            hitState = sm.AddState("Hit");
            Debug.Log("[FixHitLayer] Created Hit state");
        }

        // 创建 Empty 状态（如果不存在）
        if (emptyState == null)
        {
            emptyState = sm.AddState("Empty");
            emptyState.speed = 0f;
            Debug.Log("[FixHitLayer] Created Empty state");
        }

        // 设置默认状态为 Empty
        sm.defaultState = emptyState;

        // 移除所有 AnyState 转换
        foreach (var t in sm.anyStateTransitions)
        {
            sm.RemoveAnyStateTransition(t);
        }

        // 移除 Hit 状态的所有出站转换
        foreach (var t in hitState.transitions)
        {
            hitState.RemoveTransition(t);
        }

        // 添加 Hit → Empty 转换
        var hitToEmpty = hitState.AddTransition(emptyState);
        hitToEmpty.hasExitTime = true;
        hitToEmpty.exitTime = 0.9f;
        hitToEmpty.duration = 0.1f;
        hitToEmpty.canTransitionToSelf = false;

        // 添加 Empty → Hit 转换（通过 Hit Trigger）
        var emptyToHit = emptyState.AddTransition(hitState);
        emptyToHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
        emptyToHit.hasExitTime = false;
        emptyToHit.duration = 0f;
        emptyToHit.canTransitionToSelf = false;

        // 保存
        controller.layers[hitLayerIndex] = hitLayer;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log("[FixHitLayer] Done! Hit Layer fixed at " + targetPath);
    }
}
