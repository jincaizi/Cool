using System.Collections;
using Hotfix.GameSystems.Monster;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class HitStopManager : MonoBehaviour
    {
        public static bool EnableVFX = false;

        [SerializeField] private HitFeedbackProfile _profile;
        [SerializeField] private Animator _playerAnimator;

        private bool _playerFrozen;
        private bool _timeSlowing;

        private void OnEnable()
        {
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnHit);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnHit);
            StopAllCoroutines();
            _playerFrozen = false;
            _timeSlowing = false;
            if (_playerAnimator != null) _playerAnimator.speed = 1f;
            Time.timeScale = 1f;
        }

        private void OnHit(MonsterTakeDamageEvent e)
        {
            if (!EnableVFX) return;
            float duration = e.SkillId > 0 ? _profile.SkillHitStop : _profile.NormalHitStop;
            if (e.IsCritical) duration += _profile.CritHitStopBonus;
            duration += (e.ComboIndex - 1) * _profile.ComboHitStopBonus;
            duration = Mathf.Min(duration, _profile.MaxHitStop);

            if (_playerAnimator != null && !_playerFrozen)
                StartCoroutine(FreezePlayerAnimator(duration));

            var targetAnim = FindAnimatorById(e.EntityId);
            if (targetAnim != null)
                StartCoroutine(FreezeAnimator(targetAnim, duration));

            if (e.IsCritical && !_timeSlowing)
                StartCoroutine(TimeSlowRoutine(_profile.CritTimeSlowScale, _profile.CritTimeSlowDuration));
        }

        private IEnumerator FreezePlayerAnimator(float duration)
        {
            _playerFrozen = true;
            _playerAnimator.speed = 0f;
            yield return new WaitForSecondsRealtime(duration);
            if (_playerAnimator != null) _playerAnimator.speed = 1f;
            _playerFrozen = false;
        }

        private IEnumerator FreezeAnimator(Animator anim, float duration)
        {
            anim.speed = 0f;
            yield return new WaitForSecondsRealtime(duration);
            if (anim != null) anim.speed = 1f;
        }

        private IEnumerator TimeSlowRoutine(float scale, float duration)
        {
            _timeSlowing = true;
            Time.timeScale = scale;
            yield return new WaitForSecondsRealtime(duration);
            Time.timeScale = 1f;
            _timeSlowing = false;
        }

        private Animator FindAnimatorById(int entityId)
        {
            var entity = PhysicsRegistry.Instance.GetEntity(entityId);
            if (entity is MonsterEntity monster)
                return monster.Animator;
            return null;
        }
    }
}
