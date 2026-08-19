using UnityEngine;
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
            planet.Initialize(settings, seed, radius, settings.GravityStrength);

            TerrainField field = PlanetGenerator.GenerateBaseShape(radius, planet.Seed);
            ChunkGrid chunkGrid = new ChunkGrid(radius, chunkSize);

            ChunkTerrainRenderer renderer = GetComponent<ChunkTerrainRenderer>();
            renderer.Initialize(field, chunkGrid, radius);
            renderer.RebuildDirtyChunks();

            SDFDebugView sdfDebugView = GetComponent<SDFDebugView>();
            MarchingSquaresGridDebugView gridDebugView = GetComponent<MarchingSquaresGridDebugView>();
            sdfDebugView.Initialize(field, radius);
            gridDebugView.Initialize(field, chunkGrid, renderer.CellSize);

            renderer.TerrainChanged += sdfDebugView.NotifyTerrainChanged;
            renderer.TerrainChanged += gridDebugView.NotifyTerrainChanged;
        }
    }
}
