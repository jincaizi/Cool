using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 菜单工具：一键生成 Character3C.controller
/// 菜单路径：Tools → 3C → Create Character3C Controller
/// </summary>
public static class CreateCharacter3CController
{
    // === 动画片段 GUID ===
    private const string GUID_IDLE = "423aabfede0896f4db862ab8e54dde30";
    private const string GUID_BATTLE_IDLE = "0308cf4e83cf517488b60af58b290fe0";
    private const string GUID_MOVE = "7d4f9e9da55a3bd4f958a63308a522a1";
    private const string GUID_RUN = "5eee3d6dbfbcef04ab20b548575d7b9d";
    private const string GUID_JUMP_START = "c2b2e4c79d87c3045838cbc5935d8a98";
    private const string GUID_JUMP_AIR = "8be8f9bf3f16f184fb9719bd233874e6";
    private const string GUID_JUMP_END = "8b662f6fbb996ba429182e54857361d3";
    private const string GUID_DEATH = "5940bb0b55717a746bbbe4d3e47e7e39";
    private const string GUID_ATTACK1 = "db509ad77f9b4f84a8eb1989f589b24c";
    private const string GUID_ATTACK2 = "8283fadf2c89507469495f30db8680db";
    private const string GUID_ATTACK3 = "9a6c3585df66f2e4782635fc7a23494c";
    private const string GUID_ATTACK4 = "b267a2c210dbd1d4badc3f270df6d12d";

    // === Avatar Mask GUID ===
    private const string GUID_MASK = "acaf52b69aaad2042b776ec016c26e0e";

    // === 输出路径 ===
    private const string OUTPUT_PATH = "Assets/RpgDuo/Animator/Character3C.controller";

    // === 参数名 ===
    private const string PARAM_STATE = "State";
    private const string PARAM_ATTACK_PHASE = "AttackPhase";
    private const string PARAM_JUMP = "Jump";
    private const string PARAM_ATTACK = "Attack";

    [MenuItem("Tools/3C/Create Character3C Controller")]
    public static void Create()
    {
        // 加载动画片段
        var idle = LoadMotion(GUID_IDLE, "Idle");
        var battleIdle = LoadMotion(GUID_BATTLE_IDLE, "BattleIdle");
        var move = LoadMotion(GUID_MOVE, "Move");
        var run = LoadMotion(GUID_RUN, "Run");
        var jumpStart = LoadMotion(GUID_JUMP_START, "JumpStart");
        var jumpAir = LoadMotion(GUID_JUMP_AIR, "JumpAir");
        var jumpEnd = LoadMotion(GUID_JUMP_END, "JumpEnd");
        var death = LoadMotion(GUID_DEATH, "Death");
        var attack1 = LoadMotion(GUID_ATTACK1, "Attack1");
        var attack2 = LoadMotion(GUID_ATTACK2, "Attack2");
        var attack3 = LoadMotion(GUID_ATTACK3, "Attack3");
        var attack4 = LoadMotion(GUID_ATTACK4, "Attack4");

        // 加载 Avatar Mask
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(
            AssetDatabase.GUIDToAssetPath(GUID_MASK));

        // StateMachineBehaviour 类型（通过反射获取，因为 SMB 在 Hotfix 程序集中）
        var smbJumpEndType = Type.GetType("Hotfix.GameSystems.Sys3C.Character.CharacterStateBehaviour, Hotfix.GameSystems.Sys3C");
        var smbAttackType = Type.GetType("Hotfix.GameSystems.Sys3C.Character.AttackStateBehaviour, Hotfix.GameSystems.Sys3C");

        // 创建 Controller
        var controller = AnimatorController.CreateAnimatorControllerAtPath(OUTPUT_PATH);

        // === 添加参数 ===
        controller.AddParameter(PARAM_STATE, AnimatorControllerParameterType.Int);
        controller.AddParameter(PARAM_ATTACK_PHASE, AnimatorControllerParameterType.Int);
        controller.AddParameter(PARAM_JUMP, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(PARAM_ATTACK, AnimatorControllerParameterType.Trigger);

        // === Base Layer ===
        var baseLayer = controller.layers[0];
        baseLayer.name = "Base Layer";
        baseLayer.defaultWeight = 1;

        var baseSM = baseLayer.stateMachine;

        var sIdle = AddState(baseSM, "Idle", idle);
        var sBattleIdle = AddState(baseSM, "BattleIdle", battleIdle);
        var sMove = AddState(baseSM, "Move", move);
        var sRun = AddState(baseSM, "Run", run);
        var sJumpStart = AddState(baseSM, "JumpStart", jumpStart);
        var sJumpAir = AddState(baseSM, "JumpAir", jumpAir);
        var sJumpEnd = AddState(baseSM, "JumpEnd", jumpEnd);
        var sDeath = AddState(baseSM, "Death", death);

        baseSM.defaultState = sIdle;

        // 添加 SMB 到 JumpEnd
        if (smbJumpEndType != null)
            sJumpEnd.AddStateMachineBehaviour(smbJumpEndType);
        else
            Debug.LogWarning("[CreateCharacter3CController] CharacterStateBehaviour type not found, skipping SMB");

        // Base Layer 转换
        float t = 0.1f; // 过渡时间

        // Idle ↔ Move, Run
        AddIntConditionTransition(sIdle, sMove, PARAM_STATE, 2, t);
        AddIntConditionTransition(sIdle, sRun, PARAM_STATE, 3, t);

        // Move ↔ Idle, Run, JumpStart, Death
        AddIntConditionTransition(sMove, sIdle, PARAM_STATE, 0, t);
        AddIntConditionTransition(sMove, sRun, PARAM_STATE, 3, t);
        AddIntConditionTransition(sMove, sJumpStart, PARAM_STATE, 4, t);
        AddIntConditionTransition(sMove, sDeath, PARAM_STATE, 7, t);

        // Run ↔ Idle, Move, JumpStart
        AddIntConditionTransition(sRun, sIdle, PARAM_STATE, 0, t);
        AddIntConditionTransition(sRun, sMove, PARAM_STATE, 2, t);
        AddIntConditionTransition(sRun, sJumpStart, PARAM_STATE, 4, t);

        // BattleIdle → Idle, Move, Run
        AddIntConditionTransition(sBattleIdle, sIdle, PARAM_STATE, 0, t);
        AddIntConditionTransition(sBattleIdle, sMove, PARAM_STATE, 2, t);
        AddIntConditionTransition(sBattleIdle, sRun, PARAM_STATE, 3, t);

        // JumpStart → JumpAir, Death
        AddIntConditionTransition(sJumpStart, sJumpAir, PARAM_STATE, 5, t);
        AddIntConditionTransition(sJumpStart, sDeath, PARAM_STATE, 7, t);

        // JumpAir → JumpEnd, Death
        AddIntConditionTransition(sJumpAir, sJumpEnd, PARAM_STATE, 6, t);
        AddIntConditionTransition(sJumpAir, sDeath, PARAM_STATE, 7, t);

        // JumpEnd → Idle (ExitTime)
        AddExitTimeTransition(sJumpEnd, sIdle, 0.9f, 0.2f);

        // AnyState → Death
        AddAnyStateTransition(baseSM, sDeath, PARAM_STATE, 7, t);

        // === Attack Layer ===
        controller.AddLayer("Attack Layer");
        var attackLayer = controller.layers[1];
        attackLayer.defaultWeight = 1;
        attackLayer.avatarMask = mask;

        var attackSM = attackLayer.stateMachine;

        var sEmpty = AddState(attackSM, "Empty", battleIdle);
        var sAttack1 = AddState(attackSM, "Attack1", attack1);
        var sAttack2 = AddState(attackSM, "Attack2", attack2);
        var sAttack3 = AddState(attackSM, "Attack3", attack3);
        var sAttack4 = AddState(attackSM, "Attack4", attack4);

        attackSM.defaultState = sEmpty;

        // 添加 SMB 到攻击状态
        if (smbAttackType != null)
        {
            sAttack1.AddStateMachineBehaviour(smbAttackType);
            sAttack2.AddStateMachineBehaviour(smbAttackType);
            sAttack3.AddStateMachineBehaviour(smbAttackType);
            sAttack4.AddStateMachineBehaviour(smbAttackType);
        }
        else
        {
            Debug.LogWarning("[CreateCharacter3CController] AttackStateBehaviour type not found, skipping SMB");
        }

        // AnyState → Attack1~4 (Trigger + AttackPhase)
        AddAttackTransition(attackSM, sAttack1, 1);
        AddAttackTransition(attackSM, sAttack2, 2);
        AddAttackTransition(attackSM, sAttack3, 3);
        AddAttackTransition(attackSM, sAttack4, 4);

        // Attack1~4 → Empty (ExitTime)
        AddExitTimeTransition(sAttack1, sEmpty, 0.9f, 0.1f);
        AddExitTimeTransition(sAttack2, sEmpty, 0.9f, 0.1f);
        AddExitTimeTransition(sAttack3, sEmpty, 0.9f, 0.1f);
        AddExitTimeTransition(sAttack4, sEmpty, 0.9f, 0.1f);

        // 保存
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[CreateCharacter3CController] Done! Controller saved to " + OUTPUT_PATH);
    }

    // ============================================
    // 辅助方法
    // ============================================

    private static Motion LoadMotion(string guid, string name)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        var clip = AssetDatabase.LoadAssetAtPath<Motion>(path);
        if (clip == null)
            Debug.LogWarning("[CreateCharacter3CController] Cannot load motion: " + name + " (guid=" + guid + ")");
        return clip;
    }

    private static AnimatorState AddState(AnimatorStateMachine sm, string name, Motion motion)
    {
        var state = sm.AddState(name);
        state.motion = motion;
        return state;
    }

    /// <summary>
    /// 添加 Int 条件转换（Equals 模式）
    /// 注意：AnimatorConditionMode.Equals = 1, NotEqual = 2, Less = 3, Greater = 4
    /// </summary>
    private static void AddIntConditionTransition(
        AnimatorState from, AnimatorState to,
        string paramName, int value, float transitionDuration)
    {
        var transition = from.AddTransition(to);
        transition.AddCondition(AnimatorConditionMode.Equals, value, paramName);
        transition.hasExitTime = false;
        transition.duration = transitionDuration;
        transition.offset = 0;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
    }

    /// <summary>
    /// 添加 ExitTime 转换（无条件）
    /// </summary>
    private static void AddExitTimeTransition(
        AnimatorState from, AnimatorState to,
        float exitTime, float transitionDuration)
    {
        var transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = exitTime;
        transition.duration = transitionDuration;
        transition.offset = 0;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
    }

    /// <summary>
    /// 添加 AnyState 转换（Int 条件）
    /// </summary>
    private static void AddAnyStateTransition(
        AnimatorStateMachine sm, AnimatorState to,
        string paramName, int value, float transitionDuration)
    {
        var transition = sm.AddAnyStateTransition(to);
        transition.AddCondition(AnimatorConditionMode.Equals, value, paramName);
        transition.hasExitTime = false;
        transition.duration = transitionDuration;
        transition.offset = 0;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
    }

    /// <summary>
    /// 添加攻击 AnyState 转换（Trigger + AttackPhase）
    /// </summary>
    private static void AddAttackTransition(
        AnimatorStateMachine sm, AnimatorState to, int phase)
    {
        var transition = sm.AddAnyStateTransition(to);
        transition.AddCondition(AnimatorConditionMode.If, 0, PARAM_ATTACK);
        transition.AddCondition(AnimatorConditionMode.Equals, phase, PARAM_ATTACK_PHASE);
        transition.hasExitTime = false;
        transition.duration = 0.1f;
        transition.offset = 0;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
    }
}
