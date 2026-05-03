using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Definition;

namespace Hotfix.GameSystems.Sys3C.Skill
{
    /// <summary>
    /// Buff数据（ScriptableObject）
    /// </summary>
    [CreateAssetMenu(fileName = "BuffData", menuName = "Game/Buff")]
    public class BuffData : ScriptableObject
    {
        [Header("=== Basic Info ===")]
        [SerializeField] private string _buffId;
        public string BuffId => _buffId;

        [SerializeField] private string _buffName;
        public string BuffName => _buffName;

        [SerializeField, TextArea(2, 4)] private string _description;
        public string Description => _description;

        [SerializeField] private Sprite _icon;
        public Sprite Icon => _icon;

        [Header("=== Effect ===")]
        [SerializeField] private EffectType _effectType;
        public EffectType EffectType => _effectType;

        [SerializeField] private EffectData _effect;
        public EffectData Effect => _effect;

        [Header("=== Duration & Stacking ===")]
        [SerializeField] private float _duration;
        public float Duration => _duration;

        [SerializeField] private int _maxStacks = 1;
        public int MaxStacks => _maxStacks;

        [SerializeField] private StackingRule _stackingRule = StackingRule.Refresh;
        public StackingRule StackingRule => _stackingRule;

        [Header("=== Display ===")]
        [SerializeField] private Color _uiColor = Color.white;
        public Color UiColor => _uiColor;

        [SerializeField] private bool _showInUI = true;
        public bool ShowInUI => _showInUI;

        [Header("=== Control Effect ===")]
        [SerializeField] private bool _isControlEffect;
        public bool IsControlEffect => _isControlEffect;

        [Header("=== Priority ===")]
        [SerializeField] private int _priority;
        public int Priority => _priority;

        /// <summary>
        /// 创建简单的属性Buff
        /// </summary>
        public static BuffData CreateAttributeBuff(string id, string name, AttributeType attribute, float value, float duration)
        {
            var buff = ScriptableObject.CreateInstance<BuffData>();
            buff._buffId = id;
            buff._buffName = name;
            buff._effectType = EffectType.Buff;
            buff._duration = duration;

            // 创建Buff效果
            var effect = new BuffEffectData();
            effect.SetEffectId = id;
            effect.SetAttributeToModify = attribute;
            effect.SetValue = value;
            effect.SetModifierType = ModifierType.Flat;
            buff._effect = effect;

            return buff;
        }
    }
}