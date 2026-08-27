namespace SDFTerrain.Terrain
{
    /// <summary>
    /// A single rectangular region of a planet's terrain. Owns only indexing/dirty state here; the
    /// mesh, collider, and density samples it will eventually own are added in later tasks
    /// (Meshing, Collision). Never regenerated wholesale — only marked dirty and rebuilt.
    /// </summary>
    public class TerrainChunk
    {
        public int Index { get; }
        public long Key { get; }
        public int Col { get; }
        public int Row { get; }
        public float MinX { get; }
        public float MaxX { get; }
        public float MinY { get; }
        public float MaxY { get; }
        public bool IsDirty { get; private set; }

        /// <summary>
        /// True when all lattice samples within this chunk have the same sign (all solid or all air).
        /// Set by the sampler after sampling.
        /// </summary>
        public bool IsUniform { get; set; }

        /// <summary>
        /// True when this chunk is uniformly solid (all samples negative). Only meaningful when
        /// <see cref="IsUniform"/> is true.
        /// </summary>
        public bool IsSolid { get; set; }

        public TerrainChunk(int index, int col, int row, float minX, float maxX, float minY, float maxY)
        {
            Index = index;
            Key = (((long)col) << 32) | ((long)row & 0xffffffffL);
            Col = col;
            Row = row;
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
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
