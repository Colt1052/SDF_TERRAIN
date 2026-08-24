using UnityEngine;
using SDFTerrain.Materials;
using SDFTerrain.Planet;

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

        private void Start()
        {
            Planet.Planet planet = GetComponent<Planet.Planet>();
            PlanetSettings effectiveSettings = settings ?? CreateDefaultSettings();
            float gravity = effectiveSettings.GravityStrength;
            planet.Initialize(effectiveSettings, seed, radius, gravity);

            TerrainField field = PlanetGenerator.GenerateBaseShape(radius, planet.Seed);
            ChunkGrid chunkGrid = new ChunkGrid(radius, chunkSize);

            ChunkTerrainRenderer renderer = GetComponent<ChunkTerrainRenderer>();
            renderer.Initialize(field, chunkGrid, radius);

            // Geological layers — vertex-color rendering by depth
            var profile = GeologicalProfile.EarthLike(seed, 0.3f);
            renderer.SetGeologicalProfile(profile);

            // Material from shader — no .mat asset required
            var shader = Shader.Find("SDFTerrain/VertexColor");
            if (shader != null)
            {
                renderer.SetMaterial(new Material(shader));
            }

            // Zoom camera to see the whole planet
            Camera cam = Camera.main;
            if (cam != null && cam.orthographic)
            {
                cam.orthographicSize = radius + 10f;
            }

            renderer.RebuildDirtyChunks();

            SDFDebugView sdfDebugView = GetComponent<SDFDebugView>();
            MarchingSquaresGridDebugView gridDebugView = GetComponent<MarchingSquaresGridDebugView>();
            sdfDebugView.Initialize(field, radius);
            gridDebugView.Initialize(field, chunkGrid, renderer.CellSize);

            renderer.TerrainChanged += sdfDebugView.NotifyTerrainChanged;
            renderer.TerrainChanged += gridDebugView.NotifyTerrainChanged;
        }

        private static PlanetSettings CreateDefaultSettings()
        {
            var settings = ScriptableObject.CreateInstance<PlanetSettings>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            return settings;
        }
    }
}
