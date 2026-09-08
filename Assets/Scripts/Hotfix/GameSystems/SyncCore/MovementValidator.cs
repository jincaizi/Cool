namespace Cool.SyncCore
{
    /// <summary>移动校验结果（决策 D1/D2：共享规则，两端同源）</summary>
    public readonly struct MoveValidationResult
    {
        public readonly bool NeedsCorrection;
        public readonly Vec3 AuthoritativePosition;
        public readonly float AuthoritativeRotation;
        public readonly float ClampedSpeed;

        public MoveValidationResult(bool needsCorrection, Vec3 authoritativePosition, float authoritativeRotation, float clampedSpeed)
        {
            NeedsCorrection = needsCorrection;
            AuthoritativePosition = authoritativePosition;
            AuthoritativeRotation = authoritativeRotation;
            ClampedSpeed = clampedSpeed;
        }
    }

    /// <summary>
    /// 移动校验规则（spec B+：客户端报位置 + 服务器合理性校验）——
    /// 判定逻辑从服务器 PositionSyncHandler 移植并共享；纠偏目标位置改进为
    /// 沿 next−prev 真实移动方向推算（原实现用固定 Z 轴，不按玩家朝向）。
    ///
    /// 已知限制（P2 决策，P3 反作弊再评估）：
    /// ① 瞬移判定双门槛 `distance > MaxSpeed×dt×1.2 && distance > 50m`——正常上报窗口
    ///    （dt &lt; 2.78s）内 50m 绝对门槛主导，时间基分支仅在单报 &gt;50m 时生效；
    /// ② dt=0（时间戳未走）整段跳过瞬移检查（Zero_TimeDelta 测试固化该语义）；
    /// ③ 时间戳完全由客户端上报——冻结/伪造时间戳可绕过瞬移检查（需服务端接收时刻或单调计数）；
    /// ④ 跳高仅标记（NeedsCorrection=true）不钳制 Y——权威位置 = 客户端自报值，客户端侧无实际拉回。
    /// </summary>
    public static class MovementValidator
    {
        public const float MaxSpeed = 15f;
        public const float MaxTeleportDistance = 50f;
        public const float MaxJumpHeight = 3f;
        private const float SpeedToleranceFactor = 1.2f;

        /// <summary>
        /// 校验一条位置上报。
        /// prevTimestamp/nextTimestamp 单位：ticks（服务器时钟语义，spec §3）。
        /// </summary>
        public static MoveValidationResult Validate(
            Vec3 prev, Vec3 next, float rotation, float speed,
            long prevTimestamp, long nextTimestamp)
        {
            // 非有限输入守卫（损坏/恶意网络包可含 NaN/∞：NaN 比较恒 false 穿透全部阈值，
            // Infinity 归一化产出 NaN 纠偏位）——直接纠偏回 prev 已知位置，杜绝 NaN 经广播扩散
            if (!IsFinite(prev) || !IsFinite(next) || !IsFinite(rotation) || !IsFinite(speed))
            {
                return new MoveValidationResult(true, prev, rotation, 0f);
            }

            bool needsCorrection = false;
            Vec3 authoritative = next;
            float authoritativeRotation = rotation;
            float clampedSpeed = speed;

            // 1) 超速
            if (speed > MaxSpeed)
            {
                clampedSpeed = MaxSpeed;
                needsCorrection = true;
            }

            // 2) 瞬移（时间窗内距离超物理可能）
            long timeDelta = nextTimestamp - prevTimestamp;
            float timeDeltaSeconds = timeDelta / (float)System.TimeSpan.TicksPerSecond;
            if (timeDeltaSeconds > 0f)
            {
                float distance = prev.Distance(next);
                float maxPossibleDistance = MaxSpeed * timeDeltaSeconds * SpeedToleranceFactor;
                if (distance > maxPossibleDistance && distance > MaxTeleportDistance)
                {
                    needsCorrection = true;
                    // 纠偏目标：沿真实移动方向（next−prev 归一化）按最大可行速度推算
                    Vec3 dir = (next - prev).Normalized();
                    authoritative = prev + dir * (MaxSpeed * timeDeltaSeconds);
                }
            }

            // 3) 异常跳高（垂直位移超物理）
            float verticalDelta = next.Y - prev.Y;
            if (verticalDelta > MaxJumpHeight)
            {
                needsCorrection = true;
            }

            return new MoveValidationResult(needsCorrection, authoritative, authoritativeRotation, clampedSpeed);
        }

        private static bool IsFinite(Vec3 v)
            => IsFinite(v.X) && IsFinite(v.Y) && IsFinite(v.Z);

        private static bool IsFinite(float v)
            => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
