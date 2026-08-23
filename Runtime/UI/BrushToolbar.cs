using System;
using System.Collections.Generic;
using UnityEngine;
using SDFTerrain.Core;
using SDFTerrain.Terrain;

namespace SDFTerrain.UI
{
    /// <summary>
    /// MonoBehaviour that manages brush state (mode, radius) and applies brush strokes
    /// to the terrain. Add to a GameObject alongside ChunkTerrainRenderer.
    ///
    /// In the Editor, <see cref="BrushToolbarEditor"/> (in the Editor folder) attaches
    /// a SceneView toolbar, gizmo preview, and keyboard shortcuts automatically.
    ///
    /// Keyboard shortcuts: [1] Add, [2] Remove, [3] Smooth, [Q/E] shrink/grow radius.
    /// Left mouse button applies the brush while held.
    /// </summary>
    public class BrushToolbar : MonoBehaviour
    {
        private const string PrefKey_Mode = "SDFTerrain_BrushMode";
        private const string PrefKey_Radius = "SDFTerrain_BrushRadius";
        private const string PrefKey_StrikeRadius = "SDFTerrain_StrikeRadius";
        private const string PrefKey_SearchRayCount = "SDFTerrain_SearchRayCount";
        public const float MinRadius = 0.5f;
        public const float MaxRadius = 20f;
        public const float MinStrikeRadius = 0.3f;
        public const float MaxStrikeRadius = 10f;
        public const int MinSearchRayCount = 4;
        public const int MaxSearchRayCount = 72;

        private static readonly string[] ModeLabels = { "Add", "Remove", "Smooth", "Electric" };

        [Header("Targets")]
        [SerializeField] private ChunkTerrainRenderer chunkTerrainRenderer;
        [SerializeField] private Transform planetCenter;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private TerrainStats terrainStats;

        [Header("Settings")]
        [SerializeField] [Range(MinRadius, MaxRadius)] private float brushRadius = 3f;
        [SerializeField] [Range(MinStrikeRadius, MaxStrikeRadius)] private float strikeRadius = 1.5f;
        [SerializeField] [Range(MinSearchRayCount, MaxSearchRayCount)] private int searchRayCount = 36;

        private BrushMode _mode;
        private bool _isActive;

        public BrushMode Mode
        {
            get => _mode;
            set => _mode = value;
        }

        public float Radius
        {
            get => brushRadius;
            set => brushRadius = Mathf.Clamp(value, MinRadius, MaxRadius);
        }

        /// <summary>Strike radius used by the Electric brush to control crater size.</summary>
        public float StrikeRadius
        {
            get => strikeRadius;
            set => strikeRadius = Mathf.Clamp(value, MinStrikeRadius, MaxStrikeRadius);
        }

        /// <summary>Number of search rays used by the Electric brush to scan for terrain.</summary>
        public int SearchRayCount
        {
            get => searchRayCount;
            set => searchRayCount = Mathf.Clamp(value, MinSearchRayCount, MaxSearchRayCount);
        }

        public ChunkTerrainRenderer ChunkTerrainRenderer => chunkTerrainRenderer;
        public Transform PlanetCenter => planetCenter;
        public Camera TargetCamera => targetCamera;

        /// <summary>True while the user is holding the mouse button to paint.</summary>
        public bool IsActive => _isActive;

        private void Awake()
        {
            string savedMode = PlayerPrefs.GetString(PrefKey_Mode, "");
            _mode = Array.IndexOf(ModeLabels, savedMode) >= 0
                ? (BrushMode)Array.IndexOf(ModeLabels, savedMode)
                : BrushMode.Add;

            brushRadius = Mathf.Clamp(PlayerPrefs.GetFloat(PrefKey_Radius, 3f), MinRadius, MaxRadius);
            strikeRadius = Mathf.Clamp(PlayerPrefs.GetFloat(PrefKey_StrikeRadius, 1.5f), MinStrikeRadius, MaxStrikeRadius);
            searchRayCount = Mathf.Clamp(PlayerPrefs.GetInt(PrefKey_SearchRayCount, 36), MinSearchRayCount, MaxSearchRayCount);

            if (terrainStats != null && chunkTerrainRenderer != null)
            {
                terrainStats.Initialize(chunkTerrainRenderer);
            }
        }

        private void Update()
        {
            HandleKeyboardShortcuts();
            HandleMouseInput();
        }

        /// <summary>Handle keyboard shortcuts for mode and radius. Public so editor UI can invoke.</summary>
        public void HandleKeyboardShortcuts()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                SetMode(BrushMode.Add);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                SetMode(BrushMode.Remove);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                SetMode(BrushMode.Smooth);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                SetMode(BrushMode.Electric);
            }

            if (Input.GetKeyDown(KeyCode.Q))
            {
                Radius = Mathf.Max(MinRadius, Radius - 0.5f);
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                Radius = Mathf.Min(MaxRadius, Radius + 0.5f);
            }
        }

        private void HandleMouseInput()
        {
            _isActive = Input.GetMouseButton(0);

            if (_isActive)
            {
                ApplyBrushAtMousePosition();
            }
        }

        /// <summary>Applies the current brush at the mouse position. Public so editor UI can invoke.</summary>
        public void ApplyBrushAtMouse()
        {
            ApplyBrushAtMousePosition();
        }

        private void ApplyBrushAtMousePosition()
        {
            Camera camera = targetCamera != null ? targetCamera : Camera.main;
            if (camera == null)
            {
                return;
            }

            if (chunkTerrainRenderer == null)
            {
                return;
            }

            Vector2 worldPosition = camera.ScreenToWorldPoint(Input.mousePosition);
            Vector2 planetCenterPosition = planetCenter != null ? (Vector2)planetCenter.position : Vector2.zero;
            Vector2 localPosition = PlanetCoordinates.WorldToLocal(worldPosition, planetCenterPosition);

            var brush = new TerrainBrush(_mode, brushRadius);
            BrushAreaDelta delta = chunkTerrainRenderer.ApplyBrush(brush, localPosition, strikeRadius, searchRayCount);

            if (terrainStats != null)
            {
                terrainStats.RecordDelta(delta);
            }
        }

        /// <summary>Sets the active brush mode and persists to PlayerPrefs.</summary>
        public void SetMode(BrushMode mode)
        {
            if (_mode != mode)
            {
                _mode = mode;
                PlayerPrefs.SetString(PrefKey_Mode, ModeLabels[(int)mode]);
            }
        }

        /// <summary>Draws the toolbar UI in the Game View at runtime.</summary>
        private void OnGUI()
        {
            DrawToolbarGUI();
            DrawIndicatorGUI();
            DrawTerrainStatsPanel();
            DrawBrushPreviewOverlay();
        }

        /// <summary>Draws a colored circle at the mouse position showing the brush footprint.</summary>
        private void DrawBrushPreviewOverlay()
        {
            Camera camera = targetCamera != null ? targetCamera : Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector2 worldPos = camera.ScreenToWorldPoint(Input.mousePosition);
            Vector3 screenPos = camera.WorldToScreenPoint(worldPos);

            // Convert world-space radius to screen pixels by projecting an edge point.
            Vector2 edgeWorld = worldPos + new Vector2(brushRadius, 0f);
            Vector3 edgeScreen = camera.WorldToScreenPoint(edgeWorld);
            float radiusPixels = Mathf.Abs(edgeScreen.x - screenPos.x);

            Color modeColor = GetModeColor(_mode, 0.6f);

            // Draw circle using GL within OnGUI.
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            GL.Begin(GL.LINES);
            GL.Color(modeColor);
            for (int i = 0; i <= 360; i += 5)
            {
                float angle1 = i * Mathf.Deg2Rad;
                float angle2 = (i + 5) * Mathf.Deg2Rad;
                float sx1 = screenPos.x + Mathf.Cos(angle1) * radiusPixels;
                float sy1 = Screen.height - screenPos.y + Mathf.Sin(angle1) * radiusPixels;
                float sx2 = screenPos.x + Mathf.Cos(angle2) * radiusPixels;
                float sy2 = Screen.height - screenPos.y + Mathf.Sin(angle2) * radiusPixels;
                GL.Vertex3(sx1, sy1, 0f);
                GL.Vertex3(sx2, sy2, 0f);
            }
            GL.End();
            GL.PopMatrix();
        }

        private void DrawToolbarGUI()
        {
            float width = 340f;
            float height = 38f;
            float x = (Screen.width - width) / 2f;
            float y = 8f;

            // Background panel.
            Rect toolbarRect = new Rect(x, y, width, height);
            DrawRect(toolbarRect, new Color(0.15f, 0.15f, 0.15f, 0.85f));
            DrawRect(new Rect(x, y + height - 1f, width, 1f), new Color(0.5f, 0.5f, 0.5f, 0.6f));

            // Mode toggle buttons.
            float buttonWidth = (width - 40f) / 3f;
            float buttonX = x + 8f;

            for (int i = 0; i < ModeLabels.Length; i++)
            {
                BrushMode mode = (BrushMode)i;
                bool isActive = _mode == mode;

                Rect buttonRect = new Rect(buttonX, y + 4f, buttonWidth, height - 8f);

                if (GUI.Button(buttonRect, ModeLabels[i]))
                {
                    SetMode(mode);
                }

                // Highlight active button.
                if (isActive)
                {
                    Color accent = GetModeColor(mode, alpha: 1f);
                    DrawRect(new Rect(buttonX, y + height - 3f, buttonWidth, 2f), accent);
                }

                buttonX += buttonWidth + 4f;
            }

            // Radius slider(s).
            float sliderX = x + 8f;
            float sliderWidth = width - 16f;
            float sliderY = y + height + 4f;

            if (_mode == BrushMode.Electric)
            {
                // Electric mode shows sliders: search radius, strike radius, and ray count.
                GUI.Label(new Rect(sliderX, sliderY, 65f, 16f), "Search R");
                brushRadius = GUI.HorizontalSlider(
                    new Rect(sliderX + 70f, sliderY + 2f, sliderWidth - 110f, 16f),
                    brushRadius, MinRadius, MaxRadius);
                GUI.Label(new Rect(sliderX + sliderWidth - 35f, sliderY, 35f, 16f), brushRadius.ToString("F1"));

                float sliderY2 = sliderY + 20f;
                GUI.Label(new Rect(sliderX, sliderY2, 65f, 16f), "Strike R");
                strikeRadius = GUI.HorizontalSlider(
                    new Rect(sliderX + 70f, sliderY2 + 2f, sliderWidth - 110f, 16f),
                    strikeRadius, MinStrikeRadius, MaxStrikeRadius);
                GUI.Label(new Rect(sliderX + sliderWidth - 35f, sliderY2, 35f, 16f), strikeRadius.ToString("F1"));

                float sliderY3 = sliderY2 + 20f;
                GUI.Label(new Rect(sliderX, sliderY3, 65f, 16f), "Rays");
                searchRayCount = (int)GUI.HorizontalSlider(
                    new Rect(sliderX + 70f, sliderY3 + 2f, sliderWidth - 110f, 16f),
                    searchRayCount, MinSearchRayCount, MaxSearchRayCount);
                GUI.Label(new Rect(sliderX + sliderWidth - 35f, sliderY3, 35f, 16f), searchRayCount.ToString());
            }
            else
            {
                GUI.Label(new Rect(sliderX, sliderY, 55f, 16f), "Radius");
                brushRadius = GUI.HorizontalSlider(
                    new Rect(sliderX + 60f, sliderY + 2f, sliderWidth - 70f, 16f),
                    brushRadius, MinRadius, MaxRadius);
                GUI.Label(new Rect(sliderX + sliderWidth - 40f, sliderY, 40f, 16f), brushRadius.ToString("F1"));
            }
        }

        private void DrawIndicatorGUI()
        {
            float width = 340f;
            float indicatorHeight = 24f;
            float x = (Screen.width - width) / 2f;
            float y = 8f + 38f + 28f; // toolbar + slider row + gap

            // Account for Electric mode having an extra slider row.
            if (_mode == BrushMode.Electric)
            {
                y += 20f;
            }

            // Background panel.
            DrawRect(new Rect(x, y, width, indicatorHeight), new Color(0.1f, 0.1f, 0.1f, 0.9f));

            Color modeColor = GetModeColor(_mode, alpha: 1f);

            // Colored swatch.
            float swatchSize = indicatorHeight - 6f;
            DrawRect(new Rect(x + 6f, y + 3f, swatchSize, swatchSize), modeColor);

            // Mode label.
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            GUI.Label(new Rect(x + 30f, y, 80f, indicatorHeight), ModeLabels[(int)_mode], labelStyle);

            // Separator.
            DrawRect(new Rect(x + 110f, y + 4f, 1f, indicatorHeight - 8f), new Color(0.4f, 0.4f, 0.4f, 0.6f));

            // Radius readout.
            GUIStyle radiusStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };
            string radiusText = _mode == BrushMode.Electric
                ? $"S: {brushRadius:F1}  H: {strikeRadius:F1}"
                : "R: " + brushRadius.ToString("F1");
            GUI.Label(new Rect(x + 116f, y, 100f, indicatorHeight), radiusText, radiusStyle);

            // Separator.
            DrawRect(new Rect(x + 196f, y + 4f, 1f, indicatorHeight - 8f), new Color(0.4f, 0.4f, 0.4f, 0.6f));

            // Shortcut hints.
            GUIStyle hintStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) },
                alignment = TextAnchor.MiddleLeft
            };
            GUI.Label(new Rect(x + 202f, y, width - 208f, indicatorHeight), "[1/2/3/4] [Q/E]", hintStyle);
        }

        /// <summary>Draws a small stats panel showing total terrain, area built, and area mined.</summary>
        private void DrawTerrainStatsPanel()
        {
            if (terrainStats == null)
            {
                return;
            }

            float width = 340f;
            float panelHeight = 24f;
            float x = (Screen.width - width) / 2f;
            float baseY = 8f + 38f + 28f; // toolbar + slider row + gap

            if (_mode == BrushMode.Electric)
            {
                baseY += 20f;
            }

            float y = baseY + 24f + 4f; // indicator height + gap

            // Background panel.
            DrawRect(new Rect(x, y, width, panelHeight), new Color(0.1f, 0.1f, 0.1f, 0.9f));

            // Section labels and values.
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) },
                alignment = TextAnchor.MiddleLeft
            };

            GUIStyle valueStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleRight
            };

            // "Total" column.
            GUI.Label(new Rect(x + 6f, y, 42f, panelHeight), "Total", labelStyle);
            GUI.Label(new Rect(x + 48f, y, 62f, panelHeight), FormatArea(terrainStats.TotalArea), valueStyle);

            // "Built" column.
            GUI.Label(new Rect(x + 118f, y, 42f, panelHeight), "Built", labelStyle);
            GUI.Label(new Rect(x + 160f, y, 62f, panelHeight), FormatArea(terrainStats.AreaBuilt), valueStyle);

            // "Mined" column.
            GUI.Label(new Rect(x + 230f, y, 42f, panelHeight), "Mined", labelStyle);
            GUI.Label(new Rect(x + 272f, y, 62f, panelHeight), FormatArea(terrainStats.AreaMined), valueStyle);
        }

        /// <summary>Formats an area value for display, choosing appropriate units.</summary>
        private static string FormatArea(float area)
        {
            if (area >= 10000f)
            {
                return (area / 1000f).ToString("F1") + "k";
            }
            if (area >= 1000f)
            {
                return (area / 1000f).ToString("F2") + "k";
            }
            return area.ToString("F1");
        }

        private static void DrawRect(Rect rect, Color color)
        {
            var oldColor = GUI.color;
            GUI.color = color;
            GUI.Box(rect, GUIContent.none, GUI.skin.box);
            GUI.color = oldColor;
        }

        private static Color GetModeColor(BrushMode mode, float alpha)
        {
            switch (mode)
            {
                case BrushMode.Add:      return new Color(0.3f, 0.8f, 0.4f, alpha);
                case BrushMode.Remove:   return new Color(0.9f, 0.35f, 0.35f, alpha);
                case BrushMode.Smooth:   return new Color(0.5f, 0.6f, 1f, alpha);
                case BrushMode.Electric: return new Color(0.95f, 0.85f, 0.2f, alpha);
                default:                 return new Color(1f, 1f, 1f, alpha);
            }
        }

        private void OnValidate()
        {
            brushRadius = Mathf.Clamp(brushRadius, MinRadius, MaxRadius);
            strikeRadius = Mathf.Clamp(strikeRadius, MinStrikeRadius, MaxStrikeRadius);
            searchRayCount = Mathf.Clamp(searchRayCount, MinSearchRayCount, MaxSearchRayCount);
        }

        private void OnDestroy()
        {
            PlayerPrefs.SetFloat(PrefKey_Radius, brushRadius);
            PlayerPrefs.SetFloat(PrefKey_StrikeRadius, strikeRadius);
            PlayerPrefs.SetInt(PrefKey_SearchRayCount, searchRayCount);
            PlayerPrefs.Save();
        }
    }
}
