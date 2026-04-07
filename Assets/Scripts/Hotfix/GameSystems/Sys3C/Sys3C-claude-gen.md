# 3C System Code Structure

**Assembly:** `Hotfix.GameSystems.Sys3C` (`Sys3C.asmdef`)
**Namespace root:** `Hotfix.GameSystems.Sys3C`

---

## Module Overview

3C system for MMO ARPG — Character movement/rotation, third-person camera, dual-input system, and network sync with client prediction. All logic runs in Hotfix layer; only `NetworkBridge` interfaces with AOT KcpNet.

---

## Class Responsibilities

### `Sys3CEntry` (entry point)
- **Role:** `MonoBehaviour` that instantiates and wires all 3C modules
- **Location:** Root of namespace, root of assembly
- **Public API:**
  - `BindNetworkClient(KcpClient)` — inject AOT client reference
- **Dependencies:** Creates InputManager, CharacterController, GroundDetector, CharacterAnimationController, ThirdPersonCameraController, NetworkBridge, NetworkPrediction, PositionInterpolator
- **Lifecycle:** `Awake` → `Start` → `Update` → `FixedUpdate` → `LateUpdate`

### `CharacterData`
- **Role:** Value-type data containers (no logic)
- **Location:** `Hotfix.GameSystems.Sys3C.Character`
- **Types:**
  - `MoveCommand` — input → character, immutable per frame
  - `CharacterState` — enum: Idle / Running / Falling
  - `CharacterData` — all runtime character state (position, rotation, velocity, grounded)

### `KeyboardInputAdapter` + `JoystickInputAdapter`
- **Role:** Platform-specific input adapters implementing `IInputAdapter`
- **Location:** `Hotfix.GameSystems.Sys3C.Input`
- **IInputAdapter interface:**
  - `string AdapterName { get; }`
  - `Vector3 GetMoveInput()` — normalized world-space direction
  - `Vector2 GetCameraRotationInput()` — raw mouse/joystick delta
  - `bool HasMoveInput()`
  - `bool HasCameraRotationInput()`

### `InputManager`
- **Role:** Manages input adapters, produces standardized `MoveCommand`
- **Location:** `Hotfix.GameSystems.Sys3C.Input`
- **Public API:**
  - `MoveCommand GetMoveCommand(Vector3 characterForward)`
  - `Vector2 GetCameraRotationInput()`
  - `void RegisterAdapter(IInputAdapter)`
  - `void SetActiveAdapter(string name)`
  - `float MoveSpeed { get; set; }`
  - `float CameraSensitivityX/Y { get; set; }`

### `GroundDetector`
- **Role:** 5-ray layered capsule ground detection
- **Location:** `Hotfix.GameSystems.Sys3C.Character`
- **Public API:**
  - `bool IsGrounded()` — checks center + 4 cardinal rays
  - `Vector3 GetGroundNormal()` — for slope deceleration
- **Constants:** RAY_SPREAD=0.2, MAX_GROUND_ANGLE=45°, GROUND_CHECK_DISTANCE=0.15

### `CharacterController`
- **Role:** Drives Rigidbody velocity from `MoveCommand`; manages facing
- **Location:** `Hotfix.GameSystems.Sys3C.Character`
- **Public API:**
  - `CharacterData Data { get; }` — current state
  - `bool IsGrounded { get; }`
  - `void Update(MoveCommand command)` — call every frame
  - `void ApplyServerPosition(Vector3, Quaternion)` — rubber-band snap
  - `Vector3 GetPredictedPosition()`
  - `Quaternion GetPredictedRotation()`
  - `float MoveSpeed { get; set; }`
  - `float RotationSpeed { get; set; }`
- **State machine:** Idle → Running → Falling (driven by grounded + input)

### `CharacterAnimationController`
- **Role:** Maps `CharacterData` to Unity `Animator` parameters
- **Location:** `Hotfix.GameSystems.Sys3C.Character`
- **Public API:**
  - `void Update(CharacterData data)` — call every frame
  - `void SetTrigger(string triggerName)`
- **Animator parameters written:** `Speed`, `MoveSpeed`, `Grounded`, `VerticalVelocity`

### `ThirdPersonCameraController`
- **Role:** Orbit camera with configurable distance, height, damping
- **Location:** `Hotfix.GameSystems.Sys3C.Camera`
- **Public API:**
  - `void HandleRotationInput(Vector2 input)`
  - `void Update()` — call every frame
  - `Quaternion GetRotation()`
  - `float Distance/Height/PositionDamping/RotationDamping { get; set; }`
  - `float MinPitch/MaxPitch/MouseSensitivityX/Y { get; set; }`

### `NetworkBridge`
- **Role:** Only AOT-Hotfix bridge — wraps `KcpClient`, sends/receives `PositionSyncRequest/Response`
- **Location:** `Hotfix.GameSystems.Sys3C.Network`
- **Public API:**
  - `void Initialize(KcpClient)` — inject AOT client
  - `void SendPositionSync(Vector3 pos, Quaternion rot, float speed)`
  - `void RegisterPositionSyncCallback(Action<PositionSyncResponse>)`
  - `void RegisterRemotePlayerCallback(Action<RemotePlayerSyncData>)`
  - `bool IsConnected { get; }`
- **RemotePlayerSyncData struct:** PlayerId, Position, Rotation, Speed, Timestamp

### `NetworkPrediction`
- **Role:** Records predicted frames, validates against server, applies rubber-band
- **Location:** `Hotfix.GameSystems.Sys3C.Network`
- **Public API:**
  - `void RecordPredictedFrame(uint seq, Vector3 pos, Quaternion rot)`
  - `bool ValidateAndCorrect(uint serverSeq, Vector3 serverPos, Quaternion serverRot, out Vector3 correctedPos, out Quaternion correctedRot)` — returns true if corrected
  - `Vector3 ApplyRubberBand(Vector3 current, Vector3 target, float dt)`
  - `uint GetNextSequence()`
- **Thresholds:** POSITION_DEVIATION=0.5m, ROTATION_DEVIATION=5°, RUBBER_BAND_SPEED=10

### `PositionInterpolator`
- **Role:** Smoothly interpolates remote player positions between server broadcasts
- **Location:** `Hotfix.GameSystems.Sys3C.Network`
- **Public API:**
  - `void UpdateTarget(long playerId, Vector3 pos, Quaternion rot, long serverTimestamp)`
  - `(Vector3 pos, Quaternion rot) GetInterpolatedState(long playerId)`
  - `void FrameAdvance(long playerId)` — call in LateUpdate
  - `void RemoveTarget(long playerId)`
- **Constants:** INTERPOLATION_DELAY=0.1s

---

## Key Method Signatures

| Class | Method | File |
|-------|--------|------|
| `InputManager` | `MoveCommand GetMoveCommand(Vector3)` | InputManager.cs |
| `GroundDetector` | `bool IsGrounded()` | GroundDetector.cs |
| `CharacterController` | `void Update(MoveCommand)` | CharacterController.cs |
| `CharacterController` | `void ApplyServerPosition(Vector3, Quaternion)` | CharacterController.cs |
| `CharacterAnimationController` | `void Update(CharacterData)` | CharacterAnimationController.cs |
| `ThirdPersonCameraController` | `void HandleRotationInput(Vector2)` | ThirdPersonCameraController.cs |
| `ThirdPersonCameraController` | `void Update()` | ThirdPersonCameraController.cs |
| `NetworkBridge` | `void Initialize(KcpClient)` | NetworkBridge.cs |
| `NetworkBridge` | `void SendPositionSync(Vector3, Quaternion, float)` | NetworkBridge.cs |
| `NetworkPrediction` | `bool ValidateAndCorrect(uint, Vector3, Quaternion, out Vector3, out Quaternion)` | NetworkPrediction.cs |
| `PositionInterpolator` | `void UpdateTarget(long, Vector3, Quaternion, long)` | PositionInterpolator.cs |

---

## Dependencies

```
Sys3CEntry
├── InputManager
│   ├── KeyboardInputAdapter (implements IInputAdapter)
│   └── JoystickInputAdapter (implements IInputAdapter)
├── CharacterController
│   ├── GroundDetector
│   └── CharacterData (struct, no dependency)
├── CharacterAnimationController
│   └── CharacterData (struct, no dependency)
├── ThirdPersonCameraController (no dependencies)
├── NetworkBridge
│   └── KcpNet (AOT only)
├── NetworkPrediction (no dependencies)
└── PositionInterpolator (no dependencies)
```

---

## Performance Considerations

- `CharacterData` and `MoveCommand` are **value types** (struct) to avoid heap allocation
- `GroundDetector` uses raw `Physics.Raycast` (not CapsuleCast) for minimal overhead
- `PositionInterpolator` uses `Dictionary<long, InterpolateTarget>` keyed by player ID
- `NetworkPrediction` uses `SortedList<uint, PredictedFrame>` capped at 60 frames
- All per-frame updates use `Time.deltaTime` or `FixedUpdate` for fixed-step network sync

---

## Potential Issues

1. **Rigidbody conflict:** `CharacterController` and `Rigidbody` both modify position — ensure only one drives velocity at a time
2. **GroundDetector ray origin:** Uses `transform.position + Vector3.up * (capsuleRadius + GROUND_CHECK_DISTANCE)` — capsule center offset assumed zero
3. **NetworkBridge message routing:** Hotfix cannot directly register handlers with AOT `MessageDispatcher` — requires AOT-side event bridge (TODO: implement in future)
4. **Animator parameter names:** Must match exactly (`Speed`, `MoveSpeed`, `Grounded`, `VerticalVelocity`) or animation won't transition
