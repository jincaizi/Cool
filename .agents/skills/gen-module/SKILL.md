---
name: gen-module
description: generate code for $ARGUMENTS module
disable-model-invocation: true
---


## Workflow
1. Read `AGENTS.md` to understand the project structure.
2. Locate and read the `$ARGUMENTS-pre.md` file if exist.
3. Make full use of existing code under the `Scripts/` folder (core framework, shared modules, completed modules) to ensure reusability and consistency.
4. Add XML comments for key logic.
5. Ensure there are no compilation errors.
6. Generate `$ARGUMENTS-gen.md` at the same dir of `$ARGUMENTS-pre.md` , which includes: module overview, design approach, class responsibilities, important interface, key methods, dependencies, and potential risks.

## Important Notes
- Strictly follow the requirements in `-pre.md`; any modifications must be approved.
- Program to interfaces to reduce coupling.
- Pay attention to performance, and appropriately use multithreading and asynchronous programming.