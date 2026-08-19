namespace SDFTerrain.Terrain
{
    /// <summary>
    /// A single angular slice of a planet's terrain. Owns only indexing/dirty state here; the
    /// mesh, collider, and density samples it will eventually own are added in later tasks
    /// (Meshing, Collision). Never regenerated wholesale — only marked dirty and rebuilt.
    /// </summary>
    public class TerrainChunk
    {
        public int Index { get; }
        public float StartAngle { get; }
        public float EndAngle { get; }
        public bool IsDirty { get; private set; }

        public TerrainChunk(int index, float startAngle, float endAngle)
        {
            Index = index;
            StartAngle = startAngle;
            EndAngle = endAngle;
            IsDirty = true;
        }

        public void MarkDirty()
        {
            IsDirty = true;
        }

        public void ClearDirty()
        {
            IsDirty = false;
        }
    }
}
