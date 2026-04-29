using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Animation
{
    public class HitManager
    {
        private readonly AnimationDriver _driver;
        private const int HIT_LAYER_INDEX = 2;

        public HitManager(AnimationDriver driver)
        {
            _driver = driver;
        }

        public void TriggerHit()
        {
            _driver.TriggerHit();
            _driver.SetIsHit(true);
            _driver.SetHitLayerWeight(1f);
            Debug.Log("[HitManager] TriggerHit called");
        }

        public void OnHitCompleted()
        {
            _driver.SetIsHit(false);
            _driver.SetHitLayerWeight(0f);
            Debug.Log("[HitManager] OnHitCompleted");
        }

        public void HandleHitCompleted(string stateName)
        {
            OnHitCompleted();
        }

        public float GetHitLayerWeight()
        {
            return _driver.GetHitLayerWeight();
        }

        public void SetHitLayerWeight(float weight)
        {
            _driver.SetHitLayerWeight(weight);
        }
    }
}