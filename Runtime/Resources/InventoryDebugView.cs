using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SDFTerrain.Materials;

namespace SDFTerrain.Resources
{
    /// <summary>
    /// Renders an in-game inventory overlay using OnGUI. Reads from a <see cref="Inventory"/>
    /// instance via a <see cref="TerrainExcavationSystem"/> reference.
    ///
    /// Shows each non-empty inventory slot as a row with a color swatch (from the
    /// MaterialDatabase), resource name, and quantity. Toggle visibility with Tab.
    /// </summary>
    public class InventoryDebugView : MonoBehaviour
    {
        [Tooltip("System that holds the inventory reference (set from PlanetDemo or other wiring code).")]
        [SerializeField] private TerrainExcavationSystem excavationSystem;

        [Tooltip("Key to toggle the inventory menu visibility.")]
        [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

        [Header("Appearance")]
        [SerializeField] private float panelWidth = 220f;
        [SerializeField] private float headerHeight = 28f;
        [SerializeField] private float rowHeight = 24f;
        [SerializeField] private float margin = 12f;

        private bool _isVisible = true;
        private MaterialDatabase _database;

        // Cached styles (text only)
        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;

        // Texture cache keyed by "R,G,B,A" string
        private readonly Dictionary<string, Texture2D> _colorTexCache = new Dictionary<string, Texture2D>();

        private void Start()
        {
            _database = MaterialDatabase.Instance;
        }

        private void TryDiscoverSystem()
        {
            if (excavationSystem != null)
                return;

            // 1) Same GameObject
            var demo = GetComponent<Terrain.PlanetDemo>();
            if (demo != null && demo.ExcavationSystem != null)
            {
                excavationSystem = demo.ExcavationSystem;
                Debug.Log("[InventoryDebugView] Wired to TerrainExcavationSystem (same GameObject). Press " + toggleKey + " to toggle.");
                return;
            }

            // 2) Anywhere in scene
            foreach (var pd in UnityEngine.Object.FindObjectsOfType<Terrain.PlanetDemo>())
            {
                if (pd != null && pd.ExcavationSystem != null)
                {
                    excavationSystem = pd.ExcavationSystem;
                    Debug.Log("[InventoryDebugView] Wired to TerrainExcavationSystem (found in scene). Press " + toggleKey + " to toggle.");
                    return;
                }
            }

            Debug.LogWarning("[InventoryDebugView] No TerrainExcavationSystem found in the scene. " +
                "Ensure PlanetDemo is in the scene and has started (it creates the system in Start).");
        }

        private void BuildStyles()
        {
            _labelStyle = new GUIStyle
            {
                normal = { textColor = new Color(0.92f, 0.92f, 0.95f) },
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };

            _headerStyle = new GUIStyle
            {
                normal = { textColor = new Color(0.95f, 0.96f, 1f) },
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                _isVisible = !_isVisible;
            }
        }

        private void OnGUI()
        {
            if (!_isVisible)
                return;

            // Build styles lazily here — GUI.skin is only valid inside OnGUI.
            if (_labelStyle == null)
                BuildStyles();

            // Discover system lazily — OnGUI runs after all Start() methods have completed,
            // so PlanetDemo.ExcavationSystem is guaranteed to be initialized by now.
            TryDiscoverSystem();

            Inventory inventory = excavationSystem?.GetInventory();
            if (inventory == null)
            {
                DrawNoSystemHint();
                return;
            }

            var slots = new List<InventorySlot>();
            inventory.ForEach(s => slots.Add(s));

            if (slots.Count == 0)
            {
                DrawEmptyState();
                return;
            }

            float panelH = headerHeight + slots.Count * rowHeight + margin * 2;
            float x = margin;
            float y = margin;

            DrawColoredRect(x, y, panelWidth, panelH, new Color(0.1f, 0.1f, 0.12f, 0.88f));

            float contentY = y + margin;
            GUI.Label(new Rect(x + 8f, contentY, panelWidth - 16f, headerHeight),
                "Inventory", _headerStyle);

            // Separator line
            contentY += headerHeight;
            DrawColoredRect(x + 4f, contentY, panelWidth - 8f, 1f, new Color(0.3f, 0.3f, 0.35f, 1f));
            contentY += 4f;

            foreach (var slot in slots)
            {
                DrawRow(x + 8f, contentY, panelWidth - 16f, slot);
                contentY += rowHeight;
            }
        }

        private void DrawRow(float x, float y, float w, InventorySlot slot)
        {
            float swatchSize = rowHeight - 8f;
            float swatchY = y + 4f;

            // Color swatch
            Color swatchColor = GetSwatchColor(slot.ResourceId);
            DrawColoredRect(x, swatchY, swatchSize, swatchSize, swatchColor);

            // Name
            string name = FormatResourceName(slot.ResourceId);
            float nameX = x + swatchSize + 6f;
            float nameWidth = w - swatchSize - 24f;
            GUI.Label(new Rect(nameX, y, nameWidth, rowHeight),
                name, _labelStyle);

            // Quantity
            string qty = slot.Quantity.ToString();
            float qtyW = _labelStyle.CalcSize(new GUIContent(qty)).x;
            GUI.Label(new Rect(x + w - qtyW, y, qtyW, rowHeight),
                qty, _labelStyle);
        }

        private void DrawEmptyState()
        {
            float panelH = headerHeight + rowHeight + margin * 3;
            float x = margin;
            float y = margin;

            DrawColoredRect(x, y, panelWidth, panelH, new Color(0.1f, 0.1f, 0.12f, 0.88f));

            GUI.Label(new Rect(x + 8f, y + margin, panelWidth - 16f, headerHeight),
                "Inventory", _headerStyle);
            GUI.Label(new Rect(x + 16f, y + margin + headerHeight + 4f, panelWidth - 32f, rowHeight),
                "(empty)", _labelStyle);
        }

        private void DrawNoSystemHint()
        {
            float panelH = headerHeight + rowHeight + margin * 3;
            float x = margin;
            float y = margin;

            DrawColoredRect(x, y, panelWidth, panelH, new Color(0.1f, 0.08f, 0.08f, 0.9f));

            GUI.Label(new Rect(x + 8f, y + margin, panelWidth - 16f, headerHeight),
                "Inventory", _headerStyle);
            GUI.Label(new Rect(x + 16f, y + margin + headerHeight + 4f, panelWidth - 32f, rowHeight),
                "No excavation system wired.", _labelStyle);
        }

        private void DrawColoredRect(float x, float y, float w, float h, Color color)
        {
            Texture2D tex = GetColorTexture(color);
            GUI.DrawTexture(new Rect(x, y, w, h), tex, ScaleMode.StretchToFill, true);
        }

        private Texture2D GetColorTexture(Color color)
        {
            string key = color.r + "," + color.g + "," + color.b + "," + color.a;
            if (_colorTexCache.TryGetValue(key, out Texture2D tex))
                return tex;

            tex = new Texture2D(1, 1);
            tex.filterMode = FilterMode.Point;
            tex.SetPixel(0, 0, color);
            tex.Apply();
            _colorTexCache[key] = tex;
            return tex;
        }

        private Color GetSwatchColor(string resourceId)
        {
            if (_database != null && _database.HasMaterial(resourceId))
            {
                MaterialDefinition def = _database.GetMaterial(resourceId);
                if (def != null)
                    return def.Color;
            }
            return HashColor(resourceId);
        }

        private static Color HashColor(string key)
        {
            int h = key.GetHashCode();
            float r = ((h >> 16) & 0xFF) / 255f;
            float g = ((h >> 8) & 0xFF) / 255f;
            float b = (h & 0xFF) / 255f;

            float max = Mathf.Max(r, Mathf.Max(g, b));
            float min = Mathf.Min(r, Mathf.Min(g, b));
            if (max - min < 0.2f)
            {
                r = Mathf.Max(r, 0.4f);
                g = Mathf.Max(g, 0.2f);
                b = Mathf.Max(b, 0.3f);
            }
            return new Color(r, g, b);
        }

        private static string FormatResourceName(string resourceId)
        {
            if (string.IsNullOrEmpty(resourceId))
                return "Unknown";

            string[] parts = resourceId.Split('_');
            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                if (part.Length > 0)
                    sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                    sb.Append(part.Substring(1));
            }
            return sb.ToString();
        }
    }
}
