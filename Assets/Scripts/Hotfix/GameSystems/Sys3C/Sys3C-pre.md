# 3C System Design Specification

**MMO ARPG · Unity 2022 LTS · HybridCLR**

---

## 1. Overview

3C system (Character, Camera, Control) for MMO ARPG. Free-aim combat (no target lock), state synchronization with client-side prediction and server validation.

---

## 2. Game Type

- **Genre:** MMO ARPG, free-aim action combat
- **Physics:** Rigidbody + CapsuleCollider, no jumping (reserved for future)
- **Ground Detection:** Custom layered raycast (CharacterController style)
- **Facing:** Free facing — character faces movement direction
- **Camera:** Third-person orbit camera, smooth follow (no terrain occlusion in Phase 1)
- **Network:** State sync, client prediction + server validation
- **Other Players:** Server-broadcast positions with local interpolation
- **Skills:** Independent module (outside 3C scope)

---

## 3. Architecture

### AOT / Hotfix Boundary

```
AOT Layer (IL2CPP, not hot-reloadable)
└── KcpNet networking (existing)

Hotfix Layer (DLL, hot-reloadable)
├── Input          ← direct Input.GetAxis access
├── Character      ← direct Rigidbody/CapsuleCollider access
├── Camera         ← direct Transform access
└── NetworkBridge  ← ONLY bridge: sends/receives via AOT KcpClient
```

**Only NetworkBridge requires AOT bridging** — HybridCLR's full CLR runtime allows Hotfix to directly access Unity APIs.

---

## 4. Module Design

### Input System

Dual-input abstraction, unified output:

- `KeyboardInputAdapter`: WASD → normalized move vector
- `JoystickInputAdapter`: Virtual joystick / touch → normalized move vector
- `InputManager`: Reads from active adapter, outputs `MoveCommand`

```csharp
public struct MoveCommand {
    Vector3 MoveDir;    // normalized
    float   Speed;
    Quaternion Rotation;
    long    Timestamp;
    uint    Sequence;
}
```

Camera rotation via mouse delta / joystick right analog.

### Character Controller

- Movement direction = input direction (keeps current facing when idle)
- Rigidbody-driven: `velocity = moveDir * speed`
- Ground detection: 5-ray layered (center + 4 cardinal directions)
- Slope filtering: MAX_GROUND_ANGLE = 45°
- Animator parameters: `Speed`, `MoveSpeed`, `Grounded`, `VerticalVelocity`

### Camera Controller

- Orbit: mouse/touch drag → horizontal + pitch angle
- Smooth follow: position Lerp, rotation Slerp, configurable damping
- Configurable: distance, height, pitch limits, sensitivity

### Network Sync

- Client sends `MoveCommand` per logic frame (FixedUpdate, 10Hz)
- Client prediction: immediate local execution, record predicted frame
- Server validation: compare predicted vs authoritative position
- Rubber-band if deviation > threshold (pos: 0.5m, rot: 5°)
- Other players: server broadcasts at 10-15Hz, client Lerp interpolates

---

## 5. Data Structures

```csharp
public enum CharacterState { Idle, Running, Falling }

public struct CharacterData {
    Vector3       Position;
    Quaternion     Rotation;
    Vector3       Velocity;
    CharacterState State;
    bool          IsGrounded;
    float         VerticalVelocity;
}
```

---

## 6. File Structure

```
Assets/Scripts/Hotfix/GameSystems/Sys3C/
├── Sys3C.asmdef
├── Sys3CEntry.cs                  — MonoBehaviour, wires all modules
├── Character/
│   ├── CharacterData.cs           — MoveCommand, CharacterState, CharacterData
│   ├── GroundDetector.cs          — 5-ray layered ground detection
│   ├── CharacterController.cs     — movement + facing + Rigidbody driver
│   └── CharacterAnimationController.cs — Animator parameter driver
├── Camera/
│   └── ThirdPersonCameraController.cs — orbit camera + smooth follow
├── Input/
│   ├── KeyboardInputAdapter.cs    — WASD + mouse (includes IInputAdapter)
│   ├── JoystickInputAdapter.cs   — virtual joystick + touch
│   └── InputManager.cs           — dual-adapter unified manager
└── Network/
    ├── NetworkBridge.cs           — AOT bridge (KcpClient wrapper)
    ├── NetworkPrediction.cs       — client prediction + rubber-band
    └── PositionInterpolator.cs   — remote player interpolation
```

---

## 7. Delivery Scope — Phase 1

- [x] Character moves freely (WASD + virtual joystick)
- [x] Third-person camera smoothly follows, rotatable via drag
- [x] Animator state transitions (idle/run)
- [x] Position/rotation network sync
- [x] Client prediction + rubber-band correction
- [x] Other player position interpolation

---

## 8. Reserved for Future

- Jump system (ground detection layer supports this)
- Terrain-aware camera (obstruction detection, auto pull-in)
- Combat-focused camera (zoom, damping during combat)
- Auto-facing during combat (skill system integration)
