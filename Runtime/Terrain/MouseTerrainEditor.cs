using UnityEngine;
using SDFTerrain.Core;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Demo/debug driver: applies a <see cref="TerrainBrush"/> at the mouse's planet-local
    /// position on click, so a brush tool is testable without any gameplay/input-system tooling.
    /// Left click removes (digs) terrain, right click adds (builds) terrain. Not part of the
    /// simulation pipeline itself, mirroring <see cref="PlanetDemo"/>'s role as a thin wiring
    /// script for manual testing.
    /// </summary>
    public class MouseTerrainEditor : MonoBehaviour
    {
        [SerializeField] private ChunkTerrainRenderer chunkTerrainRenderer;
        [SerializeField] private Transform planetCenter;
        [SerializeField] private float brushRadius = 2f;
        [SerializeField] private Camera targetCamera;

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
            {
                return;
            }

            Vector2 worldPosition = camera.ScreenToWorldPoint(Input.mousePosition);
            Vector2 planetCenterPosition = planetCenter != null ? (Vector2)planetCenter.position : Vector2.zero;
            Vector2 localPosition = PlanetCoordinates.WorldToLocal(worldPosition, planetCenterPosition);

            var brush = new TerrainBrush(mode, brushRadius);
            chunkTerrainRenderer.ApplyBrush(brush, localPosition);
        }
    }
}
