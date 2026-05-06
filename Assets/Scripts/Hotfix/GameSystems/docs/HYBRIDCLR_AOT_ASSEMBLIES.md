# HybridCLR AOT Assemblies

This document lists assemblies configured as **AOT (Ahead-of-Time)** in the HybridCLR hotfix system.

## AOT Assemblies

### Core
- **Assembly Name**: `Hotfix.GameSystems.Sys3C.Core`
- **Purpose**: Contains shared interfaces, data structures, and core systems used across multiple modules
- **Reason**: Contains shared interfaces and data definitions that rarely change and are referenced by multiple hotfix assemblies

### Combat
- **Assembly Name**: `Hotfix.GameSystems.Combat`
- **Purpose**: Combat logic, damage calculations, and combat state management
- **Reason**: Contains shared combat interfaces and data definitions that rarely change

## Why These Are AOT

These assemblies are marked as AOT because they:
1. **Contain shared interfaces/data** - Other hotfix assemblies depend on types defined here
2. **Rarely change** - Business logic evolves independently; these provide stable contracts
3. **Reduce coupling** - Changes to specific game systems (skills, AI) don't require recompiling shared types

## AOT vs Hotfix Assembly Behavior

| Aspect | AOT Assembly | Hotfix Assembly |
|--------|-------------|-----------------|
| Code changes | Require full rebuild | Hot-reloadable at runtime |
| IL2CPP compilation | Always included | Conditional |
| `hotfixAfterAssemblyLoaded` | `false` | `true` |
| Usage | Shared interfaces, stable contracts | Frequently changing logic |

## Configuration in asmdef

Both assemblies have:
```json
{
    "precompiledReferences": [],
    "autoReferenced": true
}
```

This marks them as AOT assemblies in the HybridCLR configuration.

## Adding New AOT Assemblies

If you need to add more assemblies as AOT:
1. Ensure the asmdef has the above configuration
2. Update this document
3. Consider if the assembly truly belongs in AOT (shared, stable)

## Related Documentation

- [HybridCLR Official Docs](https://hybridclr.docice.cn/)
- [Hotfix Assembly Architecture](./README.md)