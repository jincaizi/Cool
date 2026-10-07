using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Camera
{
    /// <summary>
    /// 第三人称相机控制器 — 平滑跟随
    /// </summary>
    public class ThirdPersonCameraController : MonoBehaviour
    {
        [Header("Target")]
        public Transform Target;

        [Header("Distance & Height")]
        public float Distance = 8.0f;
        public float Height = 2.0f;

        [Header("Damping")]
        public float PositionDamping = 5.0f;
        public float RotationDamping = 8.0f;

        [Header("Rotation Limits")]
        public float MinPitch = -30f;
        public float MaxPitch = 60f;
        public float MouseSensitivityX = 2.0f;
        public float MouseSensitivityY = 2.0f;

        private const float CameraCollisionRadius = 0.25f;
        private const float MinCameraDistance = 0.8f;
        /// <summary>相机碰撞层——触发器由 QueryTriggerInteraction.Ignore 排除；
        /// 跟随目标自身的碰撞体在代码里按层级剔除（掩码无法区分玩家子物体与场景几何），
        /// 故默认 ~0（Everything）即可，无需在 Inspector 排除玩家</summary>
        [SerializeField] private LayerMask CameraCollisionMask = ~0;

        // 当前旋转角度
        private float _horizontalAngle;
        private float _verticalAngle = 20f;

        private bool _anglesInitialized;

        private void Start()
        {
            // 延迟初始化角度，等待 Target 被设置
            _anglesInitialized = false;
            _verticalAngle = 20f;
        }

        private void EnsureAnglesInitialized()
        {
            if (_anglesInitialized) return;

            if (Target != null)
            {
                _horizontalAngle = Target.eulerAngles.y;
                _anglesInitialized = true;
            }
            else
            {
                // 使用相机自身的旋转作为初始角度
                _horizontalAngle = transform.eulerAngles.y;
                _anglesInitialized = true;
            }
        }

        /// <summary>
        /// 处理相机旋转输入
        /// </summary>
        public void HandleRotationInput(Vector2 input)
        {
            if (input.sqrMagnitude < 0.001f) return;

            _horizontalAngle += input.x * MouseSensitivityX;
            _verticalAngle -= input.y * MouseSensitivityY;

            // 限制俯仰角
            _verticalAngle = Mathf.Clamp(_verticalAngle, MinPitch, MaxPitch);
        }

        /// <summary>
        /// P6 ③：改为 LateUpdate 由 Unity 驱动。
        /// 原来由 Sys3CEntry 手动调用以"保证角色更新完再更新相机"，但该类本身是 MonoBehaviour
        /// 且方法是 Update → 每帧执行两次（插值在低帧手机上行为漂移）。
        /// LateUpdate 由 Unity 保证在所有 Update 之后执行，既保留顺序语义又消除双更新。
        /// </summary>
        private void LateUpdate()
        {
            if (Target == null) return;

            // 确保角度已初始化
            EnsureAnglesInitialized();

            // 计算目标位置（球坐标）
            Quaternion rotation = Quaternion.Euler(_verticalAngle, _horizontalAngle, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -Distance);
            offset.y = Height;

            Vector3 targetPosition = Target.position + offset;

            // P6 ③：防穿墙——从跟随目标向期望相机位投射，命中则把相机拉到碰撞点内侧。
            // 无此处理时贴墙/被怪包围会穿模，PC 上尤其刺眼。
            // 起投点复用下方注视点 lookTarget（与注视同源），避免相机拉近时朝向跳变。
            Vector3 lookTarget = Target.position + Vector3.up * Height * 0.5f;
            Vector3 castVector = targetPosition - lookTarget;
            float castDistance = castVector.magnitude;
            if (castDistance > 0.01f)
            {
                Vector3 castDirection = castVector / castDistance;
                // 起投点位于角色体内，只用单个 SphereCast 会先命中玩家自身碰撞体
                //（起始重叠 → distance=0），相机被拉到最近距离贴在脑后。
                // 掩码无法区分"玩家子物体"与场景几何（玩家子物体也在 Default 层），
                // 故用 SphereCastAll 按层级剔除 Target 自身的碰撞体，取最近的合法命中。
                var hits = Physics.SphereCastAll(lookTarget, CameraCollisionRadius, castDirection,
                    castDistance, CameraCollisionMask, QueryTriggerInteraction.Ignore);

                float nearestDistance = float.MaxValue;
                for (int i = 0; i < hits.Length; i++)
                {
                    if (IsOwnCollider(hits[i].collider)) continue;
                    if (hits[i].distance < nearestDistance)
                        nearestDistance = hits[i].distance;
                }

                if (nearestDistance < float.MaxValue)
                    targetPosition = lookTarget + castDirection * Mathf.Max(MinCameraDistance, nearestDistance);
            }

            Vector3 currentPosition = transform.position;

            // 平滑跟随
            transform.position = Vector3.Lerp(currentPosition, targetPosition, PositionDamping * Time.deltaTime);

            // 平滑看向目标（lookTarget 已在防穿墙段前算出并复用）
            Quaternion targetRotation = Quaternion.LookRotation(lookTarget - transform.position);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                RotationDamping * Time.deltaTime
            );
        }

        /// <summary>
        /// 命中的碰撞体是否属于跟随目标自身（Target 及其子物体）——防穿墙需跳过，
        /// 否则起投点在角色体内会命中自己，把相机拉到最近距离。
        /// </summary>
        private bool IsOwnCollider(Collider collider)
        {
            if (collider == null) return false;
            Transform t = collider.transform;
            return t == Target || t.IsChildOf(Target);
        }

        /// <summary>
        /// 获取当前相机旋转
        /// </summary>
        public Quaternion GetRotation()
        {
            return Quaternion.Euler(_verticalAngle, _horizontalAngle, 0f);
        }

        /// <summary>
        /// 立即将相机对齐到目标位置（无平滑）
        /// </summary>
        public void SnapToTarget()
        {
            if (Target == null) return;

            EnsureAnglesInitialized();

            // 立即设置相机位置
            Quaternion rotation = Quaternion.Euler(_verticalAngle, _horizontalAngle, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -Distance);
            offset.y = Height;

            transform.position = Target.position + offset;

            // 立即看向目标
            Vector3 lookTarget = Target.position + Vector3.up * Height * 0.5f;
            transform.rotation = Quaternion.LookRotation(lookTarget - transform.position);
        }
    }
}
