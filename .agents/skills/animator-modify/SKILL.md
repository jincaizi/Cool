---
name: animator-modify
description: |-
  Modify Unity's AnimatorController asset. Apply an array of modifications including adding/removing parameters, layers, states, and transitions. Use 'animator-get-data' tool to get valid names and parameters for modifications.
  
  Argument types — give these arguments as RQL values (as in rql-mutate): {key: value, …}, [a, b], "text", 1.5, true, ref("GameObject#12") from a query result, unity.find("Name"); leave out keys you do not need:
  AssetObjectRef {InstanceID int (instanceID of the UnityEngine.Object. If this is '0' and 'assetPath' and 'assetGuid' is not provided, empty or null, then it will be used as 'null'.), AssetType Type (Type of the asset.), AssetPath string (Path to the asset within the project. Starts with 'Assets/'), AssetGuid string (Unique identifier for the asset.)} — Reference to UnityEngine.Object asset instance. It could be Material, ScriptableObject, Prefab, and any other Asset. Anything located in the Assets and Packages folders.
  Type {}
  AnimatorModification {type…
metadata:
  generated-by: mcp-plugin-dotnet
---

# Animator / Modify

## How to Call

```bash
unity-mcp-cli run-tool animator-modify --input '{
  "animatorRef": "string_value",
  "modifications": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use:
> ```bash
> unity-mcp-cli run-tool animator-modify --input-file args.json
> ```
>
> Or pipe via stdin (recommended):
> ```bash
> unity-mcp-cli run-tool animator-modify --input-file - <<'EOF'
> {"param": "value"}
> EOF
> ```


### Troubleshooting

If `unity-mcp-cli` is not found, either install it globally (`npm install -g unity-mcp-cli`) or use `npx unity-mcp-cli` instead.
Read the /unity-initial-setup skill for detailed installation instructions.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `animatorRef` | `string` | Yes | Reference to the AnimatorController asset to modify. RQL: AssetObjectRef |
| `modifications` | `string` | Yes | Array of modifications to apply to the controller. RQL: [AnimatorModification] |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "animatorRef": {
      "type": "string"
    },
    "modifications": {
      "type": "string"
    }
  },
  "required": [
    "animatorRef",
    "modifications"
  ]
}
```

## Output

This tool does not return structured output.

