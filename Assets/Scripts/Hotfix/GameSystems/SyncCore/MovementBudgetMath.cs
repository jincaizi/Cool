using System;
using Cool.Protocol;

namespace Cool.SyncCore
{
    /// <summary>PvP 位移预算纯函数（Plan 03）：只接收服务端 tick，客户端时间戳不参与。</summary>
    public static class MovementBudgetMath
    {
        /// <summary>长间隔累积上限（秒）。</summary>
        public const float MaxAccumulatedSeconds = 0.5f;

        /// <summary>15Hz 下 1 tick 的秒数（突进按 DashDistance + 1 帧封顶）。</summary>
        public const float OneTickSeconds = 1f / CombatRules.TickRateHz;

        public static float ClampDeltaSeconds(uint nowTick, uint lastTick)
        {
            if (lastTick == 0 || nowTick <= lastTick) return 0f;
            float seconds = (nowTick - lastTick) / CombatRules.TickRateHz;
            return seconds > MaxAccumulatedSeconds ? MaxAccumulatedSeconds : seconds;
        }

        public static float NormalDistance(LocomotionMode mode, bool defending,
            float statusScale, float seconds)
        {
            if (!(seconds > 0f) || !float.IsFinite(seconds)) return 0f;
            float baseSpeed = mode == LocomotionMode.Sprint ? CombatRules.SprintSpeed : CombatRules.WalkSpeed;
            float defendScale = defending ? CombatRules.DefendSpeedMultiplier : 1f;
            float scale = float.IsFinite(statusScale) ? Math.Clamp(statusScale, 0f, 4f) : 0f;
            return baseSpeed * defendScale * scale * seconds;
        }

        public static float DashSpeed(float distance, float duration, float startup, float recovery)
        {
            float active = duration - startup - recovery;
            if (!(distance > 0f) || !(active > 0f)) return 0f;
            return distance / active;
        }

        public static float DashDistance(float dashSpeed, float statusScale,
            float seconds, float remaining)
        {
            if (!(dashSpeed > 0f) || !(seconds > 0f) || !(remaining > 0f)) return 0f;
            float scale = float.IsFinite(statusScale) ? Math.Clamp(statusScale, 0f, 4f) : 0f;
            float granted = dashSpeed * scale * seconds;
            return Math.Min(granted, remaining);
        }

        public static float JumpInertiaDistance(float speed, float seconds, float remaining)
        {
            if (!(speed > 0f) || !(seconds > 0f) || !(remaining > 0f)) return 0f;
            return Math.Min(speed * seconds, remaining);
        }

        public static Vec3 ClampHorizontal(Vec3 previous, Vec3 next, float allowedDistance)
        {
            float dx = next.X - previous.X;
            float dz = next.Z - previous.Z;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (!(distance > allowedDistance) || distance <= 0f) return next;

            float scale = Math.Max(0f, allowedDistance) / distance;
            return new Vec3(previous.X + dx * scale, next.Y, previous.Z + dz * scale);
        }

        public static float MaxJumpY(float startY, float jumpForce, float gravity, float elapsedSeconds)
        {
            if (!(gravity < 0f) || !(jumpForce > 0f)) return startY;
            float apex = jumpForce / -gravity;
            float t = Math.Clamp(elapsedSeconds, 0f, apex);
            return startY + jumpForce * t + 0.5f * gravity * t * t;
        }
    }
}
