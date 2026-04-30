using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 菜单工具：一键生成 Character3C.controller
/// 菜单路径：Tools → 3C → Create Character3C Controller
///
/// 根据新架构生成：
/// - Layer 0: Base Layer (Override) - Idle/Move/Sprint/Jump*
/// - Layer 1: Attack Layer (Override) - AttackIdle/Attack1/Attack2/AttackQ/AttackR
/// - Layer 2: Hit Layer (Additive) - Hit
///
/// 参数：BaseState, AttackState, IsJumping, IsHit, Attack, SkillQ, SkillR, Hit
/// </summary>
public static class CreateCharacter3CController
{
    // === 输出路径 ===
    private const string OUTPUT_PATH = "Assets/RpgDuo/Animator/Character3C.controller";

    // === 参数名 ===
    private const string PARAM_BASE_STATE = "BaseState";
    private const string PARAM_ATTACK_STATE = "AttackState";
    private const string PARAM_IS_JUMPING = "IsJumping";
    private const string PARAM_IS_HIT = "IsHit";
    private const string PARAM_ATTACK = "Attack";
    private const string PARAM_SKILL_Q = "SkillQ";
    private const string PARAM_SKILL_R = "SkillR";
    private const string PARAM_HIT = "Hit";

    // === BaseState 枚举值 ===
    private const int STATE_IDLE = 0;
    private const int STATE_MOVE = 1;
    private const int STATE_SPRINT = 2;
    private const int STATE_JUMP_START = 3;
    private const int STATE_JUMP_AIR = 4;
    private const int STATE_JUMP_END = 5;
    private const int STATE_DEATH = 6;

    // === AttackState 枚举值 ===
    private const int ATTACK_IDLE = 0;
    private const int ATTACK_1 = 1;
    private const int ATTACK_2 = 2;
    private const int ATTACK_Q = 3;
    private const int ATTACK_R = 4;

    // === Layer 索引 ===
    private const int LAYER_BASE = 0;
    private const int LAYER_ATTACK = 1;
    private const int LAYER_HIT = 2;

    [MenuItem("Tools/3C/Create Character3C Controller")]
    public static void Create()
    {
        Debug.Log("[CreateCharacter3CController] Starting...");

        // 删除旧文件
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(OUTPUT_PATH) != null)
        {
            AssetDatabase.DeleteAsset(OUTPUT_PATH);
            Debug.Log("[CreateCharacter3CController] Deleted old controller");
        }

        // 创建 Controller
        var controller = AnimatorController.CreateAnimatorControllerAtPath(OUTPUT_PATH);

        // === 添加参数 ===
        controller.AddParameter(PARAM_BASE_STATE, AnimatorControllerParameterType.Int);
        controller.AddParameter(PARAM_ATTACK_STATE, AnimatorControllerParameterType.Int);
        controller.AddParameter(PARAM_IS_JUMPING, AnimatorControllerParameterType.Bool);
        controller.AddParameter(PARAM_IS_HIT, AnimatorControllerParameterType.Bool);
        controller.AddParameter(PARAM_ATTACK, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(PARAM_SKILL_Q, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(PARAM_SKILL_R, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(PARAM_HIT, AnimatorControllerParameterType.Trigger);

        Debug.Log("[CreateCharacter3CController] Parameters added");

        // === 创建 Layers ===
        CreateBaseLayer(controller);
        CreateAttackLayer(controller);
        CreateHitLayer(controller);

        // 保存
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[CreateCharacter3CController] Done! Controller saved to " + OUTPUT_PATH);

        // 选中新创建的资源
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<AnimatorController>(OUTPUT_PATH);
    }

    // ============================================
    // Base Layer (Layer 0)
    // ============================================
    private static void CreateBaseLayer(AnimatorController controller)
    {
        var layer = controller.layers[LAYER_BASE];
        layer.name = "Base";
        layer.defaultWeight = 1f;

        var sm = layer.stateMachine;

        // 创建所有状态（动画置空，由用户手动赋值）
        var sIdle = AddState(sm, "Idle", null);
        var sMove = AddState(sm, "Move", null);
        var sSprint = AddState(sm, "Sprint", null);
        var sJumpStart = AddState(sm, "JumpStart", null);
        var sJumpAir = AddState(sm, "JumpAir", null);
        var sJumpEnd = AddState(sm, "JumpEnd", null);
        var sDeath = AddState(sm, "Death", null);

        // 设置默认状态
        sm.defaultState = sIdle;

        // 挂载 BaseStateBehaviour 到 JumpEnd
        AddStateMachineBehaviour(sJumpEnd, "Hotfix.GameSystems.Sys3C.Animation.StateBehaviours.BaseStateBehaviour, Hotfix.GameSystems.Sys3C");

        // === 添加转换 ===
        float t = 0.1f; // 过渡时间

        // Idle 转换
        AddTransition(sIdle, sMove, PARAM_BASE_STATE, STATE_MOVE, t);
        AddTransition(sIdle, sSprint, PARAM_BASE_STATE, STATE_SPRINT, t);
        AddTransition(sIdle, sDeath, PARAM_BASE_STATE, STATE_DEATH, t);

        // Move 转换
        AddTransition(sMove, sIdle, PARAM_BASE_STATE, STATE_IDLE, t);
        AddTransition(sMove, sSprint, PARAM_BASE_STATE, STATE_SPRINT, t);
        AddTransition(sMove, sJumpStart, PARAM_BASE_STATE, STATE_JUMP_START, t);
        AddTransition(sMove, sDeath, PARAM_BASE_STATE, STATE_DEATH, t);

        // Sprint 转换
        AddTransition(sSprint, sIdle, PARAM_BASE_STATE, STATE_IDLE, t);
        AddTransition(sSprint, sMove, PARAM_BASE_STATE, STATE_MOVE, t);
        AddTransition(sSprint, sJumpStart, PARAM_BASE_STATE, STATE_JUMP_START, t);
        AddTransition(sSprint, sDeath, PARAM_BASE_STATE, STATE_DEATH, t);

        // JumpStart → JumpAir (自动，1帧后)
        var jumpStartToAir = AddTransition(sJumpStart, sJumpAir, t);
        jumpStartToAir.hasExitTime = false;
        jumpStartToAir.AddCondition(AnimatorConditionMode.Equals, STATE_JUMP_AIR, PARAM_BASE_STATE);

        // JumpAir → JumpEnd (落地检测)
        var jumpAirToEnd = AddTransition(sJumpAir, sJumpEnd, t);
        jumpAirToEnd.hasExitTime = false;
        jumpAirToEnd.AddCondition(AnimatorConditionMode.Equals, STATE_JUMP_END, PARAM_BASE_STATE);

        // JumpEnd → 返回任意状态
        AddTransition(sJumpEnd, sIdle, PARAM_BASE_STATE, STATE_IDLE, t);
        AddTransition(sJumpEnd, sMove, PARAM_BASE_STATE, STATE_MOVE, t);
        AddTransition(sJumpEnd, sSprint, PARAM_BASE_STATE, STATE_SPRINT, t);

        // AnyState → Death
        AddAnyStateTransition(sm, sDeath, PARAM_BASE_STATE, STATE_DEATH, t);

        Debug.Log("[CreateCharacter3CController] Base Layer created");
    }

    // ============================================
    // Attack Layer (Layer 1)
    // ============================================
    private static void CreateAttackLayer(AnimatorController controller)
    {
        // 添加新 Layer
        controller.AddLayer("Attack");
        var layer = controller.layers[LAYER_ATTACK];
        layer.name = "Attack";
        layer.defaultWeight = 1f;

        var sm = layer.stateMachine;

        // 创建所有状态（动画置空）
        var sIdle = AddState(sm, "AttackIdle", null);
        var sAttack1 = AddState(sm, "Attack1", null);
        var sAttack2 = AddState(sm, "Attack2", null);
        var sAttackQ = AddState(sm, "AttackQ", null);
        var sAttackR = AddState(sm, "AttackR", null);

        sm.defaultState = sIdle;

        // 挂载 AttackStateBehaviour
        AddStateMachineBehaviour(sAttack1, "Hotfix.GameSystems.Sys3C.Animation.StateBehaviours.AttackStateBehaviour, Hotfix.GameSystems.Sys3C");
        AddStateMachineBehaviour(sAttack2, "Hotfix.GameSystems.Sys3C.Animation.StateBehaviours.AttackStateBehaviour, Hotfix.GameSystems.Sys3C");

        // === 添加转换 ===
        float t = 0.05f;

        // AttackIdle → Attack1 (Attack Trigger)
        AddTriggerTransition(sIdle, sAttack1, PARAM_ATTACK, t);

        // Attack1 → Attack2 (Attack Trigger + 连击)
        var attack1to2 = AddTransition(sAttack1, sAttack2, t);
        attack1to2.AddCondition(AnimatorConditionMode.If, 0, PARAM_ATTACK);

        // Attack1/Attack2 → AttackIdle (动画完成)
        AddTransition(sAttack1, sIdle, PARAM_ATTACK_STATE, ATTACK_IDLE, t);
        AddTransition(sAttack2, sIdle, PARAM_ATTACK_STATE, ATTACK_IDLE, t);

        // AttackIdle → SkillQ/SkillR
        AddTriggerTransition(sIdle, sAttackQ, PARAM_SKILL_Q, t);
        AddTriggerTransition(sIdle, sAttackR, PARAM_SKILL_R, t);

        // SkillQ/SkillR → AttackIdle (动画完成)
        AddTransition(sAttackQ, sIdle, PARAM_ATTACK_STATE, ATTACK_IDLE, t);
        AddTransition(sAttackR, sIdle, PARAM_ATTACK_STATE, ATTACK_IDLE, t);

        Debug.Log("[CreateCharacter3CController] Attack Layer created");
    }

    // ============================================
    // Hit Layer (Layer 2)
    // ============================================
    private static void CreateHitLayer(AnimatorController controller)
    {
        // 添加新 Layer
        controller.AddLayer("Hit");
        var layer = controller.layers[LAYER_HIT];
        layer.name = "Hit";
        layer.defaultWeight = 0f; // 默认权重为0
        layer.blendingMode = AnimatorLayerBlendingMode.Additive; // 叠加模式

        var sm = layer.stateMachine;

        // 创建 Hit 状态
        var sHit = AddState(sm, "Hit", null);

        // 挂载 HitStateBehaviour
        AddStateMachineBehaviour(sHit, "Hotfix.GameSystems.Sys3C.Animation.StateBehaviours.HitStateBehaviour, Hotfix.GameSystems.Sys3C");

        // AnyState → Hit (Hit Trigger)
        AddAnyStateTriggerTransition(sm, sHit, PARAM_HIT);

        Debug.Log("[CreateCharacter3CController] Hit Layer created");
    }

    // ============================================
    // 辅助方法
    // ============================================

    /// <summary>
    /// 添加状态（动画为空）
    /// </summary>
    private static AnimatorState AddState(AnimatorStateMachine sm, string name, Motion motion)
    {
        var state = sm.AddState(name);
        state.motion = motion;
        return state;
    }

    /// <summary>
    /// 添加 Int 条件转换
    /// </summary>
    private static AnimatorStateTransition AddTransition(
        AnimatorState from, AnimatorState to,
        string paramName, int value, float duration)
    {
        var transition = from.AddTransition(to);
        transition.AddCondition(AnimatorConditionMode.Equals, value, paramName);
        transition.hasExitTime = false;
        transition.duration = duration;
        transition.offset = 0;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        return transition;
    }

    /// <summary>
    /// 添加 Trigger 条件转换
    /// </summary>
    private static AnimatorStateTransition AddTriggerTransition(
        AnimatorState from, AnimatorState to,
        string triggerParam, float duration)
    {
        var transition = from.AddTransition(to);
        transition.AddCondition(AnimatorConditionMode.If, 0, triggerParam);
        transition.hasExitTime = false;
        transition.duration = duration;
        transition.offset = 0;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        return transition;
    }

    /// <summary>
    /// 添加转换（无条件的自动转换）
    /// </summary>
    private static AnimatorStateTransition AddTransition(
        AnimatorState from, AnimatorState to, float duration)
    {
        var transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = duration;
        transition.offset = 0;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        return transition;
    }

    /// <summary>
    /// 添加 AnyState 转换（Int 条件）
    /// </summary>
    private static void AddAnyStateTransition(
        AnimatorStateMachine sm, AnimatorState to,
        string paramName, int value, float duration)
    {
        var transition = sm.AddAnyStateTransition(to);
        transition.AddCondition(AnimatorConditionMode.Equals, value, paramName);
        transition.hasExitTime = false;
        transition.duration = duration;
        transition.offset = 0;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
    }

    /// <summary>
    /// 添加 AnyState 转换（Trigger 条件）
    /// </summary>
    private static void AddAnyStateTriggerTransition(
        AnimatorStateMachine sm, AnimatorState to, string triggerParam)
    {
        var transition = sm.AddAnyStateTransition(to);
        transition.AddCondition(AnimatorConditionMode.If, 0, triggerParam);
        transition.hasExitTime = false;
        transition.duration = 0;
        transition.offset = 0;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
    }

    /// <summary>
    /// 通过类型名称添加 StateMachineBehaviour
    /// </summary>
    private static void AddStateMachineBehaviour(AnimatorState state, string typeName)
    {
        var type = Type.GetType(typeName);
        if (type != null)
        {
            state.AddStateMachineBehaviour(type);
            Debug.Log($"[CreateCharacter3CController] Added SMB: {typeName}");
        }
        else
        {
            Debug.LogWarning($"[CreateCharacter3CController] Type not found: {typeName}");
        }
    }
}
