using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>地面瞄准的纯数学：相机相对方向、幅度距离映射、射程钳制、默认落点解析。</summary>
    public static class SkillAimMath
    {
        /// <summary>网络上报落点的射程余量（米）。服务端只容忍 MaxImpactDistance + 0.01，
        /// 客户端位置同步抖动 &gt; 1cm 就会被 impact-too-far 拒收（冷却被吃、无伤害）。
        /// 视觉圈仍可显示到 ProjectileRange；上报必须钳到 ProjectileRange − 该值。</summary>
        public const float ReportDistanceMargin = 0.05f;

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

        /// <summary>
        /// 点按（死区内 / 未拖动）的默认落点：有选中目标 → 沿 owner→目标方向钳到 maxDistance
        /// （保留目标 Y，由调用方吸附地面）；无选中目标 → 角色水平 forward × maxDistance。
        /// </summary>
        public static Vector3 ResolveDefaultTarget(Vector3 ownerPosition, Vector3 forward,
            float maxDistance, Vector3? selectedTarget)
        {
            if (selectedTarget.HasValue)
                return ClampToRange(ownerPosition, selectedTarget.Value, maxDistance);

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            return ownerPosition + forward.normalized * maxDistance;
        }
    }
}
