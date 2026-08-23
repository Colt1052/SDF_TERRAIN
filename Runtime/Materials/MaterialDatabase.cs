using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDFTerrain.Materials
{
    /// <summary>
    /// Static registry that loads all MaterialDefinition assets from Resources/Materials/ and
    /// provides O(1) lookup by material ID. Thread-safe after Initialize() has completed.
    /// </summary>
    public class MaterialDatabase
    {
        private static MaterialDatabase _instance;

        private readonly Dictionary<string, MaterialDefinition> _materials = new Dictionary<string, MaterialDefinition>();

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

            var definitions = Resources.LoadAll<MaterialDefinition>("Materials");

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

                _materials[id] = def;
            }

            if (_materials.Count > 0)
            {
                _instance = this;
            }
        }

        /// <summary>
        /// Look up a material by its unique ID. Throws if not found.
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
        /// Check whether a material with the given ID is registered.
        /// </summary>
        public bool HasMaterial(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            return _materials.ContainsKey(id);
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

            _materials[id] = definition;

            if (_materials.Count > 0)
            {
                _instance = this;
            }
        }

        /// <summary>
        /// Clear the registry. Primarily useful for EditMode tests that need a fresh state.
        /// </summary>
        public void Clear()
        {
            _materials.Clear();
            _instance = null;
        }
    }
}
