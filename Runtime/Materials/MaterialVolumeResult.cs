using System.Collections.Generic;

namespace SDFTerrain.Materials
{
    /// <summary>
    /// Result of a material-aware excavation: maps each <see cref="MaterialId"/> to the volume
    /// (area in 2D) of that material that was removed. Independent of inventory — represents
    /// the physical outcome of removing terrain.
    /// </summary>
    public class MaterialVolumeResult
    {
        private readonly Dictionary<int, float> _volumes = new Dictionary<int, float>();

        /// <summary>Total volume removed across all materials.</summary>
        public float TotalVolume { get; private set; }

        /// <summary>All materials that had volume removed.</summary>
        public IReadOnlyCollection<MaterialId> Materials => _materialIds;
        private readonly List<MaterialId> _materialIds = new List<MaterialId>();
        private readonly List<float> _volumeValues = new List<float>();

        /// <summary>
        /// Adds volume for a material. If the material is already present, accumulates the volume.
        /// </summary>
        public void Add(MaterialId materialId, float volume)
        {
            if (volume <= 0f || !materialId.IsValid)
                return;

            int key = materialId.Value;
            _volumes[key] = _volumes.TryGetValue(key, out float existing) ? existing + volume : volume;
            TotalVolume += volume;
        }

        /// <summary>
        /// Returns the volume removed for a specific material. Returns 0 if not present.
        /// </summary>
        public float GetVolume(MaterialId materialId)
        {
            return _volumes.TryGetValue(materialId.Value, out float v) ? v : 0f;
        }

        /// <summary>
        /// Returns true if any volume of the specified material was removed.
        /// </summary>
        public bool HasMaterial(MaterialId materialId)
        {
            return _volumes.ContainsKey(materialId.Value);
        }

        /// <summary>
        /// Iterates over each material-volume pair.
        /// </summary>
        public void ForEach(System.Action<MaterialId, float> action)
        {
            if (action == null)
                return;

            foreach (var kvp in _volumes)
            {
                action(new MaterialId(kvp.Key), kvp.Value);
            }
        }

        /// <summary>Clears all data.</summary>
        public void Clear()
        {
            _volumes.Clear();
            _materialIds.Clear();
            _volumeValues.Clear();
            TotalVolume = 0f;
        }

        public override string ToString()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append($"Total: {TotalVolume:F2} m³\n");
            ForEach((id, vol) => sb.Append($"  {id}: {vol:F2} m³\n"));
            return sb.ToString();
        }
    }
}
