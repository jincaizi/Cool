# Sys3C Module Generated Documentation

## Module Overview
The Sys3C module provides a complete Character, Camera, and Control system for the Unity MMO client project. It implements client-side prediction for movement, third-person camera following with collision detection, and a unified input system based on Unity's New Input System.

## Design Approach

### Architecture
- **AOT + Hotfix**: Follows HybridCLR's AOT + Hotfix layered architecture
  - AOT Layer: Core modules, DataDefinitions, Bridge interfaces
  - Hotfix Layer: GameSystems implementations and Entry point

### Key Design Patterns
- **Interface-based programming**: All controllers (Player, Camera, Input) are accessed through interfaces to reduce coupling
- **Bridge Pattern**: Hotfix layer accesses AOT functionality through bridge classes
- **Event-driven architecture**: Global EventDispatcher for decoupled communication
- **Object Pooling**: GameObjectPool for efficient object reuse (effects, bullets)

## Class Responsibilities

### AOT Layer

#### Core Modules
| Class | Responsibility |
|-------|----------------|
| `EventDispatcher` | Global singleton event dispatcher for register/unregister/dispatch |
| `InputManager` | Wraps Unity New Input System, provides input events |
| `GameObjectPool` | Object pooling for frequently created/destroyed objects |
| `ResourceLoader` | Addressable async loading wrapper |

#### DataDefinition Modules
| Class | Responsibility |
|-------|----------------|
| `IPlayerController` | Interface for player control |
| `ICameraHandler` | Interface for camera control |
| `IInputHandler` | Interface for input handling |
| `EventKeys` | Event key constants |
| `PlayerState` | Player state enum (Idle, Walk, Run, Jump, Fall, Attack, Hit, Dead) |
| `InputActionType` | Input action type enum |

#### Bridge Modules
| Class | Responsibility |
|-------|----------------|
| `Bridge_Player` | Bridge for player operations (dispatch events, pooling, resource loading) |
| `Bridge_Camera` | Bridge for camera operations (set target, distance, rotation) |
| `Bridge_Input` | Bridge for input operations (get input states) |

### Hotfix Layer

| Class | Responsibility |
|-------|----------------|
| `PlayerController` | Character movement, animation state machine, client-side prediction |
| `ThirdPersonCameraController` | Cinemachine-style camera following with rotation/zoom/collision |
| `InputHandler` | Input event translation and distribution |
| `HotfixEntry` | System initialization and lifecycle management |

## Key Methods

### PlayerController
| Method | Description |
|--------|-------------|
| `Initialize()` | Initialize player controller and register events |
| `SetMoveInput(Vector2)` | Set current movement input |
| `Jump()` | Trigger player jump |
| `Attack()` | Trigger player attack |
| `StopAttack()` | Stop attacking |
| `OnServerPositionUpdate()` | Client-side prediction correction from server |
| `Update()` | Main update loop |

### ThirdPersonCameraController
| Method | Description |
|--------|-------------|
| `Initialize()` | Initialize camera and create Camera component |
| `UpdateCamera()` | Main camera update loop |
| `SetTarget(Transform)` | Set camera follow target |
| `SetDistance(float)` | Set camera distance |
| `SetRotation()` | Set camera rotation angles |
| `UpdateRotation()` | Handle mouse input for camera rotation |
| `UpdateDistance()` | Handle scroll wheel for zoom |
| `UpdatePosition()` | Calculate and apply camera position |

### InputHandler
| Method | Description |
|--------|-------------|
| `Initialize()` | Register input callbacks |
| `UpdateInput()` | Poll input states from bridges |
| `ResetInput()` | Reset all input states |

## Dependencies

### External Dependencies
- **Unity 2022 LTS**: Required engine version
- **Unity New Input System**: For input handling (`UnityEngine.InputSystem`)
- **Cinemachine** (optional): Camera system foundations
- **Addressables**: For async resource loading

### Internal Dependencies
```
HotfixEntry
    ├── InputHandler
    ├── PlayerController
    │   └── Bridge_Player → EventDispatcher, GameObjectPool, ResourceLoader
    └── ThirdPersonCameraController
        └── Bridge_Camera → EventDispatcher

Bridge_Input → InputManager (AOT)
Bridge_Player → EventDispatcher, GameObjectPool, ResourceLoader (AOT)
Bridge_Camera → EventDispatcher (AOT)
```

## File Structure
```
Assets/Scripts/
├── AOT/
│   ├── Core/
│   │   ├── EventDispatcher/
│   │   │   └── EventDispatcher.cs
│   │   ├── InputManager/
│   │   │   └── InputManager.cs
│   │   ├── ObjectPool/
│   │   │   └── GameObjectPool.cs
│   │   └── ResourceLoader/
│   │       └── ResourceLoader.cs
│   ├── DataDefinition/
│   │   ├── Interfaces/
│   │   │   ├── IPlayerController.cs
│   │   │   ├── ICameraHandler.cs
│   │   │   └── IInputHandler.cs
│   │   ├── Constants/
│   │   │   └── EventKeys.cs
│   │   └── Enums/
│   │       ├── PlayerState.cs (in IPlayerController.cs)
│   │       └── InputActionType.cs
│   └── Bridge/
│       ├── Bridge_Player.cs
│       ├── Bridge_Camera.cs
│       └── Bridge_Input.cs
└── Hotfix/
    ├── GameSystems/
    │   ├── PlayerController.cs
    │   ├── ThirdPersonCameraController.cs
    │   └── InputHandler.cs
    └── Entry/
        └── HotfixEntry.cs
```

## Potential Risks

### 1. Thread Safety
- **Risk**: EventDispatcher uses locks for thread safety but event callbacks execute on various threads
- **Mitigation**: Callbacks should be designed to not block; heavy processing should be dispatched to main thread

### 2. Client Prediction Conflicts
- **Risk**: Client-side prediction may conflict with server authority
- **Mitigation**: `OnServerPositionUpdate` applies smooth correction; prediction flag prevents immediate re-correction

### 3. Input Latency
- **Risk**: New Input System event-driven model may introduce frame delay
- **Mitigation**: `InputHandler.UpdateInput()` polls state each frame for immediate response

### 4. Camera Collision
- **Risk**: SphereCast collision may not handle all edge cases
- **Mitigation**: Uses lerp smoothing to avoid jarring camera snaps

### 5. Resource Loading
- **Risk**: Addressable loading is async and may fail
- **Mitigation**: Fallback to default player created with primitives; error logging

### 6. Memory Management
- **Risk**: ObjectPool holds references preventing garbage collection
- **Mitigation**: Clear pools when objects no longer needed; use weak references where appropriate

### 7. Hotfix/AOT Boundary
- **Risk**: Crossing hotfix/AOT boundary improperly
- **Mitigation**: All Unity Engine access goes through Bridge interfaces; hotfix has no direct Unity dependencies

## Usage Example

```csharp
// In HotfixEntry or scene initialization:
var entry = gameObject.AddComponent<HotfixEntry>();
entry.Initialize();

// Later, access controllers:
var player = entry.GetPlayerController();
var camera = entry.GetCameraController();

// Set player target position
player.SetMoveInput(new Vector2(1, 0));
```

## Animation State Machine

The PlayerController expects the following animator parameters:
- `Speed` (float): Movement speed 0-6
- `VerticalVelocity` (float): Y velocity for jump/fall
- `IsGrounded` (bool): Grounded state
- `IsAttacking` (bool): Attack state

States: Idle → Walk/Run → Jump/Fall → Attack → Idle
