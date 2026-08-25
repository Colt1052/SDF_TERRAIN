namespace SDFTerrain.Materials
{
    /// <summary>
    /// Result of querying the material at a specific world position. Contains the authoritative
    /// <see cref="MaterialId"/> and optional concentration information when the location sits at
    /// a material boundary (e.g., ore veins embedded in stone).
    /// </summary>
    public readonly struct MaterialSample : System.IEquatable<MaterialSample>
    {
        /// <summary>The primary material at this location.</summary>
        public readonly MaterialId MaterialId;

        /// <summary>
        /// Concentration of the primary material (0 to 1). Values below 1.0 indicate a
        /// mixed boundary region (e.g., ore veins within a host rock). A value of 1.0
        /// means the material is pure/uniform at this location.
        /// </summary>
        public readonly float Concentration;

        /// <summary>Whether this location is inside solid terrain (SDF &lt; 0).</summary>
        public readonly bool IsSolid;

        public MaterialSample(MaterialId materialId, float concentration, bool isSolid)
        {
            if (concentration < 0f)
                concentration = 0f;
            if (concentration > 1f)
                concentration = 1f;

            MaterialId = materialId;
            Concentration = concentration;
            IsSolid = isSolid;
        }

        public bool Equals(MaterialSample other)
            => MaterialId == other.MaterialId && Concentration == other.Concentration && IsSolid == other.IsSolid;

        public override bool Equals(object obj) => obj is MaterialSample other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + MaterialId.GetHashCode();
                hash = hash * 31 + Concentration.GetHashCode();
                hash = hash * 31 + IsSolid.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => $"{(IsSolid ? "Solid" : "Air")} {MaterialId} ({Concentration:P0})";

        public static bool operator ==(MaterialSample left, MaterialSample right) => left.Equals(right);
        public static bool operator !=(MaterialSample left, MaterialSample right) => !left.Equals(right);
    }
}
