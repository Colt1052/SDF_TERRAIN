using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using SDFTerrain.Materials;

namespace SDFTerrain.Editor
{
    /// <summary>
    /// Editor utility that generates the default MaterialDefinition ScriptableObject assets
    /// into Resources/Materials/. Uses reflection to set [SerializeField] private fields,
    /// respecting the OnValidate clamping the runtime class performs.
    /// </summary>
    public static class MaterialAssetsCreator
    {
        private const string ResourcesFolderPath = "Assets/SDF_Terrain/Resources/Materials";

        /// <summary>
        /// (Id, DisplayName, Color, Density, Hardness, Friction, ThermalConductivity, MeltingPoint, StructuralStrength)
        /// </summary>
        private static readonly object[] sDefaultMaterials = new object[]
        {
            new object[] { "dirt",         "Dirt",         new Color(0.45f, 0.30f, 0.15f),  1.5f, 0.15f, 0.8f, 0.5f,  500f,  0.15f },
            new object[] { "soil",         "Soil",         new Color(0.35f, 0.25f, 0.12f),  1.3f, 0.10f, 0.85f, 0.4f,  450f,  0.10f },
            new object[] { "stone",        "Stone",        new Color(0.55f, 0.55f, 0.55f),  2.7f, 0.50f, 0.6f, 2.0f, 1510f,  0.70f },
            new object[] { "granite",      "Granite",      new Color(0.70f, 0.65f, 0.60f),  2.7f, 0.60f, 0.6f, 1.5f, 1200f,  0.80f },
            new object[] { "basalt",       "Basalt",       new Color(0.30f, 0.30f, 0.32f),  3.0f, 0.65f, 0.5f, 1.8f, 1450f,  0.75f },
            new object[] { "ice",          "Ice",          new Color(0.70f, 0.85f, 0.95f),  0.9f, 0.20f, 0.2f, 0.3f,  273f,  0.30f },
            new object[] { "metallic_core","Metallic Core", new Color(0.85f, 0.60f, 0.20f),  8.0f, 0.90f, 0.3f, 8.0f, 1800f,  0.95f },
            new object[] { "iron_ore",     "Iron Ore",     new Color(0.50f, 0.35f, 0.25f),  3.5f, 0.70f, 0.5f, 3.0f, 1538f,  0.75f },
            new object[] { "copper_ore",   "Copper Ore",   new Color(0.65f, 0.35f, 0.15f),  3.0f, 0.55f, 0.5f, 4.0f, 1085f,  0.60f },
            new object[] { "gold_ore",     "Gold Ore",     new Color(0.85f, 0.75f, 0.20f),  5.0f, 0.40f, 0.4f, 6.0f,  1064f,  0.45f },
            new object[] { "uranium_ore",  "Uranium Ore",  new Color(0.20f, 0.50f, 0.15f),  4.5f, 0.50f, 0.5f, 1.0f, 1405f,  0.55f },
            new object[] { "magma",        "Magma",        new Color(0.90f, 0.30f, 0.05f),  2.5f, 0.05f, 0.1f, 5.0f, 2000f,  0.05f },

            // Geological profile materials (EarthLike profile)
            new object[] { "deep_stone",   "Deep Stone",   new Color(0.35f, 0.35f, 0.38f),  3.0f, 0.70f, 0.5f, 1.8f, 1600f,  0.80f },
            new object[] { "mantle",       "Mantle",       new Color(0.70f, 0.30f, 0.10f),  3.3f, 0.80f, 0.4f, 3.0f, 1300f,  0.60f },
            new object[] { "molten_mantle","Molten Mantle",new Color(0.95f, 0.45f, 0.05f),  3.0f, 0.05f, 0.1f, 5.0f, 1400f,  0.05f },
        };

        [MenuItem("Tools/SDF Terrain/Create Default Materials")]
        public static void CreateDefaultMaterials()
        {
            // Ensure the target folder exists
            if (!System.IO.Directory.Exists(ResourcesFolderPath))
            {
                System.IO.Directory.CreateDirectory(ResourcesFolderPath);
                AssetDatabase.Refresh();
            }

            int created = 0;

            for (int i = 0; i < sDefaultMaterials.Length; i++)
            {
                object[] mat = (object[])sDefaultMaterials[i];
                string id = (string)mat[0];
                string displayName = (string)mat[1];
                Color color = (Color)mat[2];
                float density = (float)mat[3];
                float hardness = (float)mat[4];
                float friction = (float)mat[5];
                float thermalConductivity = (float)mat[6];
                float meltingPoint = (float)mat[7];
                float structuralStrength = (float)mat[8];

                string assetName = CapitalizeId(id);

                string assetPath = $"{ResourcesFolderPath}/{assetName}.asset";

                // Create or overwrite existing
                MaterialDefinition definition = ScriptableObject.CreateInstance<MaterialDefinition>();

                SetField(definition, "id", id);
                SetField(definition, "displayName", displayName);
                SetField(definition, "color", color);
                SetField(definition, "density", density);
                SetField(definition, "hardness", hardness);
                SetField(definition, "friction", friction);
                SetField(definition, "thermalConductivity", thermalConductivity);
                SetField(definition, "meltingPoint", meltingPoint);
                SetField(definition, "structuralStrength", structuralStrength);

                AssetDatabase.CreateAsset(definition, assetPath);

                // Trigger OnValidate clamping
                EditorUtility.SetDirty(definition);

                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Created {created} default material assets in {ResourcesFolderPath}");
        }

        private static string CapitalizeId(string id)
        {
            string[] parts = id.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
                }
            }
            return string.Concat(parts);
        }

        /// <summary>
        /// Set a [SerializeField] private field on a MaterialDefinition using reflection.
        /// </summary>
        private static void SetField<T>(MaterialDefinition target, string fieldName, T value)
        {
            FieldInfo field = typeof(MaterialDefinition)
                .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

            if (field == null)
            {
                Debug.LogError($"MaterialAssetsCreator: Field \"{fieldName}\" not found on MaterialDefinition.");
                return;
            }

            field.SetValue(target, value);
        }
    }
}
