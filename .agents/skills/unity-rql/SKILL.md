---
name: unity-rql
description: |-
  How to read and change anything in the Unity Editor with RQL (rql-query / rql-mutate / rql-schema): the query
  language, operators, sources (scene objects, assets, scenes, prefabs, console logs, Editor state), Editor operations
  (create, add components, scenes, prefabs, assets, selection, Play Mode) and what installed extensions add.
metadata:
  generated-by: mcp-plugin-dotnet
---
# RQL — query and change the Unity Editor

RQL is one pipeline language for the whole Editor: find objects, read members, filter, aggregate, and change them in one
call. Prefer one precise query over many exploratory calls.

- `rql-query` — read-only queries, compact table results.
- `rql-mutate` — queries ending with `set` / `call`; all-or-nothing, undoable (Ctrl+Z), `dry_run: true` previews.
- `rql-schema` — sources and component types in the scene; `rql-schema(type)` lists one type's members and methods.

```bash
unity-mcp-cli run-tool rql-query --input '{"query": "objects | where name contains \"Enemy\" | select $ref name"}'
unity-mcp-cli run-tool rql-mutate --input '{"query": "objects | where name == \"Player\" | set active = false"}'
```

## Language

```
RQL: source | stage | stage ...
Sources: objects (every GameObject in the loaded scenes, inactive included), roots, scenes, selection, prefab (the prefab open in prefab editing mode), console (Editor log entries), shaders, componentTypes, profiler (memory / rendering / frame snapshots; call Start / Stop / Save), tests (test methods; run a selection with tests-run query), testResults (last run), editor (Editor state and operations). Assets: unity.assets("t:Material") or unity.assets("t:Prefab", "Assets/Enemies"), unity.asset("Assets/Mats/Red.mat"). ref("Light#1234") resolves a $ref; unity.find("Player") or unity.find("Level/Pillar_1") a GameObject by unique name or path.
Stages:
  where <predicate>                  filter
  select a b.c alias=(expr)          project columns (wrap computed expressions in parentheses)
  sort <expr> [desc], ...            stable sort
  first N | skip N | count
  get <path>                         replace each item by a member (lists are flattened)
  recurse children [max-depth=N] [include-self=false]   walk the hierarchy (input items included by default)
  distinct [expr] | group-by expr | sum expr | min expr | max expr | avg expr
Expressions: == != < <= > >= and or not + - * / %; contains, starts-with, ends-with (case-insensitive); matches "regex"; x in [a, b]; x is null; x is not null; $ is Light (includes subclasses); has(member). Literals: numbers, "strings", true/false/null, lists [a, b], objects {x: 1, y: 2}.
Paths: a.b.c, list[0], map["key"]; .member on a list maps over its elements. List methods: .any(pred) .all(pred) .count([pred]) .sum(e) .min(e) .max(e) .first([pred]); inside them bare names refer to the element, falling back to the outer item.
Meta: $ (current item), $ref (stable id such as Light#1234), $type, $path, $index, $depth. Enums compare to strings (type == "Spot"); == on strings is case-sensitive.
Unity: GameObject has components (list), children (list), active, name, tag, layer, transform (position, localPosition, eulerAngles, localScale, parent). Component members are the C# members (public + [SerializeField]); find them via objects | get components | where $ is Light. Vectors/colors print as (x, y, z); write "(1, 2, 3)", [1, 2, 3] or {x: 1, y: 2, z: 3}; colors also "#ff8800". Material shader properties: properties["_Color"]. Destroyed objects read as null (missing references: where target == null). Navigate with paths or get; select only projects output columns.
Results come back as compact rows (header line, then col|col). Long results are truncated — refine with where/first or use count. Unknown members produce warnings with suggestions; rql-schema(type) lists a type's members.
```

## Changing things (rql-mutate)

```
  set path = expr, ...   assign members (paths may go through structs: set transform.position.y = transform.position.y + 1)
  call Method(args)      invoke a method the tools expose
Examples:
  objects | where name == "Player" | set transform.position = "(0, 1, 0)"
  objects | get components | where $ is Light | set intensity = intensity * 2, color = "#ffcc88"
  objects | where tag == "Enemy" | set active = false
  unity.asset("Assets/Mats/Red.mat") | set properties["_Color"] = "(1, 0, 0, 1)"
  objects | where name == "Spawner" | set targets = append(targets, ref("GameObject#1234"))
Create / add / remove (call; the receipt's result column holds the new object's $ref for follow-up queries):
  editor | call CreateGameObject("Props")                                   new root GameObject
  editor | call CreateGameObject("Crate", ref("GameObject#1234"), "Cube")    child of a parent, optional primitive
  objects | where name == "Crate" | call AddComponent("Rigidbody")           add a component by type name
  objects | where name == "Old" | call Destroy()                             remove a GameObject (components: get components | where $ is X | call Destroy())
  objects | where name == "Lamp" | set transform.parent = ref("GameObject#1234")   reparent (keeps the world position)
  objects | where name == "Crate" | call Duplicate()                          copy next to it (returns the copy)
Scenes, prefabs, assets, Editor state:
  editor | call OpenScene("Assets/Scenes/Level.unity")          editor | call NewScene("Assets/Scenes/New.unity")
  scenes | where path == "" | call SaveAs("Assets/Scenes/New.unity")   scenes | where name == "Level" | call SetActive()
  editor | call InstantiatePrefab("Assets/Prefabs/Enemy.prefab", ref("GameObject#1234"), "(0, 0, 5)")
  objects | where name == "Crate" | call SaveAsPrefab("Assets/Prefabs/Crate.prefab")
  editor | call OpenPrefab("Assets/Prefabs/Enemy.prefab")  then  prefab | recurse children | …  then  editor | call ClosePrefab()
  editor | call CreateMaterial("Assets/Mats/Glow.mat")        editor | call MoveAsset("Assets/A.mat", "Assets/Mats/A.mat")
  editor | set selection = [ref("GameObject#1234")]         editor | set isPlaying = true
  profiler | call Start()      profiler | call Save("Logs/profile.json")      (read: profiler | select memory frame)
```

## Sources

| Source | What |
|---|---|
| `animation` | animation: animators (every Animator in the loaded scenes), CreateClip(path, frameRate, loop), CreateController(path, motion) |
| `componentTypes` | component types that can be added (name fullName namespace) |
| `console` | captured Editor log entries, oldest first (last 1000): logType message timestamp stackTrace sequence |
| `editor` | Editor state (isPlaying, isPaused, selection …) and operations: CreateGameObject, OpenScene, NewScene, SaveScenes, InstantiatePrefab, OpenPrefab / SavePrefab / ClosePrefab, CopyAsset / MoveAsset / DeleteAsset / CreateFolder, CreateMaterial, SaveAssets, RefreshAssets, ClearConsole, ExecuteMenuItem (rql-schema "editor" for signatures) |
| `objects` | every GameObject in the loaded scenes, depth-first, inactive ones included |
| `prefab` | root GameObject of the prefab open in prefab editing mode (none otherwise) |
| `profiler` | Unity profiler: enabled supported memory rendering script frame modules (live snapshots, MB / ms); calls Start, Stop, ClearFrames, EnableModule, Save, Load (rql-schema "profiler" for signatures) |
| `roots` | root GameObjects of the loaded scenes |
| `scenes` | loaded scenes |
| `selection` | objects selected in the Editor |
| `shaders` | every shader the Editor knows (built-in and project): name supported hasErrors |
| `testResults` | results of the last tests-run: name status durationMs message stackTrace |
| `tests` | test methods (fullName name fixture namespace assembly mode categories parameterized explicit ignored); run a selection with tests-run query, e.g. tests \| where fixture == "PlayerTests" |
| `unity.asset(…)` | unity.asset(path): the main asset at a path, e.g. "Assets/Mats/Red.mat" |
| `unity.assets(…)` | unity.assets(filter, folder?): AssetDatabase search, e.g. "t:Material", "t:Prefab Enemy" (at most 200, loaded as needed) |
| `unity.find(…)` | unity.find(name): the GameObject with that unique name or hierarchy path ("Level/Pillar_1"), e.g. set Follow = unity.find("Player") |

## Animation

Clips and controllers are assets: `unity.assets("t:AnimationClip")`, `unity.asset("Assets/…/Walk.anim")`,
`unity.asset("Assets/…/Player.controller")`. An AnimationClip has `curves` (path type property keyCount timeRange keys, keys with
writable time value inTangent outTangent …), `objectCurves` (sprite swaps), `events` and `settings` (loopTime loopBlend mirror …), all
writable views, plus ordinary members (frameRate wrapMode legacy length isLooping). An AnimatorController has `layers` (name
defaultWeight blendingMode defaultState `states` anyStateTransitions) and `parameters` (name type defaultFloat defaultInt defaultBool);
states and transitions are assets whose members (speed motion tag … / hasExitTime exitTime duration conditions …) are set directly.
Writes and calls are undoable; creating an asset is not. `rql-schema "AnimationClip"` / `"AnimatorController"` list the methods.

Clips (curve `path` is the child path under the Animator, "" = its own GameObject; `type` the animated component):

```
animation | call CreateClip("Assets/Animations/Walk.anim")   empty clip, 60 fps
animation | call CreateClip("Assets/Animations/Idle.anim", 30, true)   path, frameRate, loop
unity.assets("t:AnimationClip") | select name length frameRate isLooping n=(curves.count()) e=(events.count())
unity.asset("Assets/Animations/Walk.anim") | call SetCurve("", "Transform", "localPosition.y", [[0, 0], [0.5, 1], [1, 0]])   path, type, property, keys [time, value(, inTangent, outTangent)]
unity.asset("Assets/Animations/Walk.anim") | call SetCurve("Body", "SpriteRenderer", "m_Color.a", [{time: 0, value: 1}, {time: 1, value: 0, inTangent: -1}])
unity.asset("Assets/Animations/Walk.anim") | get curves | select path type property keyCount timeRange
unity.asset("Assets/Animations/Walk.anim") | get curves | where property == "localPosition.y" | get keys | select index time value inTangent outTangent
unity.asset("Assets/Animations/Walk.anim") | get curves | where property == "localPosition.y" | set keys[1].value = 2, keys[1].outTangent = 0
unity.asset("Assets/Animations/Walk.anim") | get curves | where property == "localPosition.y" | call AddKey(1.5, 0.5)   returns the key index; RemoveKey(index), SetKeys([[t, v], …]), Evaluate(t)
unity.asset("Assets/Animations/Walk.anim") | get curves | where path == "Body" | set postWrapMode = "Loop"
unity.asset("Assets/Animations/Walk.anim") | call RemoveCurve("Body", "SpriteRenderer", "m_Color.a")   or … | get curves | where … | call Remove()
unity.asset("Assets/Animations/Walk.anim") | set frameRate = 30, settings.loopTime = true
unity.asset("Assets/Animations/Walk.anim") | call AddEvent(0.5, "Footstep", "left")   time, functionName, stringParameter?, floatParameter?, intParameter?
unity.asset("Assets/Animations/Walk.anim") | get events | select index time functionName stringParameter
unity.asset("Assets/Animations/Walk.anim") | set events[0].time = 0.25, events[0].intParameter = 1
unity.asset("Assets/Animations/Walk.anim") | call RemoveEvent(0)   ClearEvents() / ClearCurves() remove all
```

Animator controllers (states by name; a trailing `layer` argument is a layer name or index, default 0):

```
animation | call CreateController("Assets/Animations/Player.controller")   Base Layer, empty state machine (CreateController(path, motion) adds a state)
unity.asset("Assets/Animations/Player.controller") | call AddParameter("Speed", "Float")   Float, Int, Bool or Trigger
unity.asset("Assets/Animations/Player.controller") | call AddParameter("Grounded", "Bool", true)   optional default value
unity.asset("Assets/Animations/Player.controller") | call AddParameter("Jump", "Trigger")
unity.asset("Assets/Animations/Player.controller") | call AddState("Idle", unity.asset("Assets/Animations/Idle.anim"))   name, motion?, layer?; the first state becomes the default
unity.asset("Assets/Animations/Player.controller") | call AddState("Walk", unity.asset("Assets/Animations/Walk.anim"))
unity.asset("Assets/Animations/Player.controller") | call AddTransition("Idle", "Walk", "Speed", "Greater", 0.1)   from, to ("Exit" = exit), parameter?, mode?, threshold?, layer?
unity.asset("Assets/Animations/Player.controller") | call AddTransition("Walk", "Idle", "Speed", "Less", 0.1)
unity.asset("Assets/Animations/Player.controller") | call AddAnyStateTransition("Idle", "Jump")   mode defaults by parameter type: Bool / Trigger If, Float Greater, Int Equals
unity.asset("Assets/Animations/Player.controller") | call SetDefaultState("Idle")
unity.asset("Assets/Animations/Player.controller") | get layers | select index name defaultWeight defaultState.name states.name
unity.asset("Assets/Animations/Player.controller") | get layers | get states | select name speed motion.name transitions.destinationState.name
unity.asset("Assets/Animations/Player.controller") | get layers | get states | where name == "Walk" | set speed = 1.5
unity.asset("Assets/Animations/Player.controller") | get layers | get states | where name == "Idle" | get transitions | set hasExitTime = true, exitTime = 0.9, duration = 0.25
unity.asset("Assets/Animations/Player.controller") | get layers | get states | where name == "Idle" | get transitions | call AddCondition("Grounded", "If")   RemoveCondition(index), ClearConditions()
unity.asset("Assets/Animations/Player.controller") | get layers | get states | get transitions | select destinationState.name hasExitTime conditions
unity.asset("Assets/Animations/Player.controller") | get parameters | select name type defaultFloat defaultBool
unity.asset("Assets/Animations/Player.controller") | get parameters | where name == "Speed" | set defaultFloat = 0.5
unity.asset("Assets/Animations/Player.controller") | call AddLayer("Upper Body")   returns the layer index
unity.asset("Assets/Animations/Player.controller") | set layers[1].defaultWeight = 0.5, layers[1].blendingMode = "Additive"
unity.asset("Assets/Animations/Player.controller") | call AddState("Wave", null, "Upper Body")
unity.asset("Assets/Animations/Player.controller") | call RemoveTransition("Walk", "Idle")   from "AnyState" / to "Exit" for those transitions
unity.asset("Assets/Animations/Player.controller") | call RemoveState("Wave", "Upper Body")
unity.asset("Assets/Animations/Player.controller") | call RemoveLayer("Upper Body")
unity.asset("Assets/Animations/Player.controller") | call RemoveTransition("AnyState", "Idle")
unity.asset("Assets/Animations/Player.controller") | call RemoveParameter("Jump")   conditions using it are removed too
```

Animator components (`animation | get animators` = every Animator in the loaded scenes):

```
editor | call CreateGameObject("Player")
objects | where name == "Player" | call AddComponent("Animator")
animation | get animators | set runtimeAnimatorController = unity.asset("Assets/Animations/Player.controller"), applyRootMotion = false
animation | get animators | select gameObject.name runtimeAnimatorController.name isHuman cullingMode
```

Parameter names and types are read-only (conditions refer to them): rename by RemoveParameter + AddParameter. New transitions
have no exit time and no blend duration; set them on the transition. Only the Animator's configuration members are readable (its
playback state is not meaningful in Edit Mode); in Play Mode `call SetParameter("Speed", 2)` / `call Play("Walk")` drive it.
Other components' AnimationCurve fields take keyframe lists too: `set curve = [[0, 0], [1, 1]]`.

## Particle systems

Particle systems are components: `objects | get components | where $ is ParticleSystem`. Module structs (`main`,
`emission`, `shape` …) are written through, even though they are get-only properties:

```
objects | get components | where $ is ParticleSystem | set main.startLifetime = 3, main.loop = false
objects | get components | where $ is ParticleSystem | set emission.rateOverTime = [5, 25]          random between two
objects | get components | where $ is ParticleSystem | set main.startColor = "#ff8800"
objects | get components | where $ is ParticleSystem | set main.startColor = [["#ff0000", 0], ["#0000ff", 1]]   gradient
```

`MinMaxCurve` takes a number or `[min, max]`; `MinMaxGradient` a color, `[color, color]` or gradient keys.
Playback in the Editor: `objects | get components | where $ is ParticleSystem | call Play()` (also Pause, Stop, Clear,
Simulate(seconds)).

## Installed RQL modules

- `com.IvanMurzak.Unity.MCP.Editor.Rql.Modules.UnityCoreModule`
- `com.IvanMurzak.Unity.MCP.Editor.Rql.ToolTypesModule`
- `com.IvanMurzak.Unity.MCP.Editor.Rql.Animation.AnimationModule`
- `com.IvanMurzak.Unity.MCP.Editor.Rql.Particles.ParticleSystemModule`
- `com.IvanMurzak.Unity.MCP.Editor.Rql.Physics3D.PhysicsModule`
