using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace Hotfix.GameSystems.Sys3C.Editor
{
    public class AddJumpBehaviours
    {
        [MenuItem("Tools/3C/Add Jump State Behaviours")]
        public static void AddBehaviours()
        {
            string path = "Assets/RpgDuo/Animator/Character3C.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);

            if (controller == null)
            {
                Debug.LogError($"Could not find Animator Controller at {path}");
                return;
            }

            Debug.Log($"Found controller: {controller.name}");

            // Get the base layer
            var layer = controller.layers[0];
            var stateMachine = layer.stateMachine;

            // States to add behaviour to
            string[] targetStates = { "JumpStart", "JumpAir", "JumpEnd" };

            int addedCount = 0;

            // Process main states
            foreach (var state in stateMachine.states)
            {
                foreach (var targetName in targetStates)
                {
                    if (state.state.name == targetName)
                    {
                        AddBehaviourToState(state.state, targetName, ref addedCount);
                    }
                }
            }

            // Process child state machines recursively
            ProcessChildStateMachines(stateMachine, targetStates, ref addedCount);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log($"Done! Added CharacterStateBehaviour to {addedCount} states.");
        }

        private static void ProcessChildStateMachines(AnimatorStateMachine stateMachine, string[] targetStates, ref int addedCount)
        {
            // Process direct child states
            foreach (var childState in stateMachine.states)
            {
                foreach (var targetName in targetStates)
                {
                    if (childState.state.name == targetName)
                    {
                        AddBehaviourToState(childState.state, targetName + " (child)", ref addedCount);
                    }
                }
            }

            // Process child state machines
            foreach (var childSM in stateMachine.stateMachines)
            {
                // Check states in child state machine
                foreach (var state in childSM.stateMachine.states)
                {
                    foreach (var targetName in targetStates)
                    {
                        if (state.state.name == targetName)
                        {
                            AddBehaviourToState(state.state, targetName + " (nested)", ref addedCount);
                        }
                    }
                }

                // Recurse into grandchildren
                ProcessChildStateMachines(childSM.stateMachine, targetStates, ref addedCount);
            }
        }

        private static void AddBehaviourToState(AnimatorState state, string name, ref int addedCount)
        {
            // Check if already has the behaviour
            if (state.behaviours != null)
            {
                foreach (var behaviour in state.behaviours)
                {
                    if (behaviour != null && behaviour.GetType().Name == "CharacterStateBehaviour")
                    {
                        Debug.Log($"{name} already has CharacterStateBehaviour");
                        return;
                    }
                }
            }

            // Add the behaviour using the type name
            var behaviourType = System.Type.GetType("Hotfix.GameSystems.Sys3C.Character.CharacterStateBehaviour, Assembly-CSharp");
            if (behaviourType != null)
            {
                state.AddStateMachineBehaviour(behaviourType);
                addedCount++;
                Debug.Log($"Added CharacterStateBehaviour to {name}");
            }
            else
            {
                Debug.LogError($"Could not find CharacterStateBehaviour type");
            }
        }
    }
}
