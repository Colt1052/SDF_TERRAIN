using UnityEngine;
using SDFTerrain.Core;
using SDFTerrain.Materials;
using SDFTerrain.Resources;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Demo/debug driver: applies a <see cref="TerrainBrush"/> at the mouse's planet-local
    /// position on click, so a brush tool is testable without any gameplay/input-system tooling.
    /// Left click removes (digs) terrain, right click adds (builds) terrain. Not part of the
    /// simulation pipeline itself, mirroring <see cref="PlanetDemo"/>'s role as a thin wiring
    /// script for manual testing.
    ///
    /// When <see cref="excavationSystem"/> is assigned, Remove routes through
    /// <see cref="TerrainExcavationSystem.Excavate"/> (materials + inventory) and Add routes
    /// through <see cref="TerrainExcavationSystem.Place"/> (resource consumption + material placement).
    /// </summary>
    public class MouseTerrainEditor : MonoBehaviour
    {
        [SerializeField] private ChunkTerrainRenderer chunkTerrainRenderer;
        [SerializeField] private Transform planetCenter;
        [SerializeField] private float brushRadius = 2f;
        [SerializeField] private Camera targetCamera;

        [Tooltip("Optional: routes mining/placing through the excavation pipeline (materials, inventory, resources)")]
        [SerializeField] private TerrainExcavationSystem excavationSystem;

        [Tooltip("Material to place when right-clicking (requires excavationSystem)")]
        [SerializeField] private string placeMaterialId = "stone";

        /// <summary>
        /// Wires the excavation system at runtime. TerrainExcavationSystem is not a MonoBehaviour,
        /// so it cannot be assigned in the Inspector.
        /// </summary>
        public void SetExcavationSystem(TerrainExcavationSystem system)
        {
            excavationSystem = system;
        }

        private void Update()
        {
            if (Input.GetMouseButton(0))
            {
                ApplyBrushAtMouse(BrushMode.Remove);
            }
            else if (Input.GetMouseButton(1))
            {
                ApplyBrushAtMouse(BrushMode.Add);
            }
        }

        private void ApplyBrushAtMouse(BrushMode mode)
        {
            Camera camera = targetCamera != null ? targetCamera : Camera.main;
            if (camera == null)
                return;

            Vector2 worldPosition = camera.ScreenToWorldPoint(Input.mousePosition);
            Vector2 planetCenterPosition = planetCenter != null ? (Vector2)planetCenter.position : Vector2.zero;
            Vector2 localPosition = PlanetCoordinates.WorldToLocal(worldPosition, planetCenterPosition);

            // If excavation system is wired, use it for mining and placing.
            if (excavationSystem != null)
            {
                ExcavateOrPlace(mode, localPosition);
                return;
            }

            // Fallback: direct brush (SDF only, no material/inventory tracking).
            var brush = new TerrainBrush(mode, brushRadius);
            chunkTerrainRenderer.ApplyBrush(brush, localPosition);
        }

        private void ExcavateOrPlace(BrushMode mode, Vector2 localPosition)
        {
            int chunkIndex = chunkTerrainRenderer.GetChunkIndex(localPosition);

            if (mode == BrushMode.Remove)
            {
                excavationSystem.Excavate(localPosition, brushRadius, chunkIndex);
            }
            else
            {
                MaterialId materialId = MaterialDatabase.Instance.GetMaterialId(placeMaterialId);
                if (!materialId.IsValid)
                {
                    Debug.LogWarningFormat("[MouseTerrainEditor] Material \"{0}\" not found in database. Cannot place.", placeMaterialId);
                    return;
                }
                excavationSystem.Place(localPosition, brushRadius, materialId, placeMaterialId);
            }

            // Mark affected chunks dirty and rebuild (excavation system edits SDF but doesn't touch rendering).
            float minX = localPosition.x - brushRadius;
            float maxX = localPosition.x + brushRadius;
            float minY = localPosition.y - brushRadius;
            float maxY = localPosition.y + brushRadius;
            bool createChunks = (mode == BrushMode.Add);
            chunkTerrainRenderer.MarkDirtyRectAndRebuild(minX, maxX, minY, maxY, createChunks);
        }
    }
}
