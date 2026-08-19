# CLAUDE.md

# 2D Planetoid Sandbox - Development Guide

This document defines the engineering philosophy, coding standards, and expected behavior for every autonomous coding agent contributing to this repository.

The objective is not merely to produce working code, but to build a maintainable, scalable, deterministic planetary simulation engine.

---

# Primary Objective

Build a simulation-first engine for procedurally generated, destructible, constructible, spherical planetoids.

Every system should be:

* Deterministic
* Modular
* Testable
* Extensible
* Data-driven
* Profileable

Prefer long-term architecture over short-term convenience.

---

# Core Philosophy

## Simulation First

Never fake a system that can emerge naturally.

Examples:

✔ Gravity comes from planets.

✔ Terrain comes from an SDF.

✔ Weather comes from simulation.

✔ Materials have physical properties.

✔ Terrain deformation modifies the SDF.

Avoid hardcoded gameplay whenever possible.

---

## The SDF Is The Source of Truth

Never edit meshes.

Never edit colliders.

Never edit rendered geometry.

Always edit:

The Signed Distance Field.

Everything else derives from it.

```
SDF
 │
 ├── Mesh
 ├── Collider
 ├── Materials
 ├── Physics
 ├── Rendering
 └── Gameplay
```

---

## Separation of Responsibilities

Geometry is not rendering.

Rendering is not gameplay.

Gameplay is not simulation.

Simulation is not persistence.

Avoid coupling systems together.

---

# Development Workflow

Before beginning any task:

1. Read `scope.md`.
2. Read `TASKS.md`.
3. Understand the affected systems.
4. Search for existing implementations.
5. Reuse existing abstractions.

Never create duplicate systems.

---

# Required Development Process

Every task follows this sequence.

## 1. Understand

Before writing code:

* understand the feature
* inspect nearby code
* identify dependencies

Never immediately begin coding.

---

## 2. Design

Think through:

* APIs
* ownership
* lifetime
* performance
* testing

before implementation.

---

## 3. Implement

Write the smallest implementation that fully solves the task.

Avoid speculative features.

---

## 4. Test

Every feature should be verified.

Prefer:

* Unit tests
* Integration tests
* Simulation tests

---

## 5. Validate

After implementation:

* Compile
* Run tests
* Verify editor behavior
* Check allocations
* Confirm deterministic output

---

# Architecture Rules

## Single Responsibility

Classes should have one reason to change.

Bad:

Planet

* rendering
* physics
* saving
* generation

Good:

Planet

PlanetGenerator

PlanetRenderer

PlanetCollider

PlanetGravity

PlanetSaveData

---

## Composition

Prefer composition over inheritance.

Bad

```
RockPlanet
    IcePlanet
        LavaPlanet
```

Good

```
Planet

+ Generator

+ MaterialProfile

+ AtmosphereProfile

+ GravityProfile
```

---

## Data Driven

Avoid hardcoded behavior.

Configuration belongs in:

ScriptableObjects

or

readonly configuration objects

Never scatter constants throughout the codebase.

---

# Determinism

Every procedural system must be deterministic.

Given:

Seed = X

The generated output must always be identical.

Never use:

Random()

Use:

Seeded random generators.

All randomness must originate from the planet seed.

---

# Terrain Rules

Terrain is represented only by the Signed Distance Field.

Never create gameplay systems that depend on meshes.

Meshes are disposable.

Colliders are disposable.

The SDF is permanent.

---

# Chunk Rules

Planets are chunked.

Never regenerate an entire planet.

Only dirty chunks rebuild.

Dirty regions should be propagated minimally.

---

# Performance Rules

Avoid:

* GC allocations
* boxing
* LINQ in update loops
* reflection
* string allocations
* recursive simulation

Prefer:

* NativeArrays
* object pooling
* stack allocation where appropriate
* reusable buffers

---

# Memory Rules

Generated data should not be stored unless necessary.

Store:

* seed
* edits
* player structures

Regenerate everything else.

---

# Mesh Generation

Meshes should be treated as caches.

Never store gameplay information in meshes.

Mesh generation should be completely reproducible.

---

# Collision

Collision should derive from generated geometry.

Never use collision as authoritative gameplay data.

---

# Planet Generation

Generation order:

Planet DNA

↓

Large Scale Shape

↓

Terrain Height

↓

Geological Layers

↓

Caves

↓

Ore

↓

Materials

↓

Vegetation

↓

Entities

Never generate these out of order.

---

# Materials

Materials define:

* hardness
* density
* friction
* conductivity
* melting point
* mining speed
* color

Do not hardcode gameplay behavior for specific materials.

Use material properties.

---

# Simulation Order

Prefer a consistent simulation order.

Example:

Gravity

↓

Planet Physics

↓

Terrain Changes

↓

Atmosphere

↓

Water

↓

Temperature

↓

Entities

↓

Rendering

Avoid cyclic dependencies.

---

# Debugging

Every major system should expose debugging tools.

Examples:

Chunk borders

Normals

Density field

Ore

Materials

Gravity

Weather

Temperature

Pressure

Never build "black box" systems.

---

# Logging

Logging should be:

Useful

Actionable

Minimal

Avoid spam.

Use structured messages.

Debug logs should be removable.

---

# Error Handling

Never silently ignore failures.

Prefer:

* validation
* assertions
* informative exceptions

Fail early during development.

---

# Public APIs

Public APIs should:

Be documented

Be deterministic

Validate inputs

Avoid unnecessary allocations

Avoid exposing implementation details.

---

# Refactoring

If refactoring:

Do not mix feature work.

Refactor only what is necessary.

Keep behavior identical.

---

# Naming

Prefer descriptive names.

Good

TerrainChunk

PlanetGravity

MaterialDatabase

PlanetGenerator

Bad

Manager2

Helper

Utils

Thing

Data2

---

# Methods

Methods should:

Do one thing.

Prefer under ~40 lines.

Extract helpers instead of nesting deeply.

Avoid boolean flag arguments.

---

# Classes

Prefer:

Small focused classes.

Avoid:

God objects.

---

# Interfaces

Create interfaces only when:

There are multiple implementations

or

A stable abstraction exists.

Avoid premature abstraction.

---

# Testing Requirements

Every feature should have one or more of:

Unit test

Integration test

Simulation test

Debug visualization

If something cannot be tested automatically,

provide an interactive debug scene.

---

# Profiling

Before optimizing:

Measure.

Never optimize based on assumptions.

Profile:

CPU

GC

Memory

Simulation time

Mesh generation

---

# Unity Guidelines

Prefer:

[SerializeField]

readonly

private fields

Avoid unnecessary public members.

Keep MonoBehaviours thin.

Place simulation in plain C# classes whenever possible.

---

# Future Compatibility

Every system should be designed assuming future migration to:

Unity Jobs

Burst

GPU compute

Networking

Do not tightly couple systems to MonoBehaviours.

---

# Autonomous Agent Behavior

An implementation agent should:

* Read project documentation before coding.
* Understand the architecture before modifying it.
* Preserve determinism.
* Prefer incremental changes.
* Keep commits focused.
* Keep the project compiling.
* Add tests with new functionality.
* Avoid duplicate code.
* Improve code quality when touching nearby systems.
* Leave the codebase cleaner than it was found.

---

# Things To Avoid

Do not:

* Rewrite unrelated systems.
* Introduce unnecessary dependencies.
* Create giant classes.
* Create giant methods.
* Store generated data unnecessarily.
* Mix rendering with simulation.
* Mix gameplay with terrain generation.
* Use magic numbers.
* Leave commented-out code.
* Ignore warnings.
* Add "temporary" hacks without documenting them.

---

# Definition of Excellent Code

Excellent code is:

Easy to read.

Easy to test.

Easy to profile.

Easy to extend.

Easy to debug.

Deterministic.

Modular.

Data-oriented.

Well documented.

Free of unnecessary complexity.

The project should evolve into a reusable planetary simulation framework where new systems can be added with minimal modification to existing code, and every simulation ultimately derives from the authoritative planetary data model rather than from rendered representations.
