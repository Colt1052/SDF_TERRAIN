using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SDFTerrain.Materials;
using SDFTerrain.Terrain;

namespace SDFTerrain.Resources
{
    /// <summary>
    /// Wires <see cref="WorldSaveData"/> into actual file I/O.
    ///
    /// Features:
    /// - Slot-based saves (up to 5 slots) stored in Application.persistentDataPath.
    /// - PlayerPrefs-backed slot metadata (seed, edit count, timestamp) for the save menu.
    /// - Auto-save on scene unload / application quit.
    /// - Load-on-start when <paramref name="autoLoadSlot"/> >= 0.
    /// - Deterministic reproducibility: given the same save file, terrain and materials
    ///   are identical after a Unity domain reload.
    ///
    /// Typical usage: Attach to a game object alongside <see cref="ChunkTerrainRenderer"/>.
    /// The component lazily wires to the renderer's field, a <see cref="MaterialLayer"/>,
    /// and a <see cref="TerrainExcavationSystem"/> on first save or load call.
    /// </summary>
    public class WorldPersistence : MonoBehaviour
    {
        /// <summary>Maximum number of save slots.</summary>
        public const int MaxSaveSlots = 5;

        /// <summary>Directory name under persistentDataPath where save files live.</summary>
        public const string SaveDirectoryName = "SDFPlanetSaves";

        /// <summary>Auto-load slot index (-1 = don't auto-load).</summary>
        [Tooltip("Which save slot to load on start. -1 means no auto-load.")]
        [SerializeField] private int autoLoadSlot = -1;

        /// <summary>Enable automatic save on application quit.</summary>
        [Tooltip("Automatically save to slot 0 when the application quits.")]
        [SerializeField] private bool autoSaveOnQuit = true;

        /// <summary>Slot used for auto-save (default: 0).</summary>
        [Tooltip("Which slot the auto-save writes to.")]
        [SerializeField] private int autoSaveSlot = 0;

        // Lazy-wired references.
        private ChunkTerrainRenderer _renderer;
        private MaterialLayer _materialLayer;
        private TerrainExcavationSystem _excavationSystem;
        private Planet.Planet _planet;

        /// <summary>Called when a save completes successfully.</summary>
        public event System.Action<int> Saved;

        /// <summary>Called when a load completes successfully.</summary>
        public event System.Action<int> Loaded;

        /// <summary>Raised after a save or load to notify debug views to re-sample.</summary>
        public event System.Action WorldChanged;

        private string SaveDirectory
        {
            get
            {
                var path = Path.Combine(Application.persistentDataPath, SaveDirectoryName);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        private void Start()
        {
            // Discover components on the same GameObject.
            _renderer = GetComponent<ChunkTerrainRenderer>();
            _planet = GetComponent<Planet.Planet>();

            // Listen for application quit to trigger auto-save.
            Application.quitting += OnApplicationQuitting;

            // Auto-load if configured.
            if (autoLoadSlot >= 0 && autoLoadSlot < MaxSaveSlots)
            {
                // Delay until other systems have initialized (e.g., PlanetDemo.Start).
                StartCoroutine(DelayedAutoLoad());
            }
        }

        private System.Collections.IEnumerator DelayedAutoLoad()
        {
            yield return null; // Wait one frame
            if (Load(autoLoadSlot))
            {
                Debug.Log($"[WorldPersistence] Auto-loaded slot {autoLoadSlot}.");
            }
            else
            {
                Debug.LogWarning($"[WorldPersistence] Auto-load slot {autoLoadSlot} was empty. Starting fresh.");
            }
        }

        private void OnApplicationQuitting()
        {
            if (autoSaveOnQuit)
            {
                Save(autoSaveSlot, $"Auto-save {autoSaveSlot}");
            }
        }

        private void OnDestroy()
        {
            Application.quitting -= OnApplicationQuitting;
        }

        /// <summary>
        /// Explicitly configure the material layer and excavation system.
        /// Call this if the systems live on a different GameObject.
        /// </summary>
        public void Configure(MaterialLayer materialLayer, TerrainExcavationSystem excavationSystem)
        {
            _materialLayer = materialLayer;
            _excavationSystem = excavationSystem;
        }

        /// <summary>
        /// Saves the current world state to the given slot.
        /// Creates a new slot if it doesn't exist. Overwrites if it does.
        /// </summary>
        /// <param name="slot">Slot index (0 to MaxSaveSlots-1).</param>
        /// <param name="displayName">Human-readable name for the save entry. Null = auto-generated.</param>
        /// <returns>True if the save succeeded.</returns>
        public bool Save(int slot, string displayName = null)
        {
            if (slot < 0 || slot >= MaxSaveSlots)
            {
                Debug.LogError($"[WorldPersistence] Invalid slot {slot}. Must be 0-{MaxSaveSlots - 1}.");
                return false;
            }

            // Gather systems from renderer.
            TerrainField field = _renderer?.Field;
            if (field == null)
            {
                Debug.LogWarning("[WorldPersistence] Cannot save: no TerrainField found. Ensure ChunkTerrainRenderer is initialized.");
                return false;
            }

            // Material layer is optional — save works with just SDF edits.
            MaterialLayer layer = _materialLayer;
            Inventory inventory = _excavationSystem?.GetInventory();

            // If we have an excavation system, pull its material layer and inventory.
            if (_excavationSystem != null && layer == null)
            {
                layer = _excavationSystem.GetMaterialLayer();
            }

            // Fallback: create minimal objects if nothing is wired.
            if (layer == null)
            {
                Debug.LogWarning("[WorldPersistence] No MaterialLayer configured. Saving SDF edits only.");
                layer = new MaterialLayer(
                    GeologicalProfile.EarthLike(_planet?.Seed ?? 0, 0.3f),
                    MaterialDatabase.Instance);
            }

            if (inventory == null)
            {
                inventory = new Inventory();
            }

            // Capture state.
            int seed = _planet?.Seed ?? 0;
            var saveData = WorldSaveData.Capture(field, layer, inventory, seed);

            // Write to disk.
            string filePath = SaveFilePath(slot);
            string json = saveData.ToJson();
            File.WriteAllText(filePath, json);

            // Update slot metadata in PlayerPrefs.
            SaveSlotMetadata(slot, displayName ?? $"Save {slot}", seed);

            int terrainEditCount = saveData.TerrainEdits.Length;
            int materialEditCount = saveData.MaterialEdits.Length;
            Debug.Log($"[WorldPersistence] Saved slot {slot}: {terrainEditCount} terrain edits, {materialEditCount} material edits, {inventory.SlotCount} inventory slots.");

            Saved?.Invoke(slot);
            return true;
        }

        /// <summary>
        /// Loads world state from the given slot.
        /// This replaces the current field, material layer, and inventory.
        /// </summary>
        /// <param name="slot">Slot index (0 to MaxSaveSlots-1).</param>
        /// <returns>True if the load succeeded. False if the slot is empty.</returns>
        public bool Load(int slot)
        {
            if (slot < 0 || slot >= MaxSaveSlots)
            {
                Debug.LogError($"[WorldPersistence] Invalid slot {slot}. Must be 0-{MaxSaveSlots - 1}.");
                return false;
            }

            string filePath = SaveFilePath(slot);
            if (!File.Exists(filePath))
            {
                Debug.Log($"[WorldPersistence] Slot {slot} is empty (no file at {filePath}).");
                return false;
            }

            string json = File.ReadAllText(filePath);
            var saveData = WorldSaveData.FromJson(json);

            // Rebuild the terrain field.
            float baseRadius = saveData.BaseRadius > 0f ? saveData.BaseRadius : 30f;
            TerrainField field = new TerrainField(baseRadius, saveData.Seed, TerrainNoiseSettings.None);

            // Rebuild the material layer.
            var database = MaterialDatabase.Instance;
            var profile = GeologicalProfile.EarthLike(saveData.Seed, 0.3f);
            MaterialLayer layer = new MaterialLayer(profile, database);

            // Rebuild inventory.
            Inventory inventory = new Inventory();

            // Apply saved state.
            saveData.Apply(field, layer, inventory);

            // Wire back to renderer if available.
            if (_renderer != null)
            {
                // Reinitialize the renderer with the fresh field and grid.
                ChunkGrid chunkGrid = new ChunkGrid(baseRadius, _renderer.CellSize);
                _renderer.Initialize(field, chunkGrid, baseRadius);

                // Reapply material layer to renderer.
                _renderer.SetMaterialLayer(layer);

                // Rebuild all chunks.
                _renderer.RebuildDirtyChunks();
            }

            // Update excavation system if available.
            if (_excavationSystem != null)
            {
                _excavationSystem.Rewire(field, layer, inventory);
            }

            // Store references for subsequent saves.
            _materialLayer = layer;

            Debug.Log($"[WorldPersistence] Loaded slot {slot}: seed={saveData.Seed}, {saveData.TerrainEdits.Length} terrain edits, {saveData.MaterialEdits.Length} material edits.");

            Loaded?.Invoke(slot);
            WorldChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Returns metadata about all save slots for UI display.
        /// </summary>
        public SlotMetadata[] GetAllSlotMetadata()
        {
            var slots = new SlotMetadata[MaxSaveSlots];
            for (int i = 0; i < MaxSaveSlots; i++)
            {
                slots[i] = GetSlotMetadata(i);
            }
            return slots;
        }

        /// <summary>
        /// Returns metadata for a single save slot without loading the file.
        /// </summary>
        public SlotMetadata GetSlotMetadata(int slot)
        {
            if (slot < 0 || slot >= MaxSaveSlots)
                return SlotMetadata.Empty;

            string prefix = SlotKey(slot);
            bool exists = PlayerPrefs.HasKey($"{prefix}.exists");

            if (!exists)
                return SlotMetadata.Empty;

            return new SlotMetadata(
                slot: slot,
                displayName: PlayerPrefs.GetString($"{prefix}.name", $"Save {slot}"),
                seed: PlayerPrefs.GetInt($"{prefix}.seed", 0),
                timestamp: PlayerPrefs.GetFloat($"{prefix}.timestamp", 0f),
                hasData: true
            );
        }

        /// <summary>
        /// Deletes the save data for the given slot.
        /// </summary>
        /// <param name="slot">Slot index (0 to MaxSaveSlots-1).</param>
        /// <returns>True if the slot was deleted, false if it was already empty.</returns>
        public bool DeleteSlot(int slot)
        {
            if (slot < 0 || slot >= MaxSaveSlots)
            {
                Debug.LogError($"[WorldPersistence] Invalid slot {slot}.");
                return false;
            }

            string filePath = SaveFilePath(slot);
            string prefix = SlotKey(slot);

            bool hadData = File.Exists(filePath) || PlayerPrefs.HasKey($"{prefix}.exists");

            if (File.Exists(filePath))
                File.Delete(filePath);

            PlayerPrefs.DeleteKey($"{prefix}.exists");
            PlayerPrefs.DeleteKey($"{prefix}.name");
            PlayerPrefs.DeleteKey($"{prefix}.seed");
            PlayerPrefs.DeleteKey($"{prefix}.timestamp");
            PlayerPrefs.Save();

            if (hadData)
                Debug.Log($"[WorldPersistence] Deleted slot {slot}.");

            return hadData;
        }

        private static string SaveFilePath(int slot)
        {
            return Path.Combine(Application.persistentDataPath, SaveDirectoryName, $"slot_{slot}.json");
        }

        private static string SlotKey(int slot)
        {
            return $"sdf_save_{slot}";
        }

        private static void SaveSlotMetadata(int slot, string displayName, int seed)
        {
            string prefix = SlotKey(slot);
            PlayerPrefs.SetInt($"{prefix}.exists", 1);
            PlayerPrefs.SetString($"{prefix}.name", displayName);
            PlayerPrefs.SetInt($"{prefix}.seed", seed);
            PlayerPrefs.SetFloat($"{prefix}.timestamp", Time.realtimeSinceStartup);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Lightweight metadata for a save slot. Does not contain the full save data.
        /// </summary>
        public readonly struct SlotMetadata
        {
            public readonly int Slot;
            public readonly string DisplayName;
            public readonly int Seed;
            public readonly float Timestamp;
            public readonly bool HasData;

            public SlotMetadata(int slot, string displayName, int seed, float timestamp, bool hasData)
            {
                Slot = slot;
                DisplayName = displayName;
                Seed = seed;
                Timestamp = timestamp;
                HasData = hasData;
            }

            public static readonly SlotMetadata Empty = new SlotMetadata(-1, "Empty", 0, 0f, false);

            public override string ToString()
            {
                return HasData ? $"{DisplayName} (Slot {Slot}, Seed {Seed})" : "Empty";
            }
        }
    }
}
