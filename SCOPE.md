# scope.md

# Project Scope — 2D Destructible Planetoid Sandbox

## Vision

Create a simulation-driven 2D sandbox game centered around fully destructible and constructible spherical planetoids. Every planet should feel like a small, living world with believable geology, atmosphere, gravity, and ecosystems.

The terrain is not tile-based or voxel-based. Instead, every planet is represented as a continuous Signed Distance Field (SDF), allowing smooth terrain deformation, natural caves, geological layers, and procedural generation.

The long-term objective is to build a reusable planetary simulation framework rather than a one-off game.

---

# Core Design Principles

## Simulation First

Every major system should emerge from simulation rather than scripted behavior whenever practical.

Examples:

* Terrain exists because of an SDF.
* Gravity comes from planetary mass.
* Weather follows physical simulation.
* Ore distribution follows geology.
* Water follows terrain.
* Planet collisions physically deform terrain.

Avoid systems that simply "fake" outcomes when they can naturally emerge from existing simulations.

---

## Continuous Terrain

Terrain should never be grid-aligned or block-based.

Goals:

* Smooth surfaces
* Arbitrary caves
* Overhangs
* Natural cliffs
* Infinite digging precision
* Arbitrary terrain construction

Terrain modifications should operate on scalar fields instead of discrete tiles.

---

## Planet-Centric Design

Everything should assume that worlds are spherical.

Systems should never assume:

* Flat worlds
* Fixed "up"
* Global gravity direction

Instead:

* Gravity points toward planetary centers.
* Terrain generation is radial.
* Biomes follow planetary geometry.
* Weather wraps naturally around planets.

---

## Procedural Everything

Every planet should be generated from a deterministic seed.

Planet seeds should generate:

* Radius
* Gravity
* Geological composition
* Terrain
* Cave systems
* Ore deposits
* Core composition
* Biomes
* Atmosphere
* Weather parameters

Planets should require minimal stored data.

Only player modifications should be saved.

---

# Terrain System

## Representation

Terrain is stored as a Signed Distance Field.

The SDF is the authoritative representation of geometry.

Rendering, collision, and gameplay derive from it.

---

## Terrain Properties

Geometry is independent from material.

Each location may contain:

* Distance
* Material
* Hardness
* Damage
* Moisture
* Temperature
* Pressure
* Density
* Custom simulation layers

Not every property must be explicitly stored.

Whenever possible:

* Generate procedurally.
* Cache when needed.
* Persist only modifications.

---

## Terrain Modification

Support:

* Digging
* Construction
* Explosions
* Erosion
* Lava
* Planetary impacts

Terrain modification should be implemented through field operations.

Never manipulate rendered meshes directly.

---

## Chunking

Planets are divided into chunks.

Each chunk owns:

* Density samples
* Mesh
* Collider
* Dirty state

Only modified chunks rebuild.

No full-planet regeneration.

---

# Geological Simulation

Planet interiors should have meaningful structure.

Examples:

* Soil
* Sand
* Stone
* Granite
* Basalt
* Metallic core
* Ice
* Magma

Generation should be based on:

* Depth
* Heat
* Pressure
* Noise
* Planet DNA

---

## Ore Generation

Ore should not be manually placed.

Instead generate procedurally from:

* Geological layers
* Pressure
* Temperature
* Noise fields
* Mineral simulation

Examples:

* Iron
* Copper
* Gold
* Uranium
* Crystal formations

---

## Cave Generation

Caves should emerge from procedural field operations.

Support:

* Lava tubes
* Caverns
* Worm tunnels
* Crystal caves
* Underground lakes

Avoid repetitive noise-generated tunnels.

---

# Planet Generation

Every planet has procedural DNA.

Example parameters:

* Radius
* Density
* Gravity
* Core radius
* Crust thickness
* Mountain height
* Roughness
* Cave frequency
* Ore richness
* Atmospheric pressure
* Rotation
* Volcanic activity

The generator should support many planet archetypes without changing code.

---

# Planet Physics

Each planet behaves as an independent celestial body.

Support:

* Gravity
* Rotation
* Orbital mechanics (future)
* Planet collisions
* Surface deformation

---

## Planet Collisions

Planets should eventually support:

Temporary deformation

Permanent deformation

Crater formation

Material displacement

Planet merging (stretch goal)

Terrain deformation should occur through the SDF, not through mesh manipulation.

---

# Rendering

Terrain rendering should support:

* Smooth interpolation
* Multiple material layers
* Dynamic rebuilding
* Large planets
* Small planets
* Multiple planets

Future enhancements:

* Tessellation
* GPU meshing
* Adaptive LOD
* Material blending

---

# Collision

Collision geometry is generated from terrain.

Colliders regenerate only for modified chunks.

Gameplay should never collide directly against density fields.

---

# Gravity

Gravity is radial.

Every object experiences gravity toward its nearest dominant planetary body.

Future support:

* Multi-body gravity
* Gravity blending
* Artificial gravity

---

# Atmosphere

Atmosphere should eventually become a full simulation.

Goals:

Pressure

Temperature

Humidity

Wind

Clouds

Rain

Storms

Heat transfer

Atmospheric escape

Weather should react to:

* Terrain
* Planet rotation
* Solar heating
* Water
* Player modifications

---

# Water

Water is planned as a simulation rather than static tiles.

Support:

* Lakes
* Oceans
* Rivers
* Groundwater
* Ice
* Steam

Water should interact with:

Terrain

Atmosphere

Temperature

Pressure

---

# Materials

Materials should define gameplay.

Each material may contain:

* Density
* Hardness
* Friction
* Thermal conductivity
* Melting point
* Color
* Structural strength

Gameplay should derive from these properties.

---

# Buildings

Buildings should integrate into terrain.

Support:

* Automatic terrain shaping
* Foundations
* Anchoring
* Destruction
* Terraforming

Buildings should not require flat tile grids.

---

# Performance Goals

Support:

* Multiple active planets
* Continuous terrain editing
* Real-time rebuilding
* Large simulation distances
* Smooth frame rates

Avoid:

* Global updates
* Full planet rebuilds
* Unnecessary allocations

Favor:

* Dirty chunk updates
* Job systems
* Burst compilation
* GPU acceleration where appropriate

---

# Architecture

Prefer modular systems.

Suggested modules:

* Planet System
* Terrain System
* Meshing System
* Collider System
* Planet Generator
* Material System
* Weather System
* Water System
* Gravity System
* Physics System
* Rendering System
* Save System

Modules should communicate through well-defined interfaces.

Avoid circular dependencies.

---

# Saving

Persist only:

* Planet seed
* Player modifications
* Constructed objects
* Dynamic entities

Never serialize generated terrain if it can be reconstructed.

---

# Coding Philosophy

Prioritize:

* Deterministic generation
* Data-oriented design
* Clear separation of simulation and rendering
* Extensible systems
* Testability
* Profiling before optimization

Avoid premature optimization, but design APIs that can later migrate to Jobs, Burst, or GPU implementations.

---

# Long-Term Goals

* Planet-scale weather simulation
* Geological simulation
* Dynamic tectonics
* Volcanoes
* Plate movement
* Procedural ecosystems
* Terraforming
* Planetary engineering
* Space travel
* Planet collisions
* Planet merging
* Multiplayer synchronization
* Massive procedural solar systems

---

# Non-Goals

The following are intentionally out of scope for the initial implementation:

* Tile-based terrain
* Block-based mining
* Pre-authored maps
* Scripted terrain generation
* Fixed gravity direction
* Flat-world assumptions
* Hardcoded biome placement
* Manual ore placement
* Mesh-first terrain editing

Every core system should be designed to support continuous, simulation-driven planetary worlds.
