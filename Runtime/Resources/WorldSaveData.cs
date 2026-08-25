using System.Collections.Generic;
using UnityEngine;
using SDFTerrain.Materials;
using SDFTerrain.Terrain;

namespace SDFTerrain.Resources
{
    /// <summary>
    /// Serializable representation of a <see cref="TerrainEdit"/> for persistence.
    /// Matches the <see cref="TerrainEdit"/> struct fields so JsonUtility can round-trip.
    /// </summary>
    [System.Serializable]
    public class SerializableTerrainEdit
    {
        public float LocalPositionX;
        public float LocalPositionY;
        public float Radius;
        public bool IsAdditive;

        public TerrainEdit ToTerrainEdit()
        {
            return new TerrainEdit(
                new Vector2(LocalPositionX, LocalPositionY),
                Radius,
                IsAdditive);
        }
    }

    /// <summary>
    /// Serializable representation of a <see cref="MaterialEdit"/> for persistence.
    /// Stores the numeric MaterialId value since MaterialId is a struct not directly
    /// serializable by Unity's JsonUtility.
    /// </summary>
    [System.Serializable]
    public class SerializableMaterialEdit
    {
        public float LocalPositionX;
        public float LocalPositionY;
        public float Radius;
        public int MaterialIdValue;
        public int Order;

        public MaterialEdit ToMaterialEdit()
        {
            return new MaterialEdit(
                new Vector2(LocalPositionX, LocalPositionY),
                Radius,
                new MaterialId(MaterialIdValue),
                Order);
        }
    }

    /// <summary>
    /// Serializable representation of an inventory slot.
    /// </summary>
    [System.Serializable]
    public class SerializableInventorySlot
    {
        public string ResourceId;
        public int Quantity;
        public int MaxStack;
    }

    /// <summary>
    /// Root serialization container for world state. Holds SDF edits, material edits,
    /// and inventory so that a saved world produces identical terrain and material results
    /// after reload.
    /// </summary>
    [System.Serializable]
    public class WorldSaveData
    {
        /// <summary>Planet seed used for procedural generation.</summary>
        public int Seed;

        /// <summary>Base radius of the planet sphere.</summary>
        public float BaseRadius;

        /// <summary>SDF edits that modify terrain geometry.</summary>
        public SerializableTerrainEdit[] TerrainEdits;

        /// <summary>Material overrides placed by the player.</summary>
        public SerializableMaterialEdit[] MaterialEdits;

        /// <summary>Current inventory state.</summary>
        public SerializableInventorySlot[] InventorySlots;

        /// <summary>
        /// Creates a WorldSaveData snapshot from the current state of the world systems.
        /// </summary>
        public static WorldSaveData Capture(
            TerrainField field,
            MaterialLayer layer,
            Inventory inventory,
            int seed)
        {
            var data = new WorldSaveData
            {
                Seed = seed,
                BaseRadius = field.BaseRadius,
                TerrainEdits = new SerializableTerrainEdit[field.Edits.Count],
                MaterialEdits = new SerializableMaterialEdit[layer.EditCount],
            };

            // Capture SDF edits
            for (int i = 0; i < field.Edits.Count; i++)
            {
                TerrainEdit edit = field.Edits[i];
                data.TerrainEdits[i] = new SerializableTerrainEdit
                {
                    LocalPositionX = edit.LocalPosition.x,
                    LocalPositionY = edit.LocalPosition.y,
                    Radius = edit.Radius,
                    IsAdditive = edit.IsAdditive,
                };
            }

            // Capture material edits
            for (int i = 0; i < layer.EditCount; i++)
            {
                MaterialEdit edit = layer.Edits[i];
                data.MaterialEdits[i] = new SerializableMaterialEdit
                {
                    LocalPositionX = edit.LocalPosition.x,
                    LocalPositionY = edit.LocalPosition.y,
                    Radius = edit.Radius,
                    MaterialIdValue = edit.MaterialId.Value,
                    Order = edit.Order,
                };
            }

            // Capture inventory
            var slotList = new List<SerializableInventorySlot>();
            inventory.ForEach(slot =>
            {
                if (!slot.IsEmpty)
                {
                    slotList.Add(new SerializableInventorySlot
                    {
                        ResourceId = slot.ResourceId,
                        Quantity = slot.Quantity,
                        MaxStack = slot.MaxStack,
                    });
                }
            });
            data.InventorySlots = slotList.ToArray();

            return data;
        }

        /// <summary>
        /// Serializes this save data to a JSON string.
        /// </summary>
        public string ToJson()
        {
            return JsonUtility.ToJson(this, prettyPrint: false);
        }

        /// <summary>
        /// Serializes this save data to a pretty-printed JSON string (useful for debugging).
        /// </summary>
        public string ToJsonPretty()
        {
            return JsonUtility.ToJson(this, prettyPrint: true);
        }

        /// <summary>
        /// Deserializes a JSON string into a WorldSaveData.
        /// </summary>
        public static WorldSaveData FromJson(string json)
        {
            return JsonUtility.FromJson<WorldSaveData>(json);
        }

        /// <summary>
        /// Applies the saved data to the world systems, replacing their current state.
        /// This is the "load" operation — it clears existing edits and restores the saved ones.
        /// </summary>
        public void Apply(
            TerrainField field,
            MaterialLayer layer,
            Inventory inventory)
        {
            // Restore SDF edits
            var terrainEdits = new List<TerrainEdit>(TerrainEdits.Length);
            for (int i = 0; i < TerrainEdits.Length; i++)
            {
                terrainEdits.Add(TerrainEdits[i].ToTerrainEdit());
            }
            field.LoadEdits(terrainEdits);

            // Restore material edits
            var materialEdits = new List<MaterialEdit>(MaterialEdits.Length);
            for (int i = 0; i < MaterialEdits.Length; i++)
            {
                materialEdits.Add(MaterialEdits[i].ToMaterialEdit());
            }
            layer.LoadEdits(materialEdits);

            // Restore inventory
            inventory.Clear();
            for (int i = 0; i < InventorySlots.Length; i++)
            {
                SerializableInventorySlot slot = InventorySlots[i];
                if (slot.Quantity > 0)
                {
                    inventory.Add(slot.ResourceId, slot.Quantity);
                }
            }
        }
    }
}
