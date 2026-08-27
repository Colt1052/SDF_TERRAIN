using UnityEngine;
using SDFTerrain.Core;
using SDFTerrain.Materials;
using SDFTerrain.Planet;
using SDFTerrain.Resources;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Demo driver: generates a planet's base terrain field on Start and hands it to the
    /// ChunkTerrainRenderer on the same GameObject, so a planet is visible without any editor
    /// tooling. Not part of the simulation pipeline itself — a thin wiring script for manual
    /// testing.
    /// </summary>
    [RequireComponent(typeof(Planet.Planet))]
    [RequireComponent(typeof(ChunkTerrainRenderer))]
    [RequireComponent(typeof(SDFDebugView))]
    [RequireComponent(typeof(MarchingSquaresGridDebugView))]
    public class PlanetDemo : MonoBehaviour
    {
        [SerializeField] private PlanetSettings settings;
        [SerializeField] private int seed = 1;
        [SerializeField] private float radius = 30f;
        [SerializeField] private float chunkSize = 15f;

        /// <summary>
        /// The excavation system created during Start. Accessible via GetComponent or
        /// by referencing this PlanetDemo from other components.
        /// </summary>
        public TerrainExcavationSystem ExcavationSystem { get; private set; }

        private void Start()
        {
            Planet.Planet planet = GetComponent<Planet.Planet>();
            planet.Initialize(seed, radius);

            TerrainField field = PlanetGenerator.GenerateBaseShape(radius, planet.Seed);
            ChunkGrid chunkGrid = new ChunkGrid(radius, chunkSize);

            ChunkTerrainRenderer renderer = GetComponent<ChunkTerrainRenderer>();
            renderer.Initialize(field, chunkGrid, radius);

            // Geological layers — vertex-color rendering by depth
            var profile = GeologicalProfile.EarthLike(seed, 0.3f);
            renderer.SetGeologicalProfile(profile);

            // --- Material system wiring ---

            // Initialize MaterialDatabase from ScriptableObject assets
            var database = MaterialDatabase.Instance;
            database.Initialize();

            // Create MaterialLayer with geological fallback and chunk-indexed editing
            var materialLayer = new MaterialLayer(profile, database);
            materialLayer.EnableChunkIndexing(chunkGrid);
            renderer.SetMaterialLayer(materialLayer);

            // Create excavation pipeline: inventory + yield table + system
            var inventory = new Inventory();
            var yieldTable = ResourceYieldTable.Default(database);
            ExcavationSystem = new TerrainExcavationSystem(field, materialLayer, inventory, yieldTable, database);

            // Wire WorldPersistence if present
            var persistence = GetComponent<WorldPersistence>();
            if (persistence != null)
            {
                persistence.Configure(materialLayer, ExcavationSystem);
            }

            // Wire MouseTerrainEditor to use excavation system
            var mouseEditor = GetComponent<MouseTerrainEditor>();
            if (mouseEditor != null)
            {
                mouseEditor.SetExcavationSystem(ExcavationSystem);
            }

            // --- End material system wiring ---

            // Material from shader — create at runtime so no .mat asset is required.
            // If the shader isn't found, leave the material as-is (Inspector-assigned).
            var shader = Shader.Find("SDFTerrain/VertexColor");
            if (shader != null)
            {
                Material mat = new Material(shader);
                // Pass planet radius so the shader can compute depth from center.
                mat.SetFloat("_PlanetRadius", radius);
                renderer.SetMaterial(mat);
            }
            else
            {
                Debug.LogErrorFormat("[PlanetDemo] Shader 'SDFTerrain/VertexColor' not found. Assign a material to ChunkTerrainRenderer manually, or check the shader compiles.");
            }

            // Zoom camera to see the whole planet
            Camera cam = Camera.main;
            if (cam != null && cam.orthographic)
            {
                cam.orthographicSize = radius + 10f;
            }

            renderer.RebuildDirtyChunks();

            // Debug views
            SDFDebugView sdfDebugView = GetComponent<SDFDebugView>();
            MarchingSquaresGridDebugView gridDebugView = GetComponent<MarchingSquaresGridDebugView>();
            sdfDebugView.Initialize(field, radius);
            gridDebugView.Initialize(field, chunkGrid, renderer.CellSize);

            // Game-view debug overlay (FPS, edit count)
            var gameDebugOverlay = GetComponent<GameDebugOverlay>();
            if (gameDebugOverlay != null)
            {
                gameDebugOverlay.Initialize(field);
            }

            // MaterialDebugView — visualize material distribution
            var materialDebugView = GetComponent<MaterialDebugView>();
            if (materialDebugView != null)
            {
                materialDebugView.Initialize(field, materialLayer, radius);
            }

            // Geological layer diagnostic — logs depth/material table to Console
            var geoDiagnostic = GetComponent<GeologicalLayerDiagnostic>();
            if (geoDiagnostic != null)
            {
                geoDiagnostic.Initialize(field, materialLayer, profile, radius);
            }

            renderer.TerrainChanged += sdfDebugView.NotifyTerrainChanged;
            renderer.TerrainChanged += gridDebugView.NotifyTerrainChanged;
            if (materialDebugView != null)
            {
                renderer.TerrainChanged += materialDebugView.NotifyTerrainChanged;
            }
        }
    }
}
