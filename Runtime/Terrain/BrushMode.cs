namespace SDFTerrain.Terrain
{
    /// <summary>Gameplay-facing intent for a terrain brush stroke.</summary>
    public enum BrushMode
    {
        Add,
        Remove,
        Smooth,
        /// <summary>
        /// Finds the nearest terrain surface within the brush radius and carves a circular
        /// edit at that location, as if a lightning bolt struck the closest point.
        /// </summary>
        Electric,
    }
}
