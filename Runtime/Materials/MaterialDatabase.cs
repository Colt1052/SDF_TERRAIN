using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SDFTerrain.Materials
{
    /// <summary>
    /// Static registry that loads all MaterialDefinition assets from Resources/Materials/ and
    /// provides O(1) lookup by material ID (string or numeric). Thread-safe after Initialize() has completed.
    ///
    /// Materials are assigned compact numeric <see cref="MaterialId"/> values at registration time.
    /// Air is always MaterialId 0. Subsequent materials receive incrementing IDs. The string-based
    /// API remains for backward compatibility and editor/serialization use.
    /// </summary>
    public class MaterialDatabase
    {
        private static MaterialDatabase _instance;

        private readonly Dictionary<string, MaterialDefinition> _materials = new Dictionary<string, MaterialDefinition>();
        private readonly Dictionary<string, MaterialId> _idToMaterialId = new Dictionary<string, MaterialId>();
        private readonly List<MaterialDefinition> _byNumericId = new List<MaterialDefinition>();
        private int _nextNumericId = 1;

        /// <summary>
        /// Shared singleton instance. Lazy-initialized; tests that need isolation can construct
        /// a new MaterialDatabase() directly.
        /// </summary>
        public static MaterialDatabase Instance => _instance ?? new MaterialDatabase();

        /// <summary>
        /// All registered materials in an enumerable collection. Returns an empty collection if
        /// the database has not yet been initialized.
        /// </summary>
        public IReadOnlyCollection<MaterialDefinition> AllMaterials => _materials.Values;

        /// <summary>
        /// Returns true if the database has been populated from Resources.
        /// </summary>
        public bool IsInitialized => _materials.Count > 0;

        /// <summary>
        /// Load all MaterialDefinition assets from Resources/Materials/. Safe to call multiple
        /// times — subsequent calls are no-ops unless <paramref name="forceReload"/> is true.
        /// </summary>
        public void Initialize(bool forceReload = false)
        {
            if (_materials.Count > 0 && !forceReload)
            {
                return;
            }

            _materials.Clear();
            _idToMaterialId.Clear();
            _byNumericId.Clear();
            _nextNumericId = 1;

            // Register Air first (MaterialId 0).
            RegisterAirMaterial();

            var definitions = global::UnityEngine.Resources.LoadAll<MaterialDefinition>("Materials");

            for (int i = 0; i < definitions.Length; i++)
            {
                MaterialDefinition def = definitions[i];

                if (def == null)
                {
                    continue;
                }

                string id = def.Id;

                if (_materials.ContainsKey(id))
                {
                    Debug.LogWarning($"MaterialDatabase: Duplicate material ID \"{id}\". Skipping {def.name}.");
                    continue;
                }

                RegisterMaterial(def);
            }

            if (_materials.Count > 0)
            {
                _instance = this;
            }
        }

        private void RegisterAirMaterial()
        {
            // Air is MaterialId 0 — always present, no ScriptableObject needed.
            // The MaterialDefinition for Air can be loaded from Resources if available,
            // otherwise callers rely on MaterialId.Air directly.
        }

        private void RegisterMaterial(MaterialDefinition def)
        {
            string id = def.Id;
            MaterialId matId = new MaterialId(_nextNumericId++);

            _materials[id] = def;
            _idToMaterialId[id] = matId;

            // Ensure _byNumericId is large enough.
            while (_byNumericId.Count <= matId.Value)
            {
                _byNumericId.Add(null);
            }
            _byNumericId[matId.Value] = def;
        }

        /// <summary>
        /// Look up a material by its unique string ID. Throws if not found.
        /// </summary>
        public MaterialDefinition GetMaterial(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException(nameof(id), "Material ID must not be null or empty.");
            }

            if (!_materials.TryGetValue(id, out MaterialDefinition def))
            {
                throw new KeyNotFoundException($"Material \"{id}\" not found in the database. Call Initialize() first.");
            }

            return def;
        }

        /// <summary>
        /// Look up a material by its numeric <see cref="MaterialId"/>.
        /// </summary>
        public MaterialDefinition GetMaterial(MaterialId id)
        {
            if (!id.IsValid)
            {
                return null;
            }

            if (id.Value < _byNumericId.Count)
            {
                return _byNumericId[id.Value];
            }

            return null;
        }

        /// <summary>
        /// Convert a string material ID to a numeric <see cref="MaterialId"/>.
        /// Returns <see cref="MaterialId.Unknown"/> if not found.
        /// </summary>
        public MaterialId GetMaterialId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return MaterialId.Unknown;
            }

            if (_idToMaterialId.TryGetValue(id, out MaterialId matId))
            {
                return matId;
            }

            // "air" is always MaterialId 0.
            if (id == "air" || id == "Air")
            {
                return MaterialId.Air;
            }

            return MaterialId.Unknown;
        }

        /// <summary>
        /// Check whether a material with the given string ID is registered.
        /// </summary>
        public bool HasMaterial(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            return _materials.ContainsKey(id) || id == "air" || id == "Air";
        }

        /// <summary>
        /// Check whether a numeric <see cref="MaterialId"/> is registered.
        /// </summary>
        public bool HasMaterial(MaterialId id)
        {
            if (!id.IsValid)
            {
                return false;
            }

            if (id.Value < _byNumericId.Count)
            {
                return _byNumericId[id.Value] != null;
            }

            return false;
        }

        /// <summary>
        /// Register a material definition directly without loading from Resources.
        /// Primarily useful for EditMode tests that create ScriptableObjects programmatically.
        /// </summary>
        public void AddMaterial(MaterialDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition), "Material definition must not be null.");
            }

            string id = definition.Id;

            if (_materials.ContainsKey(id))
            {
                Debug.LogWarning($"MaterialDatabase: Material ID \"{id}\" already registered. Overwriting.");
            }

            RegisterMaterial(definition);

            if (_materials.Count > 0)
            {
                _instance = this;
            }
        }

        /// <summary>
        /// Returns the human-readable name for the given <see cref="MaterialId"/>.
        /// Used by <see cref="MaterialId.ToString"/>.
        /// </summary>
        public string GetName(MaterialId id)
        {
            if (!id.IsValid || id.Value >= _byNumericId.Count)
            {
                return $"Material_{id.Value}";
            }

            MaterialDefinition def = _byNumericId[id.Value];
            return def != null ? def.DisplayName : $"Material_{id.Value}";
        }

        /// <summary>
        /// Returns true if there is a name for the given <see cref="MaterialId"/>.
        /// Used by <see cref="MaterialId.ToString"/> to avoid calling when invalid.
        /// </summary>
        public bool TryGetName(MaterialId id)
        {
            if (!id.IsValid || id.Value >= _byNumericId.Count)
            {
                return false;
            }

            return _byNumericId[id.Value] != null;
        }

        /// <summary>
        /// Clear the registry. Primarily useful for EditMode tests that need a fresh state.
        /// </summary>
        public void Clear()
        {
            _materials.Clear();
            _idToMaterialId.Clear();
            _byNumericId.Clear();
            _nextNumericId = 1;
            _instance = null;
        }
    }
}
