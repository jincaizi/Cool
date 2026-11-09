---
name: animation-create
description: Create Unity's Animation asset files (AnimationClip). Creates folders recursively if they do not exist. Each path should start with 'Assets/' and end with '.anim'.
metadata:
  generated-by: mcp-plugin-dotnet
---

# Animation / Create

## How to Call

```bash
unity-mcp-cli run-tool animation-create --input '{
  "sourcePaths": []
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use:
> ```bash
> unity-mcp-cli run-tool animation-create --input-file args.json
> ```
>
> Or pipe via stdin (recommended):
> ```bash
> unity-mcp-cli run-tool animation-create --input-file - <<'EOF'
> {"param": "value"}
> EOF
> ```


### Troubleshooting

If `unity-mcp-cli` is not found, either install it globally (`npm install -g unity-mcp-cli`) or use `npx unity-mcp-cli` instead.
Read the /unity-initial-setup skill for detailed installation instructions.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `sourcePaths` | `array` | Yes | The paths of the animation assets to create. Each path should start with 'Assets/' and end with '.anim'. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "sourcePaths": {
      "type": "array",
      "items": {
        "type": "string"
      }
    }
  },
  "required": [
    "sourcePaths"
  ]
}
```

## Output

This tool does not return structured output.

