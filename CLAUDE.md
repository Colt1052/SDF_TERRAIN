# CLAUDE.md — Agent Rules

## Reading Order

1. **This file** — agent behavior rules.
2. **README.md** — project overview, folder layout, current state.
3. **SCOPE.md** — vision and design principles (read for *why* something exists).
4. **ARCHITECTURE.md** — system layers, tech decisions, data flow.
5. **TASKS.md** — task tracker (completed = one-line summary; future = full spec).
6. **docs/task-archive.md** — deep implementation history (read only when working on a subsystem whose past decisions matter).

---

## Core Philosophy

### Simulation First
Never fake a system that can emerge naturally. Gravity comes from planets. Terrain comes from an SDF. Weather comes from simulation. Materials have physical properties. Avoid hardcoded gameplay.

### The SDF Is The Source of Truth
Never edit meshes, colliders, or rendered geometry directly. Always edit the Signed Distance Field. Everything else derives from it:
```
SDF -> Mesh -> Collider -> Materials -> Physics -> Rendering -> Gameplay
```

### Determinism
Every procedural system must be deterministic. Given Seed = X, output is always identical. Never use `UnityEngine.Random` — use `SeededRandom` only.

### Separation of Responsibilities
Geometry != rendering != gameplay != simulation != persistence. Avoid coupling systems.

---

## Agent Workflow

Before any task:
1. Read README.md for orientation.
2. Read TASKS.md to find the task.
3. Inspect affected code and dependencies.
4. Reuse existing abstractions — never create duplicates.

Every task follows: Understand -> Design -> Implement -> Test -> Validate.

---

## Architecture Rules

### Single Responsibility
Classes have one reason to change. Prefer small focused classes over god objects.

### Composition over Inheritance
`Planet + Generator + MaterialProfile` not `RockPlanet : IcePlanet : Planet`.

### Data-Driven
Configuration belongs in ScriptableObjects or readonly config objects. Never scatter constants.

### Chunking
Planets are chunked. Only dirty chunks rebuild. Never regenerate an entire planet.

### Memory
Store: seed, edits, player structures. Regenerate everything else.

---

## Coding Standards

### Naming
Descriptive: `TerrainChunk`, `PlanetGravity`, `MaterialDatabase`. Never: `Manager2`, `Helper`, `Utils`.

### Methods
Do one thing. Prefer under ~40 lines. Extract helpers over nesting. No boolean flag arguments.

### Unity
- Prefer `[SerializeField] private readonly` fields.
- Keep MonoBehaviours thin; put simulation in plain C# classes.
- Design APIs assuming future Jobs/Burst/GPU migration.

### Performance
Avoid: GC allocations, LINQ in updates, reflection, string allocations, recursive simulation.
Prefer: NativeArrays, object pooling, stack allocation, reusable buffers.

### Testing
Every system gets at least one of: unit test, integration test, debug visualization.

### Logging
Useful, actionable, minimal. Structured messages. No spam.

---

## Things to Avoid
- Rewriting unrelated systems
- Creating giant classes or methods
- Storing generated data unnecessarily
- Mixing rendering with simulation, or gameplay with terrain generation
- Using magic numbers
- Leaving commented-out code or ignoring warnings
- Introducing unnecessary dependencies or premature abstractions
- Breaking determinism
