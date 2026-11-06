using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>地面瞄准的纯数学：相机相对方向、幅度距离映射、射程钳制。</summary>
    public static class SkillAimMath
    {
        public static Vector3 CameraRelativeDirection(Vector2 aim, Vector3 cameraForward)
        {
            Vector3 flat = new Vector3(cameraForward.x, 0f, cameraForward.z);
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
            flat.Normalize();

            Vector3 world = Quaternion.LookRotation(flat) * new Vector3(aim.x, 0f, aim.y);
            return world.sqrMagnitude > 0.0001f ? world.normalized : flat;
        }

        public static float DistanceFromMagnitude(float magnitude, float min, float max)
            => Mathf.Lerp(min, max, Mathf.Clamp01(magnitude));

        public static Vector3 ClampToRange(Vector3 origin, Vector3 target, float maxDistance)
        {
            Vector3 flat = target - origin;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance <= maxDistance || distance < 0.0001f) return target;

            Vector3 clamped = origin + flat / distance * maxDistance;
            clamped.y = target.y;
            return clamped;
        }
    }
}
