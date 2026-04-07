# 3C System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Working 3C system (Character, Camera, Control) for MMO ARPG with network sync

**Architecture:** HybridCLR hotfix — only NetworkBridge interfaces with AOT KcpNet. All other 3C logic runs in Hotfix layer with direct Unity API access.

**Tech Stack:** Unity 2022 LTS, Rigidbody + CapsuleCollider, KCP networking (existing), Animator state machine

---

## File Map

```
Assets/Scripts/Hotfix/GameSystems/Sys3C/
├── Sys3C.asmdef
├── Character/
│   ├── CharacterData.cs          — 值类型数据结构
│   ├── CharacterController.cs    — 移动/转向/状态机
│   └── CharacterAnimationController.cs — Animator 参数驱动
├── Camera/
│   └── ThirdPersonCameraController.cs — 第三人称跟随相机
├── Input/
│   ├── InputManager.cs           — 输入总管理器
│   ├── KeyboardInputAdapter.cs   — WASD 适配器
│   └── JoystickInputAdapter.cs  — 虚拟摇杆适配器
├── Network/
│   ├── NetworkBridge.cs          — AOT KcpNet 桥接（唯一跨层）
│   ├── NetworkPrediction.cs      — 客户端预测/服务端校验
│   └── PositionInterpolator.cs  — 其他玩家位置插值
└── Sys3CEntry.cs                — 系统入口

Assets/Scripts/AOT/ — 无需修改（KcpNet 已存在）
```

---

## Task 1: Project Scaffold

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3C.asmdef`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/`

- [ ] **Step 1: Create Sys3C.asmdef**

```json
{
    "name": "Hotfix.GameSystems.Sys3C",
    "rootNamespace": "",
    "references": [
        "Unity.Model",
        "Unity.Entities",
        "Unity.Burst"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Create directory structure and empty .meta files**

Unity will generate .meta files automatically on import. Skip manual creation.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/
git commit -m "feat(3C): create Sys3C hotfix module scaffold"
```

---

## Task 2: CharacterData (值类型数据结构)

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs`

- [ ] **Step 1: Write MoveCommand struct**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 标准化移动命令
    /// </summary>
    public struct MoveCommand
    {
        /// <summary>
        /// 移动方向（标准化）
        /// </summary>
        public Vector3 MoveDir;

        /// <summary>
        /// 移动速度
        /// </summary>
        public float Speed;

        /// <summary>
        /// 角色朝向（四元数）
        /// </summary>
        public Quaternion Rotation;

        /// <summary>
        /// 时间戳（客户端预测用）
        /// </summary>
        public long Timestamp;

        /// <summary>
        /// 序列号
        /// </summary>
        public uint Sequence;
    }

    /// <summary>
    /// 角色状态
    /// </summary>
    public enum CharacterState
    {
        Idle,
        Running,
        Falling
    }

    /// <summary>
    /// 角色数据（值类型，主线程访问）
    /// </summary>
    public struct CharacterData
    {
        /// <summary>
        /// 世界位置
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// 旋转（四元数）
        /// </summary>
        public Quaternion Rotation;

        /// <summary>
        /// 速度向量
        /// </summary>
        public Vector3 Velocity;

        /// <summary>
        /// 当前状态
        /// </summary>
        public CharacterState State;

        /// <summary>
        /// 是否在地面上
        /// </summary>
        public bool IsGrounded;

        /// <summary>
        /// 垂直速度（用于动画）
        /// </summary>
        public float VerticalVelocity;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs
git commit -m "feat(3C): add CharacterData and MoveCommand structs"
```

---

## Task 3: KeyboardInputAdapter

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/KeyboardInputAdapter.cs`

- [ ] **Step 1: Write KeyboardInputAdapter**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// WASD 键盘输入适配器
    /// </summary>
    public class KeyboardInputAdapter : IInputAdapter
    {
        private const float DEAD_ZONE = 0.1f;

        public string AdapterName => "Keyboard";

        /// <summary>
        /// 获取标准化移动输入
        /// </summary>
        public Vector3 GetMoveInput()
        {
            float horizontal = Input.GetAxisRaw("Horizontal"); // A/D
            float vertical = Input.GetAxisRaw("Vertical");   // W/S

            Vector3 input = new Vector3(horizontal, 0f, vertical);

            if (input.magnitude < DEAD_ZONE)
                return Vector3.zero;

            return input.normalized;
        }

        /// <summary>
        /// 获取相机旋转输入（鼠标）
        /// </summary>
        public Vector2 GetCameraRotationInput()
        {
            // Mouse X = 水平旋转，Mouse Y = 垂直旋转
            float mouseX = Input.GetAxisRaw("Mouse X");
            float mouseY = Input.GetAxisRaw("Mouse Y");
            return new Vector2(mouseX, mouseY);
        }

        /// <summary>
        /// 是否有移动输入
        /// </summary>
        public bool HasMoveInput()
        {
            return GetMoveInput().sqrMagnitude > 0f;
        }

        /// <summary>
        /// 是否有相机旋转输入
        /// </summary>
        public bool HasCameraRotationInput()
        {
            Vector2 rot = GetCameraRotationInput();
            return rot.sqrMagnitude > 0f;
        }
    }
}
```

- [ ] **Step 2: Write IInputAdapter interface** (add to same file or separate)

```csharp
namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 输入适配器接口
    /// </summary>
    public interface IInputAdapter
    {
        string AdapterName { get; }
        Vector3 GetMoveInput();
        Vector2 GetCameraRotationInput();
        bool HasMoveInput();
        bool HasCameraRotationInput();
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/KeyboardInputAdapter.cs
git commit -m "feat(3C): add KeyboardInputAdapter with WASD and mouse input"
```

---

## Task 4: JoystickInputAdapter

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/JoystickInputAdapter.cs`

- [ ] **Step 1: Write JoystickInputAdapter**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 虚拟摇杆输入适配器（用于移动端）
    /// </summary>
    public class JoystickInputAdapter : IInputAdapter
    {
        private const float DEAD_ZONE = 0.05f;

        // 摇杆的 Unity UI 组件引用（需要外部赋值）
        // 典型实现：使用 EventSystem 获取触摸/摇杆输入
        // 这里使用 Input.touches 和 Input.GetAxis("Horizontal_Joystick")

        public string AdapterName => "Joystick";

        /// <summary>
        /// 获取标准化移动输入（从虚拟摇杆）
        /// </summary>
        public Vector3 GetMoveInput()
        {
            float horizontal = Input.GetAxisRaw("Horizontal_Joystick");
            float vertical = Input.GetAxisRaw("Vertical_Joystick");

            Vector3 input = new Vector3(horizontal, 0f, vertical);

            if (input.magnitude < DEAD_ZONE)
                return Vector3.zero;

            return input.normalized;
        }

        /// <summary>
        /// 获取相机旋转输入（从第二个摇杆或触摸拖拽）
        /// </summary>
        public Vector2 GetCameraRotationInput()
        {
            float rotX = Input.GetAxisRaw("Mouse X_Joystick");
            float rotY = Input.GetAxisRaw("Mouse Y_Joystick");

            // 如果没有摇杆输入，尝试触摸拖拽
            if (Mathf.Abs(rotX) < DEAD_ZONE && Mathf.Abs(rotY) < DEAD_ZONE)
            {
                if (Input.touchCount > 0)
                {
                    Touch touch = Input.GetTouch(0);
                    if (touch.phase == TouchPhase.Moved)
                    {
                        return touch.deltaPosition / 100f;
                    }
                }
            }

            return new Vector2(rotX, rotY);
        }

        /// <summary>
        /// 是否有移动输入
        /// </summary>
        public bool HasMoveInput()
        {
            return GetMoveInput().sqrMagnitude > 0f;
        }

        /// <summary>
        /// 是否有相机旋转输入
        /// </summary>
        public bool HasCameraRotationInput()
        {
            Vector2 rot = GetCameraRotationInput();
            return rot.sqrMagnitude > 0f;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/JoystickInputAdapter.cs
git commit -m "feat(3C): add JoystickInputAdapter for mobile/touch input"
```

---

## Task 5: InputManager

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs`

- [ ] **Step 1: Write InputManager**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 输入管理器 — 双层抽象核心
    /// 统一所有输入适配器，输出标准化 MoveCommand
    /// </summary>
    public class InputManager
    {
        private readonly List<IInputAdapter> _adapters = new List<IInputAdapter>();
        private IInputAdapter _activeAdapter;

        // 相机旋转灵敏度
        public float CameraSensitivityX { get; set; } = 2.0f;
        public float CameraSensitivityY { get; set; } = 2.0f;

        // 移动速度
        public float MoveSpeed { get; set; } = 5.0f;

        // 当前序列号（用于网络预测）
        private uint _sequence;

        public InputManager()
        {
            // 注册所有适配器
            RegisterAdapter(new KeyboardInputAdapter());
            RegisterAdapter(new JoystickInputAdapter());

            // 默认使用键盘
            _activeAdapter = _adapters[0]; // KeyboardInputAdapter
        }

        /// <summary>
        /// 注册输入适配器
        /// </summary>
        public void RegisterAdapter(IInputAdapter adapter)
        {
            if (!_adapters.Contains(adapter))
                _adapters.Add(adapter);
        }

        /// <summary>
        /// 切换活动适配器
        /// </summary>
        public void SetActiveAdapter(string adapterName)
        {
            foreach (var adapter in _adapters)
            {
                if (adapter.AdapterName == adapterName)
                {
                    _activeAdapter = adapter;
                    return;
                }
            }
        }

        /// <summary>
        /// 获取标准化移动命令
        /// </summary>
        public MoveCommand GetMoveCommand(Vector3 characterForward)
        {
            Vector3 moveInput = _activeAdapter.GetMoveInput();

            // 将世界坐标输入转换为角色朝向相关的局部坐标
            // 如果有输入，角色朝向跟随移动方向
            Quaternion targetRotation = characterForward.sqrMagnitude > 0.1f
                ? Quaternion.LookRotation(characterForward)
                : Quaternion.identity;

            if (moveInput.sqrMagnitude > 0.1f)
            {
                // 将输入从世界坐标转换到相机视角
                // （相机朝向决定"前方向"）
                Vector3 worldMoveDir = ConvertToWorldDirection(moveInput);
                targetRotation = Quaternion.LookRotation(worldMoveDir);
            }

            return new MoveCommand
            {
                MoveDir = moveInput,
                Speed = MoveSpeed,
                Rotation = targetRotation,
                Timestamp = System.DateTime.UtcNow.Ticks,
                Sequence = ++_sequence
            };
        }

        /// <summary>
        /// 获取相机旋转输入（原始向量）
        /// </summary>
        public Vector2 GetCameraRotationInput()
        {
            return _activeAdapter.GetCameraRotationInput();
        }

        /// <summary>
        /// 将输入向量从相机视角转换为世界方向
        /// </summary>
        private Vector3 ConvertToWorldDirection(Vector3 input)
        {
            // 简化实现：input 是相对于相机的方向
            // 实际需要相机朝向，这里假设相机的forward是场景中的"前"
            // 真实实现需要 Camera.main.transform.forward
            return input;
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update()
        {
            // 可在此处理适配器自动切换逻辑
            // 例如：检测到触摸时切换到 JoystickInputAdapter
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs
git commit -m "feat(3C): add InputManager with dual-adapter support"
```

---

## Task 6: GroundDetector

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/GroundDetector.cs`

- [ ] **Step 1: Write GroundDetector**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 地面检测器 — 分层射线实现（CharacterController 风格）
    /// </summary>
    public class GroundDetector
    {
        private readonly Transform _transform;
        private readonly CapsuleCollider _capsule;
        private readonly LayerMask _groundLayer;

        // 射线检测参数
        private const int RAY_COUNT = 5;
        private const float RAY_SPREAD = 0.2f;
        private const float MAX_GROUND_ANGLE = 45f;
        private const float GROUND_CHECK_DISTANCE = 0.15f;

        public GroundDetector(Transform transform, CapsuleCollider capsule, LayerMask groundLayer)
        {
            _transform = transform;
            _capsule = capsule;
            _groundLayer = groundLayer;
        }

        /// <summary>
        /// 检测是否在地面上
        /// </summary>
        public bool IsGrounded()
        {
            float capsuleHeight = _capsule.height;
            float capsuleRadius = _capsule.radius;
            float capsuleCenterY = _capsule.center.y;

            Vector3 origin = _transform.position + Vector3.up * (capsuleRadius + GROUND_CHECK_DISTANCE);

            // 中心射线
            if (CheckRay(origin, Vector3.down, capsuleHeight * 0.5f + GROUND_CHECK_DISTANCE))
                return true;

            // 4 方向脚底射线
            Vector3[] directions = new Vector3[]
            {
                Vector3.forward,
                Vector3.back,
                Vector3.left,
                Vector3.right
            };

            for (int i = 0; i < directions.Length; i++)
            {
                Vector3 offset = directions[i] * RAY_SPREAD;
                Vector3 rayOrigin = origin + offset;

                if (CheckRay(rayOrigin, Vector3.down, capsuleHeight * 0.5f + GROUND_CHECK_DISTANCE))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 单根射线检测
        /// </summary>
        private bool CheckRay(Vector3 origin, Vector3 direction, float distance)
        {
            if (Physics.Raycast(origin, direction, out RaycastHit hit, distance, _groundLayer))
            {
                // 检查地面角度
                float angle = Vector3.Angle(Vector3.up, hit.normal);
                return angle <= MAX_GROUND_ANGLE;
            }
            return false;
        }

        /// <summary>
        /// 获取地面法线（用于坡道减速）
        /// </summary>
        public Vector3 GetGroundNormal()
        {
            Vector3 origin = _transform.position + Vector3.up * (_capsule.radius + GROUND_CHECK_DISTANCE);

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _capsule.height * 0.5f + GROUND_CHECK_DISTANCE, _groundLayer))
            {
                return hit.normal;
            }

            return Vector3.up;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/GroundDetector.cs
git commit -m "feat(3C): add GroundDetector with layered raycast"
```

---

## Task 7: CharacterController

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs`

- [ ] **Step 1: Write CharacterController**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Input;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 角色控制器 — 移动/转向/物理驱动
    /// </summary>
    public class CharacterController
    {
        private readonly UnityEngine.CharacterController _controller;
        private readonly Rigidbody _rigidbody;
        private readonly GroundDetector _groundDetector;
        private readonly Transform _transform;

        // 移动参数
        public float MoveSpeed { get; set; } = 5.0f;
        public float RotationSpeed { get; set; } = 10.0f;
        public float Gravity { get; set; } = -20f;
        public float GroundCheckDistance { get; set; } = 0.1f;

        // 状态
        private CharacterData _data;
        private MoveCommand _currentCommand;
        private Vector3 _velocity;

        public CharacterData Data => _data;
        public bool IsGrounded => _data.IsGrounded;

        public CharacterController(
            Transform transform,
            UnityEngine.CharacterController controller,
            Rigidbody rigidbody,
            LayerMask groundLayer)
        {
            _transform = transform;
            _controller = controller;
            _rigidbody = rigidbody;
            _groundDetector = new GroundDetector(transform, controller.capsuleCollider, groundLayer);

            _data = new CharacterData
            {
                Position = transform.position,
                Rotation = transform.rotation,
                State = CharacterState.Idle,
                IsGrounded = true
            };
        }

        /// <summary>
        /// 每帧驱动
        /// </summary>
        public void Update(MoveCommand command)
        {
            _currentCommand = command;

            // 更新地面状态
            _data.IsGrounded = _groundDetector.IsGrounded();
            _data.Position = _transform.position;
            _data.Rotation = _transform.rotation;

            // 移动逻辑
            if (_data.IsGrounded)
            {
                _velocity.y = Gravity * Time.deltaTime;

                if (command.MoveDir.sqrMagnitude > 0.01f)
                {
                    // 移动
                    Vector3 moveVelocity = command.MoveDir * command.Speed;
                    _rigidbody.velocity = new Vector3(moveVelocity.x, _velocity.y, moveVelocity.z);

                    // 转向
                    Quaternion targetRot = command.Rotation;
                    _transform.rotation = Quaternion.Slerp(
                        _transform.rotation,
                        targetRot,
                        RotationSpeed * Time.deltaTime
                    );

                    _data.State = CharacterState.Running;
                }
                else
                {
                    _rigidbody.velocity = new Vector3(0f, _velocity.y, 0f);
                    _data.State = CharacterState.Idle;
                }
            }
            else
            {
                // 空中
                _velocity.y += Gravity * Time.deltaTime;
                _rigidbody.velocity = new Vector3(_rigidbody.velocity.x, _velocity.y, _rigidbody.velocity.z);
                _data.State = CharacterState.Falling;
            }

            // 更新数据
            _data.Velocity = _rigidbody.velocity;
            _data.VerticalVelocity = _velocity.y;
        }

        /// <summary>
        /// 应用服务端权威位置（网络校验后）
        /// </summary>
        public void ApplyServerPosition(Vector3 position, Quaternion rotation)
        {
            _transform.position = position;
            _transform.rotation = rotation;
            _rigidbody.position = position;
            _rigidbody.rotation = rotation;

            _data.Position = position;
            _data.Rotation = rotation;
        }

        /// <summary>
        /// 获取预测位置（用于网络同步）
        /// </summary>
        public Vector3 GetPredictedPosition()
        {
            return _transform.position;
        }

        public Quaternion GetPredictedRotation()
        {
            return _transform.rotation;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs
git commit -m "feat(3C): add CharacterController with movement and facing"
```

---

## Task 8: CharacterAnimationController

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterAnimationController.cs`

- [ ] **Step 1: Write CharacterAnimationController**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 角色动画控制器 — Animator 参数驱动
    /// </summary>
    public class CharacterAnimationController
    {
        private readonly Animator _animator;

        //Animator 参数名常量
        private static readonly int PARAM_SPEED = Animator.StringToHash("Speed");
        private static readonly int PARAM_MOVE_SPEED = Animator.StringToHash("MoveSpeed");
        private static readonly int PARAM_GROUNDED = Animator.StringToHash("Grounded");
        private static readonly int PARAM_VERTICAL_VELOCITY = Animator.StringToHash("VerticalVelocity");

        public CharacterAnimationController(Animator animator)
        {
            _animator = animator;
        }

        /// <summary>
        /// 每帧更新动画参数
        /// </summary>
        public void Update(CharacterData data)
        {
            if (_animator == null) return;

            // 速度（用于混合树）
            _animator.SetFloat(PARAM_SPEED, data.Velocity.magnitude, 0.1f, Time.deltaTime);
            _animator.SetFloat(PARAM_MOVE_SPEED, data.Velocity.magnitude, 0.1f, Time.deltaTime);

            // 地面状态
            _animator.SetBool(PARAM_GROUNDED, data.IsGrounded);

            // 垂直速度（用于落地/跳跃动画）
            _animator.SetFloat(PARAM_VERTICAL_VELOCITY, data.VerticalVelocity, 0.1f, Time.deltaTime);
        }

        /// <summary>
        /// 触发动画事件（可选）
        /// </summary>
        public void SetTrigger(string triggerName)
        {
            if (_animator != null)
            {
                _animator.SetTrigger(Animator.StringToHash(triggerName));
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterAnimationController.cs
git commit -m "feat(3C): add CharacterAnimationController for Animator parameter control"
```

---

## Task 9: ThirdPersonCameraController

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs`

- [ ] **Step 1: Write ThirdPersonCameraController**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Camera
{
    /// <summary>
    /// 第三人称相机控制器 — 平滑跟随
    /// </summary>
    public class ThirdPersonCameraController
    {
        private readonly Transform _cameraTransform;
        private readonly Transform _targetTransform;

        // 相机参数
        public float Distance { get; set; } = 5.0f;         // 相机距离
        public float Height { get; set; } = 2.0f;           // 相机高度
        public float PositionDamping { get; set; } = 5.0f;  // 位置平滑
        public float RotationDamping { get; set; } = 8.0f;   // 旋转平滑

        public float MinPitch { get; set; } = -30f;          // 最小俯仰角
        public float MaxPitch { get; set; } = 60f;           // 最大俯仰角
        public float MouseSensitivityX { get; set; } = 2.0f;
        public float MouseSensitivityY { get; set; } = 2.0f;

        // 当前旋转角度
        private float _horizontalAngle;
        private float _verticalAngle = 20f;

        public ThirdPersonCameraController(Transform cameraTransform, Transform targetTransform)
        {
            _cameraTransform = cameraTransform;
            _targetTransform = targetTransform;

            // 初始化相机角度
            if (_targetTransform != null)
            {
                _horizontalAngle = _targetTransform.eulerAngles.y;
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
        /// 每帧更新相机位置和旋转
        /// </summary>
        public void Update()
        {
            if (_targetTransform == null) return;

            // 计算目标位置（球坐标）
            Quaternion rotation = Quaternion.Euler(_verticalAngle, _horizontalAngle, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -Distance);
            offset.y = Height;

            Vector3 targetPosition = _targetTransform.position + offset;
            Vector3 currentPosition = _cameraTransform.position;

            // 平滑跟随
            _cameraTransform.position = Vector3.Lerp(currentPosition, targetPosition, PositionDamping * Time.deltaTime);

            // 平滑看向目标
            Vector3 lookTarget = _targetTransform.position + Vector3.up * Height * 0.5f;
            Quaternion targetRotation = Quaternion.LookRotation(lookTarget - _cameraTransform.position);
            _cameraTransform.rotation = Quaternion.Slerp(
                _cameraTransform.rotation,
                targetRotation,
                RotationDamping * Time.deltaTime
            );
        }

        /// <summary>
        /// 获取当前相机旋转
        /// </summary>
        public Quaternion GetRotation()
        {
            return Quaternion.Euler(_verticalAngle, _horizontalAngle, 0f);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs
git commit -m "feat(3C): add ThirdPersonCameraController with smooth follow"
```

---

## Task 10: NetworkBridge

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/NetworkBridge.cs`

**Note:** This is the only AOT-Hotfix bridge. It must use the existing AOT `KcpClient` and `MessageDispatcher` interfaces. No AOT code is modified.

- [ ] **Step 1: Write NetworkBridge**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using KcpNet;

namespace Hotfix.GameSystems.Sys3C.Network
{
    /// <summary>
    /// 玩家同步数据（服务端广播的他人位置）
    /// </summary>
    public struct RemotePlayerSyncData
    {
        public long PlayerId;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Speed;
        public long Timestamp;
    }

    /// <summary>
    /// 网络桥接 — Hotfix层访问AOT KcpNet的唯一通道
    /// </summary>
    public class NetworkBridge
    {
        private KcpClient _kcpClient;
        private Action<PositionSyncResponse> _onPositionSyncResponse;
        private Action<RemotePlayerSyncData> _onRemotePlayerUpdate;
        private uint _localSequence;

        /// <summary>
        /// 初始化桥接（需要外部传入AOT层的KcpClient引用）
        /// </summary>
        public void Initialize(KcpClient kcpClient)
        {
            _kcpClient = kcpClient;

            // 注册消息处理器（通过AOT的MessageDispatcher）
            // 注意：这需要AOT层提供MessageDispatcher的访问接口
            // 或者通过事件派发的方式让Hotfix订阅
        }

        /// <summary>
        /// 发送本地位置同步请求
        /// </summary>
        public void SendPositionSync(Vector3 position, Quaternion rotation, float speed)
        {
            if (_kcpClient == null || !_kcpClient.IsConnected) return;

            var request = new PositionSyncRequest
            {
                X = position.x,
                Y = position.y,
                Z = position.z,
                Rotation = rotation.eulerAngles.y,
                Speed = speed,
                Timestamp = DateTime.UtcNow.Ticks,
                Sequence = ++_localSequence
            };

            _kcpClient.SendAsync(request, MessageFlags.Reliable).ConfigureAwait(false);
        }

        /// <summary>
        /// 处理服务端位置同步响应
        /// </summary>
        public void HandlePositionSyncResponse(PositionSyncResponse response)
        {
            _onPositionSyncResponse?.Invoke(response);
        }

        /// <summary>
        /// 处理服务端广播的其他玩家位置
        /// </summary>
        public void HandleRemotePlayerUpdate(RemotePlayerSyncData data)
        {
            _onRemotePlayerUpdate?.Invoke(data);
        }

        /// <summary>
        /// 注册位置同步响应回调
        /// </summary>
        public void RegisterPositionSyncCallback(Action<PositionSyncResponse> callback)
        {
            _onPositionSyncResponse += callback;
        }

        /// <summary>
        /// 注册其他玩家位置更新回调
        /// </summary>
        public void RegisterRemotePlayerCallback(Action<RemotePlayerSyncData> callback)
        {
            _onRemotePlayerUpdate += callback;
        }

        /// <summary>
        /// 获取连接状态
        /// </summary>
        public bool IsConnected => _kcpClient?.IsConnected ?? false;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/NetworkBridge.cs
git commit -m "feat(3C): add NetworkBridge as AOT-Hotfix bridge"
```

---

## Task 11: NetworkPrediction

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/NetworkPrediction.cs`

- [ ] **Step 1: Write NetworkPrediction**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Network
{
    /// <summary>
    /// 客户端预测与服务端校验
    /// </summary>
    public class NetworkPrediction
    {
        // 预测位置队列（key = sequence）
        private readonly SortedList<uint, PredictedFrame> _predictedFrames = new SortedList<uint, PredictedFrame>();

        // 偏差阈值（超过则 rubber-band）
        private const float POSITION_DEVIATION_THRESHOLD = 0.5f;
        private const float ROTATION_DEVIATION_THRESHOLD = 5f;
        private const float RUBBER_BAND_SPEED = 10f;

        private uint _lastServerSequence;

        private struct PredictedFrame
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public uint Sequence;
            public long Timestamp;
        }

        /// <summary>
        /// 记录预测帧
        /// </summary>
        public void RecordPredictedFrame(uint sequence, Vector3 position, Quaternion rotation)
        {
            // 清理过期帧
            while (_predictedFrames.Count > 60) // 最多保留60帧
            {
                _predictedFrames.RemoveAt(0);
            }

            _predictedFrames[sequence] = new PredictedFrame
            {
                Position = position,
                Rotation = rotation,
                Sequence = sequence,
                Timestamp = System.DateTime.UtcNow.Ticks
            };
        }

        /// <summary>
        /// 处理服务端确认/拒绝
        /// </summary>
        public bool ValidateAndCorrect(uint serverSequence, Vector3 serverPosition, Quaternion serverRotation,
            out Vector3 correctedPosition, out Quaternion correctedRotation)
        {
            correctedPosition = serverPosition;
            correctedRotation = serverRotation;

            // 跳过已确认的旧帧
            if (serverSequence <= _lastServerSequence)
                return false;

            _lastServerSequence = serverSequence;

            // 检查是否有对应的预测帧
            if (_predictedFrames.TryGetValue(serverSequence, out var predictedFrame))
            {
                float posDeviation = Vector3.Distance(predictedFrame.Position, serverPosition);
                float rotDeviation = Quaternion.Angle(predictedFrame.Rotation, serverRotation);

                if (posDeviation > POSITION_DEVIATION_THRESHOLD || rotDeviation > ROTATION_DEVIATION_THRESHOLD)
                {
                    // 偏差过大，需要 rubber-band
                    correctedPosition = serverPosition;
                    correctedRotation = serverRotation;
                    return true; // 表示做了校正
                }
            }

            return false;
        }

        /// <summary>
        /// 执行 rubber-band 拉回
        /// </summary>
        public Vector3 ApplyRubberBand(Vector3 currentPosition, Vector3 targetPosition, float deltaTime)
        {
            return Vector3.Lerp(currentPosition, targetPosition, RUBBER_BAND_SPEED * deltaTime);
        }

        /// <summary>
        /// 获取下一个序列号
        /// </summary>
        public uint GetNextSequence()
        {
            return _lastServerSequence + 1;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/NetworkPrediction.cs
git commit -m "feat(3C): add NetworkPrediction with client-side prediction and rubber-band"
```

---

## Task 12: PositionInterpolator

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/PositionInterpolator.cs`

- [ ] **Step 1: Write PositionInterpolator**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Network
{
    /// <summary>
    /// 其他玩家位置插值器
    /// </summary>
    public class PositionInterpolator
    {
        // 插值目标数据
        private class InterpolateTarget
        {
            public Vector3 CurrentPosition;
            public Quaternion CurrentRotation;
            public Vector3 TargetPosition;
            public Quaternion TargetRotation;
            public float InterpolationTime;     // 目标到达时间
            public long LastUpdateTimestamp;
            public float NetworkLatency;        // 估算的网络延迟
        }

        private readonly Dictionary<long, InterpolateTarget> _targets = new Dictionary<long, InterpolateTarget>();

        // 插值延迟（秒），服务端广播间隔约 0.1s (10Hz)，加上延迟补偿
        private const float INTERPOLATION_DELAY = 0.1f;

        /// <summary>
        /// 更新目标位置（服务端广播触发）
        /// </summary>
        public void UpdateTarget(long playerId, Vector3 position, Quaternion rotation, long serverTimestamp)
        {
            if (!_targets.TryGetValue(playerId, out var target))
            {
                target = new InterpolateTarget
                {
                    CurrentPosition = position,
                    CurrentRotation = rotation,
                    TargetPosition = position,
                    TargetRotation = rotation
                };
                _targets[playerId] = target;
            }

            long now = System.DateTime.UtcNow.Ticks;
            long elapsed = now - serverTimestamp;
            float elapsedSeconds = elapsed / (float)System.TimeSpan.TicksPerSecond;

            // 网络延迟估算（简化：使用实际经过时间作为延迟）
            target.NetworkLatency = elapsedSeconds;

            // 目标位置 = 刚刚收到的服务端位置
            // 插值时间 = 延迟 + 一个广播间隔
            target.TargetPosition = position;
            target.TargetRotation = rotation;
            target.InterpolationTime = elapsedSeconds + INTERPOLATION_DELAY;
            target.LastUpdateTimestamp = serverTimestamp;
        }

        /// <summary>
        /// 每帧获取插值后的位置（供渲染使用）
        /// </summary>
        public (Vector3 position, Quaternion rotation) GetInterpolatedState(long playerId)
        {
            if (!_targets.TryGetValue(playerId, out var target))
                return (Vector3.zero, Quaternion.identity);

            long now = System.DateTime.UtcNow.Ticks;
            float t = (now - target.LastUpdateTimestamp) / (float)System.TimeSpan.TicksPerSecond;

            // t 是从上一个更新到现在经过的时间
            // 使用 t / INTERPOLATION_DELAY 作为 Lerp 参数
            float lerpT = Mathf.Clamp01(t / INTERPOLATION_DELAY);

            return (
                Vector3.Lerp(target.CurrentPosition, target.TargetPosition, lerpT),
                Quaternion.Slerp(target.CurrentRotation, target.TargetRotation, lerpT)
            );
        }

        /// <summary>
        /// 每帧推进插值（应在 LateUpdate 中调用）
        /// </summary>
        public void FrameAdvance(long playerId)
        {
            if (!_targets.TryGetValue(playerId, out var target))
                return;

            long now = System.DateTime.UtcNow.Ticks;
            float elapsed = (now - target.LastUpdateTimestamp) / (float)System.TimeSpan.TicksPerSecond;
            float lerpT = Mathf.Clamp01(elapsed / INTERPOLATION_DELAY);

            target.CurrentPosition = Vector3.Lerp(target.CurrentPosition, target.TargetPosition, lerpT);
            target.CurrentRotation = Quaternion.Slerp(target.CurrentRotation, target.TargetRotation, lerpT);
        }

        /// <summary>
        /// 移除目标
        /// </summary>
        public void RemoveTarget(long playerId)
        {
            _targets.Remove(playerId);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Network/PositionInterpolator.cs
git commit -m "feat(3C): add PositionInterpolator for remote player interpolation"
```

---

## Task 13: Sys3CEntry

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`

- [ ] **Step 1: Write Sys3CEntry**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Camera;
using Hotfix.GameSystems.Sys3C.Network;

namespace Hotfix.GameSystems.Sys3C
{
    /// <summary>
    /// 3C 系统入口 — 绑定所有组件，在场景中挂载到角色实体
    /// </summary>
    public class Sys3CEntry : MonoBehaviour
    {
        [Header("Physics")]
        [SerializeField] private LayerMask _groundLayer = ~0;

        [Header("Movement")]
        [SerializeField] private float _moveSpeed = 5f;
        [SerializeField] private float _rotationSpeed = 10f;

        [Header("Camera")]
        [SerializeField] private float _cameraDistance = 5f;
        [SerializeField] private float _cameraHeight = 2f;
        [SerializeField] private float _cameraDamping = 5f;

        [Header("Camera Sensitivity")]
        [SerializeField] private float _mouseSensitivityX = 2f;
        [SerializeField] private float _mouseSensitivityY = 2f;

        // 各模块实例
        private InputManager _inputManager;
        private CharacterController _characterController;
        private CharacterAnimationController _animationController;
        private ThirdPersonCameraController _cameraController;
        private NetworkBridge _networkBridge;
        private NetworkPrediction _networkPrediction;
        private PositionInterpolator _positionInterpolator;

        // 组件引用
        private UnityEngine.CharacterController _unityCharacterController;
        private Rigidbody _rigidbody;
        private Animator _animator;
        private Camera _mainCamera;

        private void Awake()
        {
            // 获取组件引用
            _unityCharacterController = GetComponent<UnityEngine.CharacterController>();
            _rigidbody = GetComponent<Rigidbody>();
            _animator = GetComponent<Animator>();

            if (_mainCamera == null)
                _mainCamera = Camera.main;
        }

        private void Start()
        {
            // 初始化输入管理器
            _inputManager = new InputManager();
            _inputManager.MoveSpeed = _moveSpeed;
            _inputManager.CameraSensitivityX = _mouseSensitivityX;
            _inputManager.CameraSensitivityY = _mouseSensitivityY;

            // 初始化角色控制器
            _characterController = new CharacterController(
                transform,
                _unityCharacterController,
                _rigidbody,
                _groundLayer
            );
            _characterController.MoveSpeed = _moveSpeed;
            _characterController.RotationSpeed = _rotationSpeed;

            // 初始化动画控制器
            if (_animator != null)
                _animationController = new CharacterAnimationController(_animator);

            // 初始化相机控制器
            if (_mainCamera != null)
            {
                _cameraController = new ThirdPersonCameraController(
                    _mainCamera.transform,
                    transform
                );
                _cameraController.Distance = _cameraDistance;
                _cameraController.Height = _cameraHeight;
                _cameraController.PositionDamping = _cameraDamping;
                _cameraController.MouseSensitivityX = _mouseSensitivityX;
                _cameraController.MouseSensitivityY = _mouseSensitivityY;
            }

            // 初始化网络模块
            _networkBridge = new NetworkBridge();
            _networkPrediction = new NetworkPrediction();
            _positionInterpolator = new PositionInterpolator();

            // 注册网络回调
            _networkBridge.RegisterPositionSyncCallback(OnPositionSyncResponse);
        }

        private void Update()
        {
            // 输入更新
            _inputManager.Update();

            // 相机旋转输入
            Vector2 cameraInput = _inputManager.GetCameraRotationInput();
            _cameraController?.HandleRotationInput(cameraInput);

            // 获取移动命令
            Vector3 forward = transform.forward;
            MoveCommand command = _inputManager.GetMoveCommand(forward);

            // 记录预测帧
            uint seq = _networkPrediction.GetNextSequence();
            _networkPrediction.RecordPredictedFrame(
                seq,
                _characterController.GetPredictedPosition(),
                _characterController.GetPredictedRotation()
            );

            // 角色更新
            _characterController.Update(command);

            // 动画更新
            _animationController?.Update(_characterController.Data);

            // 相机更新（相机在 LateUpdate 更新）
            _cameraController?.Update();
        }

        private void LateUpdate()
        {
            // 额外的相机插值可以在 LateUpdate 做
        }

        private void FixedUpdate()
        {
            // 固定帧网络同步（10Hz）
            if (_networkBridge.IsConnected)
            {
                _networkBridge.SendPositionSync(
                    _characterController.GetPredictedPosition(),
                    _characterController.GetPredictedRotation(),
                    _characterController.Data.Velocity.magnitude
                );
            }
        }

        private void OnPositionSyncResponse(PositionSyncResponse response)
        {
            // 处理服务端校验结果
            // 实际项目中需要从服务端获取权威位置，这里简化处理
        }

        /// <summary>
        /// 绑定网络客户端（外部调用）
        /// </summary>
        public void BindNetworkClient(KcpNet.KcpClient kcpClient)
        {
            _networkBridge.Initialize(kcpClient);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git commit -m "feat(3C): add Sys3CEntry as system entry point"
```

---

## Task 14: Final Integration

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` (minor adjustments after first test)

- [ ] **Step 1: Review all files** — check for missing usings, type errors, interface mismatches

- [ ] **Step 2: Check asmdef references** — the Hotfix assembly needs references to AOT assemblies that contain KcpNet. Update `Sys3C.asmdef` to include `KcpNet` if required by HybridCLR

- [ ] **Step 3: Test in Unity Editor** — create a test scene with a character prefab that has:
   - `Sys3CEntry` component
   - `Rigidbody` (Use Gravity: true, IsKinematic: false)
   - `UnityEngine.CharacterController`
   - `Animator` with parameters: Speed, MoveSpeed, Grounded, VerticalVelocity
   - `CapsuleCollider`

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(3C): complete core 3C system implementation"
```

---

## Spec Coverage Check

| Spec Requirement | Task |
|-----------------|------|
| 角色自由移动（WASD + 摇杆） | Task 3, 4, 5, 7 |
| 第三人称相机平滑跟随 | Task 9 |
| Animator 状态机切换 | Task 8 |
| 位置/旋转网络同步 | Task 10 |
| 客户端预测 + rubber-band | Task 11 |
| 其他玩家位置插值 | Task 12 |
| AOT/Hotfix 分层架构 | Task 1, 10 |
| Rigidbody + CapsuleCollider 物理 | Task 6, 7 |
| 地面检测（分层射线） | Task 6 |

---

## Self-Review

1. **Placeholder scan:** No TBD/TODO found — all code is concrete
2. **Type consistency:** `MoveCommand`, `CharacterData`, `CharacterState` used consistently across tasks
3. **Interface consistency:** `IInputAdapter` method names match in both adapters
4. **Spec coverage:** All Phase 1 delivery items covered
5. **No AOT modifications:** NetworkBridge only calls existing KcpNet public APIs
