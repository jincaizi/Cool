using System;

namespace KcpServer.Movement
{

    /// <summary>
    /// ORCA 的自身状态（XZ 平面）。<see cref="PreferredVelocityX"/> / <see cref="PreferredVelocityZ"/>
    /// 是期望速度 v_pref（解算目标：尽量贴近它）。
    /// </summary>
    public struct OrcaAgent
    {
        public float PositionX;
        public float PositionZ;
        public float VelocityX;
        public float VelocityZ;
        public float Radius;
        public float MaxSpeed;
        public float PreferredVelocityX;
        public float PreferredVelocityZ;

        public OrcaAgent(float positionX, float positionZ, float velocityX, float velocityZ,
            float radius, float maxSpeed, float preferredVelocityX, float preferredVelocityZ)
        {
            PositionX = positionX;
            PositionZ = positionZ;
            VelocityX = velocityX;
            VelocityZ = velocityZ;
            Radius = radius;
            MaxSpeed = maxSpeed;
            PreferredVelocityX = preferredVelocityX;
            PreferredVelocityZ = preferredVelocityZ;
        }
    }

    /// <summary>
    /// ORCA 的邻居（XZ 平面）。<see cref="Responsibility"/> 是**自身**对这对关系承担的避让责任 α：
    /// 双方都参与（互惠）= 0.5；对方不参与（玩家 / 静止障碍，服务端改不了它的速度）= 1.0（自身全责）。
    /// 取值域 (0, 1]。
    /// </summary>
    public struct OrcaNeighbor
    {
        public float PositionX;
        public float PositionZ;
        public float VelocityX;
        public float VelocityZ;
        public float Radius;
        public float Responsibility;

        public OrcaNeighbor(float positionX, float positionZ, float velocityX, float velocityZ,
            float radius, float responsibility)
        {
            PositionX = positionX;
            PositionZ = positionZ;
            VelocityX = velocityX;
            VelocityZ = velocityZ;
            Radius = radius;
            Responsibility = responsibility;
        }
    }

    /// <summary>解算结果：XZ 平面速度。</summary>
    public struct OrcaVelocity
    {
        public float X;
        public float Z;

        public OrcaVelocity(float x, float z)
        {
            X = x;
            Z = z;
        }
    }

    /// <summary>
    /// ORCA（Optimal Reciprocal Collision Avoidance）2D 速度解算器 —— 纯算法，不需要任何世界几何：
    /// 每个邻居只需要相对位置/速度、半径和时间视界。
    ///
    /// 论文：J. van den Berg, S. J. Guy, M. Lin, D. Manocha, "Reciprocal n-Body Collision Avoidance",
    /// ISRR 2011（实现对应 §3 ORCA 半平面 + 附录 A 的线性规划）。
    ///
    /// 记号（下文注释统一使用，便于对照论文）：
    ///   p      = p_neighbor − p_self                 相对位置
    ///   v_rel  = v_self − v_neighbor                 相对速度
    ///   r      = r_self + r_neighbor                 合并半径
    ///   τ      = <see cref="OrcaSolver"/> 的 timeHorizon
    ///   顶点    = p/τ（VO 截断锥的 apex）、锥帽半径 = r/τ（paper 的 cut-off circle）
    ///   u      = 锥边界上离 v_rel 最近的点 − v_rel    论文的 u
    ///   n      = 该最近点的单位外法线（指向可行侧）     论文的 n
    ///   半平面  = { v : (v − (v_self + α·u)) · n ≥ 0 }，α = 责任权重（论文 §3.2）
    ///   目标    = 在全部半平面 + |v| ≤ maxSpeed 下最大化 v · v_pref
    ///
    /// 求解用论文附录 A 的增量线性规划，本文件三个例程：
    ///   LinearProgram1 —— 在一条边界线上求极值（满足已处理约束 + maxSpeed 圆）
    ///   LinearProgram2 —— 增量求解：逐条满足约束；失败时返回失败约束的下标
    ///   LinearProgram3 —— 约束集不可行时的回退：最小化最大违反量
    /// 注：2D 表述只有这三步；RVO2-3D 把"增量求解"叫 linearProgram3、把"最小化最大违反量"叫
    /// linearProgram4（多出的一格是 3D 的平面/多边形几何，2D 不需要）。
    ///
    /// 无分配热路径：邻居经 ReadOnlySpan 传入，内部约束缓冲随实例复用，不用 LINQ。
    /// 邻居顺序 = 约束顺序（同一输入必得同一输出）。实例持有复用缓冲，非线程安全（服务端主循环单线程）。
    ///
    /// **不要加"沿 v_pref 拉满到 maxSpeed"那一步**（曾经出现在任务指令里、被审查驳回）：
    /// 论文（§3 + 附录 A）没有该步骤，LP2 的起点就是"maxSpeed 圆上离 v_pref 最近的点"；
    /// 加上它会打破本类的护栏 —— 无邻居时不再逐字返回 v_pref（会让接线后的位置序列与接线前不等），
    /// 而且智能体在空旷处会一直全速跑（v_pref 慢下来时被重新拉满）。要限速就改调用方的 v_pref。
    /// </summary>
    public sealed class OrcaSolver
    {
        /// <summary>互惠避让：双方各承担一半（论文 §3.2 的 α = 1/2）</summary>
        public const float ReciprocalResponsibility = 0.5f;

        /// <summary>对方不参与避让（玩家 / 静止障碍）：自身全责（论文 §3.2 的 α = 1）</summary>
        public const float FullResponsibility = 1f;

        private const float ParallelEpsilon = 1e-5f;  // 边界线平行的判定阈值（单位向量）
        private const float MinLengthEpsilon = 1e-6f; // 退化长度阈值（避免除零）

        private readonly float _timeHorizon;
        private readonly float _timeStep;

        private HalfPlane[] _lines = new HalfPlane[0];
        private HalfPlane[] _projectedLines = new HalfPlane[0];

        /// <param name="timeHorizon">τ：只避让 τ 秒内会发生的碰撞（越大越早绕开、越保守）</param>
        /// <param name="timeStep">仿真步长 Δt：仅用于"已经重叠"时的分离推挤（见 BuildOrcaLine）</param>
        public OrcaSolver(float timeHorizon, float timeStep)
        {
            if (timeHorizon <= 0f) throw new ArgumentOutOfRangeException(nameof(timeHorizon));
            if (timeStep <= 0f) throw new ArgumentOutOfRangeException(nameof(timeStep));
            _timeHorizon = timeHorizon;
            _timeStep = timeStep;
        }

        public float TimeHorizon => _timeHorizon;

        public float TimeStep => _timeStep;

        /// <summary>上一次 Solve 是否走了 LinearProgram3 回退（约束集不可行）。诊断用。</summary>
        public bool LastSolveUsedFallback { get; private set; }

        /// <summary>
        /// 解算自身速度。neighbors 的顺序即约束顺序（确定性：同一输入必得同一输出）。
        /// 无邻居且 |v_pref| ≤ maxSpeed 时**逐位**返回 v_pref（接线护栏：不改变既有行为）。
        /// </summary>
        public OrcaVelocity Solve(in OrcaAgent self, ReadOnlySpan<OrcaNeighbor> neighbors)
        {
            LastSolveUsedFallback = false;

            V2 position = new V2(self.PositionX, self.PositionZ);
            V2 velocity = new V2(self.VelocityX, self.VelocityZ);
            V2 preferred = new V2(self.PreferredVelocityX, self.PreferredVelocityZ);

            EnsureCapacity(neighbors.Length);
            int lineCount = 0;
            for (int i = 0; i < neighbors.Length; i++)
            {
                if (BuildOrcaLine(self, position, velocity, neighbors[i], out HalfPlane line))
                    _lines[lineCount++] = line;
            }

            V2 result = default;
            int failedAt = LinearProgram2(_lines, lineCount, self.MaxSpeed, preferred, false, ref result);
            if (failedAt < lineCount)
            {
                // 约束集不可行（被挤在中间 / 已重叠）：退到"最小化最大违反量"
                LastSolveUsedFallback = true;
                LinearProgram3(_lines, lineCount, failedAt, self.MaxSpeed, ref result);
            }

            return new OrcaVelocity(result.X, result.Y);
        }

        /// <summary>
        /// 为一个邻居构造 ORCA 半平面（论文 §3.2）。返回 false 表示该邻居不产生约束（退化情形，
        /// 见方法内注释）。
        /// </summary>
        private bool BuildOrcaLine(in OrcaAgent self, V2 position, V2 velocity, in OrcaNeighbor other,
            out HalfPlane line)
        {
            V2 p = new V2(other.PositionX - position.X, other.PositionZ - position.Y);
            V2 vRel = new V2(velocity.X - other.VelocityX, velocity.Y - other.VelocityZ);
            float combinedRadius = self.Radius + other.Radius;
            float distSq = p.SqrMagnitude;
            float combinedRadiusSq = combinedRadius * combinedRadius;

            V2 normal; // 论文的 n
            V2 u;      // 论文的 u

            if (distSq > combinedRadiusSq)
            {
                // ---- 未重叠：最近点在"截断锥"的边界上（顶点 p/τ、锥帽半径 r/τ）----
                float invTimeHorizon = 1f / _timeHorizon;
                V2 w = vRel - p * invTimeHorizon; // 锥帽圆心 → v_rel
                float dotProduct1 = V2.Dot(w, p);

                if (dotProduct1 < 0f && dotProduct1 * dotProduct1 > combinedRadiusSq * w.SqrMagnitude)
                {
                    // 最近点在锥帽（cut-off circle）上：u 沿半径把 |w| 收到 r/τ
                    // （此分支下 w ≠ 0：条件要求 dotProduct1 ≠ 0）
                    float wLength = (float)Math.Sqrt(w.SqrMagnitude);
                    V2 unitW = w * (1f / wLength);
                    u = unitW * (combinedRadius * invTimeHorizon - wLength);
                    normal = unitW;
                }
                else
                {
                    // 最近点在锥的"腿"上：腿 = 过原点、与锥帽相切的直线（两条，取靠近 v_rel 的那条）。
                    // 腿的方程 v·n = 0，法线 n = (−r·p̂ ∓ leg·p̂⊥)/|p|，leg = √(|p|² − r²)，
                    // 符号由 v_rel 落在 p 的哪一侧（det(p, w) 的符号）决定。
                    float leg = (float)Math.Sqrt(distSq - combinedRadiusSq);
                    float invDist = 1f / (float)Math.Sqrt(distSq);
                    V2 pUnit = p * invDist;
                    V2 pUnitPerp = new V2(-pUnit.Y, pUnit.X); // p̂ 逆时针 90°
                    normal = V2.Det(p, w) > 0f
                        ? (pUnitPerp * leg - pUnit * combinedRadius) * invDist
                        : (pUnit * -combinedRadius - pUnitPerp * leg) * invDist;
                    // u = v_rel 到这条过原点直线的正交投影 − v_rel = −(v_rel · n)·n
                    u = normal * -V2.Dot(vRel, normal);
                }
            }
            else
            {
                // ---- 已重叠（distSq ≤ r²）：τ 锥在这里退化，改用仿真步长 Δt 代替 τ，保证
                //      重叠的双方在一个步长内被推开（顶点 p/Δt、锥帽半径 r/Δt）----
                float invTimeStep = 1f / _timeStep;
                V2 w = vRel - p * invTimeStep;
                float wLength = (float)Math.Sqrt(w.SqrMagnitude);
                if (wLength < MinLengthEpsilon)
                {
                    // w = 0：相对速度恰为 p/Δt，双方正以一个步长分离的速度互相远离 → 无需约束。
                    // 位置完全重合且相对速度为零时同样落到这里：几何上不存在"该往哪个方向让"，
                    // 与其编一个方向（拿零长度向量去除会得到 NaN 并污染整条解算链），不如不产生约束。
                    line = default;
                    return false;
                }
                V2 unitW = w * (1f / wLength);
                u = unitW * (combinedRadius * invTimeStep - wLength);
                normal = unitW;
            }

            // 责任分摊（论文 §3.2）：自身只让 α·u，锚点 = v_self + α·u。
            // α = 0.5 → 双方各让一半（互惠）；α = 1 → 对方不动，我们全让。
            line = new HalfPlane(velocity + u * other.Responsibility, normal);
            return true;
        }

        /// <summary>
        /// 论文附录 A 的 linearProgram1：在 lines[lineNo] 的边界线上求极值，
        /// 同时满足 lines[0..lineNo) 与 |v| ≤ radius。线与 maxSpeed 圆不相交、或与已处理约束
        /// 无交集时返回 false。
        /// </summary>
        private static bool LinearProgram1(HalfPlane[] lines, int lineNo, float radius, V2 optVelocity,
            bool directionOpt, ref V2 result)
        {
            HalfPlane line = lines[lineNo];
            V2 direction = line.Direction; // 边界线单位方向：参数化 v = line.Point + t·direction
            float dotProduct = V2.Dot(line.Point, direction);

            // |line.Point + t·direction| ≤ radius 解出 t 区间
            float discriminant = dotProduct * dotProduct + radius * radius - line.Point.SqrMagnitude;
            if (discriminant < 0f) return false;

            float sqrtDiscriminant = (float)Math.Sqrt(discriminant);
            float tLeft = -dotProduct - sqrtDiscriminant;
            float tRight = -dotProduct + sqrtDiscriminant;

            for (int i = 0; i < lineNo; i++)
            {
                HalfPlane other = lines[i];
                // 把 v = line.Point + t·direction 代入约束 i：(v − other.Point)·other.Normal ≥ 0 化为
                // t·a ≥ c，其中 a = direction·other.Normal，c = (other.Point − line.Point)·other.Normal
                // （= 锚点 line.Point 处对约束 i 的违反量，c > 0 表示锚点已经违规）
                float a = V2.Dot(direction, other.Normal);
                float c = other.Violation(line.Point);
                if (a > -ParallelEpsilon && a < ParallelEpsilon)
                {
                    // 边界线平行：整条线要么全在约束 i 的可行侧，要么完全不可行
                    if (c > 0f) return false;
                    continue;
                }

                float t = c / a;
                if (a > 0f)
                {
                    if (t > tLeft) tLeft = t;
                }
                else
                {
                    if (t < tRight) tRight = t;
                }
                if (tLeft > tRight) return false;
            }

            if (directionOpt)
            {
                // 最大化 v·optVelocity：取区间上顺着 optVelocity 的那一端
                result = line.Point + direction * (V2.Dot(optVelocity, direction) > 0f ? tRight : tLeft);
            }
            else
            {
                // 最小化 |v − optVelocity|：把期望速度投影到线上，再夹进可行区间
                float t = V2.Dot(optVelocity - line.Point, direction);
                if (t < tLeft) t = tLeft;
                else if (t > tRight) t = tRight;
                result = line.Point + direction * t;
            }
            return true;
        }

        /// <summary>
        /// 论文附录 A 的 linearProgram2：增量求解。从"maxSpeed 圆上离 v_pref 最近的点"出发，
        /// 逐条检查约束；违反时用 linearProgram1 在该约束边界线上重求极值。
        /// 返回 lineCount = 成功；否则返回失败约束的下标（它与此前的约束不可同时满足）。
        /// </summary>
        private static int LinearProgram2(HalfPlane[] lines, int lineCount, float radius, V2 optVelocity,
            bool directionOpt, ref V2 result)
        {
            if (directionOpt)
            {
                result = optVelocity * radius;
            }
            else if (optVelocity.SqrMagnitude > radius * radius)
            {
                // 期望速度超速：截断到 maxSpeed 圆（无约束时的最优解）
                result = optVelocity * (radius / (float)Math.Sqrt(optVelocity.SqrMagnitude));
            }
            else
            {
                result = optVelocity;
            }

            for (int i = 0; i < lineCount; i++)
            {
                if (lines[i].Violation(result) > 0f)
                {
                    V2 previous = result;
                    if (!LinearProgram1(lines, i, radius, optVelocity, directionOpt, ref result))
                    {
                        result = previous;
                        return i;
                    }
                }
            }
            return lineCount;
        }

        /// <summary>
        /// 论文附录 A 的回退（RVO2-3D 里的 linearProgram4）：约束集不可行时最小化**最大违反量**。
        /// 依次处理违反量超过当前最大值的约束 i：把 lines[0..i) 投影到线 i 上（降维 LP），
        /// 再沿线 i 的外法线方向最大化 —— 得到"在尽量满足其它约束的前提下，沿线 i 走得最远"的解。
        /// beginLine = linearProgram2 失败的下标（增量：之前的约束互相自洽）。
        /// </summary>
        private void LinearProgram3(HalfPlane[] lines, int lineCount, int beginLine, float radius, ref V2 result)
        {
            float distance = 0f;
            for (int i = beginLine; i < lineCount; i++)
            {
                float violation = lines[i].Violation(result);
                if (violation <= distance) continue;

                int projectedCount = 0;
                for (int j = 0; j < i; j++)
                {
                    HalfPlane other = lines[j];
                    float determinant = V2.Det(lines[i].Normal, other.Normal);
                    V2 point;
                    if (Math.Abs(determinant) <= ParallelEpsilon)
                    {
                        // 平行：同向 → 投影退化，跳过；反向 → 两条平行边界线之间取中点
                        if (V2.Dot(lines[i].Normal, other.Normal) > 0f) continue;
                        point = (lines[i].Point + other.Point) * 0.5f;
                    }
                    else
                    {
                        // 沿线 i 走到与线 j 边界的交点
                        float t = V2.Dot(lines[i].Point - other.Point, other.Normal) / determinant;
                        point = lines[i].Point + lines[i].Direction * t;
                    }

                    // 投影线的外法线 = 两条法线的夹角平分线方向（把约束 j 折算到线 i 的切空间）
                    V2 projectedNormal = other.Normal - lines[i].Normal;
                    float projectedLength = (float)Math.Sqrt(projectedNormal.SqrMagnitude);
                    _projectedLines[projectedCount++] = new HalfPlane(point, projectedNormal * (1f / projectedLength));
                }

                V2 previous = result;
                int solved = LinearProgram2(_projectedLines, projectedCount, radius, lines[i].Normal, true, ref result);
                if (solved < projectedCount)
                {
                    // 理论上不会发生（当前解的违反量已是最小）；浮点误差下保留上一次结果
                    result = previous;
                }
                distance = lines[i].Violation(result);
            }
        }

        private void EnsureCapacity(int neighborCount)
        {
            if (_lines.Length < neighborCount) _lines = new HalfPlane[neighborCount];
            if (_projectedLines.Length < neighborCount) _projectedLines = new HalfPlane[neighborCount];
        }

        /// <summary>半平面 { v : (v − Point)·Normal ≥ 0 }，Normal 为单位外法线（指向可行侧）</summary>
        private struct HalfPlane
        {
            public V2 Point;
            public V2 Normal;

            public HalfPlane(V2 point, V2 normal)
            {
                Point = point;
                Normal = normal;
            }

            /// <summary>边界线单位方向（LP 的参数化方向）：把 Normal 顺时针转 90°</summary>
            public V2 Direction => new V2(Normal.Y, -Normal.X);

            /// <summary>违反量 (Point − v)·Normal；&gt; 0 表示 v 落在半平面外</summary>
            public float Violation(V2 v) => V2.Dot(Point - v, Normal);
        }

        /// <summary>内部 2D 向量（X = 世界 X，Y = 世界 Z）</summary>
        private struct V2
        {
            public float X;
            public float Y;

            public V2(float x, float y)
            {
                X = x;
                Y = y;
            }

            public float SqrMagnitude => X * X + Y * Y;

            public static V2 operator +(V2 a, V2 b) => new V2(a.X + b.X, a.Y + b.Y);

            public static V2 operator -(V2 a, V2 b) => new V2(a.X - b.X, a.Y - b.Y);

            public static V2 operator *(V2 a, float scalar) => new V2(a.X * scalar, a.Y * scalar);

            public static float Dot(V2 a, V2 b) => a.X * b.X + a.Y * b.Y;

            /// <summary>2D 叉积的 z 分量 det(a, b) = a.X·b.Y − a.Y·b.X</summary>
            public static float Det(V2 a, V2 b) => a.X * b.Y - a.Y * b.X;
        }
    }
}
