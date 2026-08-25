namespace SDFTerrain.Materials
{
    /// <summary>
    /// Compact numeric identifier for a material type. Replaces string-based material lookups
    /// at runtime for performance and type safety. Assigned sequentially by the
    /// <see cref="MaterialDatabase"/> as materials are registered; Air is always 0, Unknown is
    /// always the final reserved ID.
    /// </summary>
    public readonly struct MaterialId : System.IEquatable<MaterialId>, System.IComparable<MaterialId>
    {
        /// <summary>Internal value — assigned by the database.</summary>
        private readonly int _value;

        internal MaterialId(int value)
        {
            _value = value;
        }

        /// <summary>Access the internal integer value (for arrays, hashing, serialization).</summary>
        public int Value => _value;

        /// <summary>Material ID for air (always outside terrain).</summary>
        public static readonly MaterialId Air = new MaterialId(0);

        /// <summary>Material ID used when no material can be determined (never air).</summary>
        public static readonly MaterialId Unknown = new MaterialId(-1);

        public bool IsValid => _value >= 0;

        public bool Equals(MaterialId other) => _value == other._value;

        public override bool Equals(object obj) => obj is MaterialId other && Equals(other);

        public override int GetHashCode() => _value;

        public int CompareTo(MaterialId other) => _value.CompareTo(other._value);

        public static bool operator ==(MaterialId left, MaterialId right) => left.Equals(right);

        public static bool operator !=(MaterialId left, MaterialId right) => !left.Equals(right);

        public override string ToString()
        {
            if (_value < 0) return "Unknown";
            if (_value == 0) return "Air";
            var database = MaterialDatabase.Instance;
            return database.TryGetName(this) ? database.GetName(this) : $"Material_{_value}";
        }
    }
}
