using UnityEngine;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Lightweight <c>OnGUI</c> overlay that renders runtime stats in the Game view.
    /// Shows frame rate and the number of persisted terrain edits.
    ///
    /// Attach to the planet GameObject alongside <see cref="ChunkTerrainRenderer"/>.
    /// Call <see cref="Initialize"/> at startup, or leave null to auto-discover the field
    /// from a sibling renderer component.
    /// </summary>
    public class GameDebugOverlay : MonoBehaviour
    {
        [Tooltip("Draw the debug overlay in the Game view")]
        [SerializeField] private bool enabled;

        private TerrainField _terrainField;

        // FPS accumulator
        private float _frameTimer;
        private int   _frameCount;
        private float _fps;

        /// <summary>
        /// Prepares this overlay to read the edit count from the given <see cref="TerrainField"/>.
        /// If not called, the component auto-discovers the field from a sibling
        /// <see cref="ChunkTerrainRenderer"/> at draw time.
        /// </summary>
        public void Initialize(TerrainField terrainField)
        {
            _terrainField = terrainField;
        }

        private void Update()
        {
            if (!enabled)
                return;

            _frameCount++;
            _frameTimer += Time.deltaTime;

            // Recompute FPS every half-second for a smoother readout.
            if (_frameTimer >= 0.5f)
            {
                _fps = _frameCount / _frameTimer;
                _frameCount = 0;
                _frameTimer = 0f;
            }

            // Keyboard shortcut: L to consolidate uniform regions.
            if (Input.GetKeyDown(KeyCode.L))
            {
                TerrainField field = _terrainField;
                if (field == null)
                {
                    var chunkRenderer = GetComponentInSibling<ChunkTerrainRenderer>();
                    field = chunkRenderer?.Field;
                }

                if (field != null)
                {
                    ChunkTerrainRenderer renderer = GetComponentInSibling<ChunkTerrainRenderer>();
                    if (renderer != null)
                    {
                        int beforeCount = field.Edits.Count;
                        try
                        {
                            field.ConsolidateUniformRegions(renderer.CellSize);
                            int afterCount = field.Edits.Count;
                            Debug.LogFormat("[GameDebugOverlay] ConsolidateUniformRegions: {0} -> {1} edits", beforeCount, afterCount);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogErrorFormat("[GameDebugOverlay] ConsolidateUniformRegions failed: {0}", ex.Message);
                        }
                    }
                }
            }
        }

        private void OnGUI()
        {
            if (!enabled)
                return;

            // Auto-discover if not explicitly initialized.
            TerrainField field = _terrainField;
            if (field == null)
            {
                var chunkRenderer = GetComponentInSibling<ChunkTerrainRenderer>();
                field = chunkRenderer?.Field;
            }

            int editCount = field?.Edits.Count ?? -1;

            GUI.Label(new Rect(8, 8, 300, 24), $"FPS: {_fps:F0}");
            GUI.Label(new Rect(8, 32, 300, 24), $"Edits: {editCount}");
            GUI.Label(new Rect(8, 56, 300, 24), $"[L] Consolidate uniform regions");
        }

        /// <summary>
        /// Finds a component of type T on another GameObject that shares this transform's parent
        /// or is this GameObject itself.
        /// </summary>
        T GetComponentInSibling<T>() where T : UnityEngine.Component
        {
            var comp = GetComponent<T>();
            if (comp != null)
                return comp;

            var parent = transform.parent;
            if (parent == null)
                return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == transform)
                    continue;
                comp = child.GetComponent<T>();
                if (comp != null)
                    return comp;
            }
            return null;
        }
    }
}
