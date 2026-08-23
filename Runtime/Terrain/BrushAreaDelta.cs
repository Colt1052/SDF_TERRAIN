namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Result of an area-aware brush operation. Immutable value type — zero allocations when
    /// returned from <see cref="ChunkTerrainRenderer.ApplyBrush"/>.
    /// </summary>
    public readonly struct BrushAreaDelta
    {
        /// <summary>
        /// Amount of solid area carved away by this brush stroke (always non-negative).
        /// Zero if the brush added terrain or was a no-op. Use as resource reward for digging.
        /// </summary>
        public readonly float AreaRemoved;

        /// <summary>
        /// Amount of solid area built by this brush stroke (always non-negative).
        /// Zero if the brush removed terrain or was a no-op. Use as resource cost for building.
        /// </summary>
        public readonly float AreaAdded;

        /// <summary>
        /// True if the brush actually modified the terrain field. False if it was a no-op
        /// (e.g., Electric mode found no surface within range).
        /// </summary>
        public readonly bool WasApplied;

        public BrushAreaDelta(float areaRemoved, float areaAdded, bool wasApplied)
        {
            AreaRemoved = areaRemoved;
            AreaAdded = areaAdded;
            WasApplied = wasApplied;
        }

        /// <summary>True if the brush removed terrain (net solid area decreased).</summary>
        public bool IsRemoval => AreaRemoved > 0f;

        /// <summary>True if the brush added terrain (net solid area increased).</summary>
        public bool IsAddition => AreaAdded > 0f;
    }
}
