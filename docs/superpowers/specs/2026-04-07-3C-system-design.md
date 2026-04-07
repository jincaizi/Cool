# 3C System Design — MMO ARPG

**Date:** 2026-04-07
**Status:** Approved

---

## 1. Overview

3C system (Character, Camera, Control) for an MMO ARPG using Unity 2022 LTS with HybridCLR hotfix architecture. Free-aim combat (no target lock), state synchronization with client prediction + server validation.

---

## 2. Game Type & Requirements

- **Genre:** MMO ARPG, free-aim combat
- **Physics:** Rigidbody + CapsuleCollider, no jumping (reserved for future)
- **Ground Detection:** Custom layered raycast (CharacterController style)
- **Facing:** Free facing — character faces movement direction, no auto-target
- **Camera:** Third-person follow camera with smooth damping (no occlusion initially)
- **Network:** State synchronization, client prediction + server validation
- **Other Players:** Server-broadcast positions with local interpolation
- **Skills:** Independent module (out of 3C scope)

---

## 3. Architecture

### AOT / Hotfix Boundary

```
AOT Layer (IL2CPP, not hot-reloadable)
└── KcpNet networking (existing)

Hotfix Layer (DLL, hot-reloadable)
├── Input (direct Input.GetAxis access)
├── Character (direct Rigidbody/CapsuleCollider access)
├── Camera (direct Transform access)
└── NetworkBridge (only bridge: sends/receives via AOT KcpClient)
```

**Only NetworkBridge requires AOT bridging** due to KCP transport being in AOT. All other systems (input, physics, camera) are accessed directly from Hotfix via HybridCLR's full CLR runtime.

---

## 4. Module Design

### Input System

Dual-input abstraction, unified output:

- **KeyboardInputAdapter:** WASD → normalized movement vector
- **JoystickInputAdapter:** Virtual UI joystick → normalized movement vector
- **InputManager:** Reads from both adapters, outputs standardized `MoveCommand` (moveDir, speed)

```csharp
public struct MoveCommand {
    public Vector3 moveDir;    // normalized
    public float speed;
    public Quaternion rotation; // from facing direction
}
```

Mouse/touch: camera rotation input handled separately via `GetCameraRotationInput()`.

### Character Controller

- Movement direction = input direction (keeps current facing when no input)
- Rigidbody driven: `velocity = moveDir * speed` (fixed frame update)
- Ground detection: layered rays at capsule bottom (center + 4 directions), slope deceleration
- Animator parameters: Speed, MoveSpeed, Grounded, VerticalVelocity

### Camera Controller

- Orbits character: mouse/touch drag controls horizontal/pitch angle
- Smooth follow: position Lerp, rotation Slerp, configurable damping
- Configurable offset (distance, height)

### Network Sync

- Client sends: MoveCommand per logic frame or input sequence number
- Client prediction: execute immediately, maintain predicted position locally
- Server validation: broadcast authoritative position, client compares to predicted
- Deviation threshold: rubber-band (gradual snap back)
- Other players: server broadcasts at 10-15Hz, client Lerp interpolates

### Animation

- Unity Animator state machine controlled by code parameters
- Parameters: Speed, MoveSpeed, Grounded, VerticalVelocity
- CharacterController sets parameters each frame to drive state transitions

---

## 5. File Structure

```
Assets/Scripts/Hotfix/GameSystems/Sys3C/
├── Sys3C.asmdef
├── Character/
│   ├── CharacterController.cs
│   ├── CharacterData.cs
│   └── CharacterAnimationController.cs
├── Camera/
│   └── ThirdPersonCameraController.cs
├── Input/
│   ├── InputManager.cs
│   ├── KeyboardInputAdapter.cs
│   └── JoystickInputAdapter.cs
├── Network/
│   ├── NetworkPrediction.cs
│   ├── PositionInterpolator.cs
│   └── NetworkBridge.cs
└── Sys3CEntry.cs

Assets/Scripts/AOT/Core/Physics/  (empty — physics accessed directly from Hotfix)
```

---

## 6. Delivery Scope — Phase 1

1. Character moves freely in scene (WASD + virtual joystick)
2. Third-person camera smoothly follows, rotatable via drag
3. Animator state machine transitions correctly (idle/run)
4. Position/rotation network sync (LAN two-machine test)
5. Client prediction + rubber-band snap-back
6. Other player positions smoothly interpolated

---

## 7. Reserved for Future

- Jump system (ground detection layer already supports this)
- Terrain-aware camera (obstruction detection, auto pull-in)
- Combat-focused camera (zoom, damping adjustments during combat)
- Auto-facing during combat (skill system integration)
