using SDFTerrain.Terrain;

namespace SDFTerrain.UI
{
    /// <summary>
    /// Tracks cumulative terrain area changes and exposes them to the UI.
    /// Pair with a <see cref="BrushToolbar"/> in the same scene.
    ///
    /// <para>AreaBuilt and AreaMined are running totals that persist across the session.
    /// NetArea reflects the cumulative player-driven change (AreaBuilt - AreaMined).
    /// TotalArea is computed on-demand from the current mesh state and includes
    /// both initial terrain and any player modifications.</para>
    /// </summary>
    public class TerrainStats : UnityEngine.MonoBehaviour
    {
        /// <summary>Cumulative solid area added by player brush strokes (always non-negative).</summary>
        public float AreaBuilt { get; private set; }

        /// <summary>Cumulative solid area removed by player brush strokes (always non-negative).</summary>
        public float AreaMined { get; private set; }

        /// <summary>
        /// Cumulative net change from player activity (AreaBuilt - AreaMined).
        /// Positive means more terrain has been built than removed.
        /// Negative means more terrain has been mined than built.
        /// </summary>
        public float NetArea => AreaBuilt - AreaMined;

        /// <summary>
        /// Total solid area currently in the world, derived from all chunk meshes.
        /// Call after the renderer has rebuilt chunks to get an accurate value.
        /// </summary>
        public float TotalArea
        {
            get
            {
                if (_renderer == null)
                    return 0f;
                return _renderer.GetTotalSolidArea();
            }
        }

        private ChunkTerrainRenderer _renderer;

        /// <summary>
        /// Links this tracker to a renderer so <see cref="TotalArea"/> can derive
        /// the current world area from chunk meshes. Call once after the renderer
        /// is initialized.
        /// </summary>
        public void Initialize(ChunkTerrainRenderer renderer)
        {
            _renderer = renderer;
        }

        /// <summary>
        /// Records the area delta from a single brush stroke.
        /// Call from <see cref="BrushToolbar"/> after each <c>ApplyBrush</c>.
        /// </summary>
        public void RecordDelta(BrushAreaDelta delta)
        {
            if (delta.IsAddition)
            {
                AreaBuilt += delta.AreaAdded;
            }
            else if (delta.IsRemoval)
            {
                AreaMined += delta.AreaRemoved;
            }
        }

        /// <summary>Resets all accumulated counters to zero.</summary>
        public void Reset()
        {
            AreaBuilt = 0f;
            AreaMined = 0f;
        }
    }
}
