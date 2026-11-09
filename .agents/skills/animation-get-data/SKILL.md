---
name: animation-get-data
description: |-
  Get data about a Unity AnimationClip asset file. Returns information such as name, length, frame rate, wrap mode, animation curves, and events.
  
  Argument types — give these arguments as RQL values (as in rql-mutate): {key: value, …}, [a, b], "text", 1.5, true, ref("GameObject#12") from a query result, unity.find("Name"); leave out keys you do not need:
  AssetObjectRef {InstanceID int (instanceID of the UnityEngine.Object. If this is '0' and 'assetPath' and 'assetGuid' is not provided, empty or null, then it will be used as 'null'.), AssetType Type (Type of the asset.), AssetPath string (Path to the asset within the project. Starts with 'Assets/'), AssetGuid string (Unique identifier for the asset.)} — Reference to UnityEngine.Object asset instance. It could be Material, ScriptableObject, Prefab, and any other Asset. Anything located in the Assets and Packages folders.
  Type {}
metadata:
  generated-by: mcp-plugin-dotnet
---

# Animation / Get Data

## How to Call

```bash
unity-mcp-cli run-tool animation-get-data --input '{
  "animRef": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use:
> ```bash
> unity-mcp-cli run-tool animation-get-data --input-file args.json
> ```
>
> Or pipe via stdin (recommended):
> ```bash
> unity-mcp-cli run-tool animation-get-data --input-file - <<'EOF'
> {"param": "value"}
> EOF
> ```


### Troubleshooting

If `unity-mcp-cli` is not found, either install it globally (`npm install -g unity-mcp-cli`) or use `npx unity-mcp-cli` instead.
Read the /unity-initial-setup skill for detailed installation instructions.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `animRef` | `string` | Yes | Reference to the animation asset. The path should start with 'Assets/' and end with '.anim'. RQL: AssetObjectRef |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "animRef": {
      "type": "string"
    }
  },
  "required": [
    "animRef"
  ]
}
```

## Output

This tool does not return structured output.

