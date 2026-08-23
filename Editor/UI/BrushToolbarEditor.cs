using UnityEngine;
using UnityEditor;
using SDFTerrain.Terrain;
using SDFTerrain.UI;

namespace SDFTerrain.UI
{
    /// <summary>
    /// Editor-only SceneView toolbar for <see cref="BrushToolbar"/>.
    /// Draws mode buttons, a radius slider, and a gizmo brush preview.
    /// Auto-discovers the active BrushToolbar component in the scene.
    /// </summary>
    [InitializeOnLoad]
    public static class BrushToolbarEditor
    {
        private const float ToolbarWidth = 340f;
        private const float ToolbarHeight = 38f;

        private static readonly string[] ModeLabels = { "Add", "Remove", "Smooth", "Electric" };

        private static BrushToolbar _toolbar;
        private static TerrainStats _stats;

        static BrushToolbarEditor()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            FindToolbar();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                FindToolbar();
            }
        }

        private static void FindToolbar()
        {
            _toolbar = Object.FindObjectOfType<BrushToolbar>();
            _stats = Object.FindObjectOfType<TerrainStats>();
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (_toolbar == null)
            {
                FindToolbar();
                if (_toolbar == null)
                {
                    return;
                }
            }

            DrawToolbar();
            DrawBrushIndicator();
            DrawTerrainStatsPanel();
            DrawBrushPreview();
        }

        private static void DrawToolbar()
        {
            float width = ToolbarWidth;
            float height = ToolbarHeight;
            float x = (Screen.width - width) / 2f;
            float y = 8f;

            Rect toolbarRect = new Rect(x, y, width, height);
            EditorGUI.DrawRect(toolbarRect, new Color(0.15f, 0.15f, 0.15f, 0.85f));
            EditorGUI.DrawRect(new Rect(x, y + height - 1f, width, 1f), new Color(0.5f, 0.5f, 0.5f, 0.6f));

            float buttonWidth = (width - 40f) / 3f;
            float buttonX = x + 8f;

            for (int i = 0; i < ModeLabels.Length; i++)
            {
                BrushMode mode = (BrushMode)i;
                bool isActive = _toolbar.Mode == mode;

                GUIContent content = new GUIContent(ModeLabels[i], $"Switch to {ModeLabels[i].ToLower()} brush ({(KeyCode)(i + 9)})");
                Rect buttonRect = new Rect(buttonX, y + 4f, buttonWidth, height - 8f);

                if (GUI.Button(buttonRect, content, GUI.skin.button))
                {
                    _toolbar.SetMode(mode);
                }

                if (isActive)
                {
                    Color accent = mode switch
                    {
                        BrushMode.Add      => new Color(0.3f, 0.8f, 0.4f, 1f),
                        BrushMode.Remove   => new Color(0.9f, 0.35f, 0.35f, 1f),
                        BrushMode.Smooth   => new Color(0.5f, 0.6f, 1f, 1f),
                        BrushMode.Electric => new Color(0.95f, 0.85f, 0.2f, 1f),
                        _                  => Color.white
                    };
                    EditorGUI.DrawRect(new Rect(buttonX, y + height - 3f, buttonWidth, 2f), accent);
                }

                buttonX += buttonWidth + 4f;
            }

            // Radius slider(s).
            float sliderX = x + 8f;
            float sliderWidth = width - 16f;
            float sliderY = y + height + 4f;

            if (_toolbar.Mode == BrushMode.Electric)
            {
                EditorGUI.LabelField(new Rect(sliderX, sliderY, 65f, 16f), "Search R");
                float newRadius = EditorGUI.Slider(
                    new Rect(sliderX + 70f, sliderY, sliderWidth - 110f, 16f),
                    _toolbar.Radius,
                    BrushToolbar.MinRadius,
                    BrushToolbar.MaxRadius);
                if (Mathf.Abs(newRadius - _toolbar.Radius) > 0.01f)
                {
                    _toolbar.Radius = newRadius;
                }
                EditorGUI.LabelField(new Rect(sliderX + sliderWidth - 40f, sliderY, 40f, 16f), _toolbar.Radius.ToString("F1"), GUI.skin.textField);

                float sliderY2 = sliderY + 20f;
                EditorGUI.LabelField(new Rect(sliderX, sliderY2, 65f, 16f), "Strike R");
                float newStrikeRadius = EditorGUI.Slider(
                    new Rect(sliderX + 70f, sliderY2, sliderWidth - 110f, 16f),
                    _toolbar.StrikeRadius,
                    BrushToolbar.MinStrikeRadius,
                    BrushToolbar.MaxStrikeRadius);
                if (Mathf.Abs(newStrikeRadius - _toolbar.StrikeRadius) > 0.01f)
                {
                    _toolbar.StrikeRadius = newStrikeRadius;
                }
                EditorGUI.LabelField(new Rect(sliderX + sliderWidth - 40f, sliderY2, 40f, 16f), _toolbar.StrikeRadius.ToString("F1"), GUI.skin.textField);

                float sliderY3 = sliderY2 + 20f;
                EditorGUI.LabelField(new Rect(sliderX, sliderY3, 65f, 16f), "Rays");
                int newRayCount = EditorGUI.IntSlider(
                    new Rect(sliderX + 70f, sliderY3, sliderWidth - 110f, 16f),
                    _toolbar.SearchRayCount,
                    BrushToolbar.MinSearchRayCount,
                    BrushToolbar.MaxSearchRayCount);
                if (newRayCount != _toolbar.SearchRayCount)
                {
                    _toolbar.SearchRayCount = newRayCount;
                }
                EditorGUI.LabelField(new Rect(sliderX + sliderWidth - 40f, sliderY3, 40f, 16f), _toolbar.SearchRayCount.ToString(), GUI.skin.textField);
            }
            else
            {
                EditorGUI.LabelField(new Rect(sliderX, sliderY, 55f, 16f), "Radius");
                float newRadius = EditorGUI.Slider(
                    new Rect(sliderX + 60f, sliderY, sliderWidth - 70f, 16f),
                    _toolbar.Radius,
                    BrushToolbar.MinRadius,
                    BrushToolbar.MaxRadius);
                if (Mathf.Abs(newRadius - _toolbar.Radius) > 0.01f)
                {
                    _toolbar.Radius = newRadius;
                }
                EditorGUI.LabelField(new Rect(sliderX + sliderWidth - 40f, sliderY, 40f, 16f), _toolbar.Radius.ToString("F1"), GUI.skin.textField);
            }
        }

        private static void DrawBrushIndicator()
        {
            // Compact status indicator below the radius slider.
            float width = ToolbarWidth;
            float indicatorHeight = 24f;
            float x = (Screen.width - width) / 2f;
            float y = 8f + ToolbarHeight + 28f; // just below the slider row

            // Account for Electric mode having an extra slider row.
            if (_toolbar.Mode == BrushMode.Electric)
            {
                y += 20f;
            }

            Rect indicatorRect = new Rect(x, y, width, indicatorHeight);
            EditorGUI.DrawRect(indicatorRect, new Color(0.1f, 0.1f, 0.1f, 0.9f));

            Color modeColor = _toolbar.Mode switch
            {
                BrushMode.Add      => new Color(0.3f, 0.8f, 0.4f, 1f),
                BrushMode.Remove   => new Color(0.9f, 0.35f, 0.35f, 1f),
                BrushMode.Smooth   => new Color(0.5f, 0.6f, 1f, 1f),
                BrushMode.Electric => new Color(0.95f, 0.85f, 0.2f, 1f),
                _                  => Color.white
            };

            // Colored swatch on the left.
            float swatchSize = indicatorHeight - 6f;
            EditorGUI.DrawRect(
                new Rect(x + 6f, y + 3f, swatchSize, swatchSize),
                modeColor);

            // Mode label.
            string modeLabel = ModeLabels[(int)_toolbar.Mode];
            GUIContent modeContent = new GUIContent(modeLabel);
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            EditorGUI.LabelField(new Rect(x + 30f, y, 80f, indicatorHeight), modeContent, labelStyle);

            // Separator line.
            EditorGUI.DrawRect(new Rect(x + 110f, y + 4f, 1f, indicatorHeight - 8f), new Color(0.4f, 0.4f, 0.4f, 0.6f));

            // Radius readout.
            string radiusText = _toolbar.Mode == BrushMode.Electric
                ? $"S: {_toolbar.Radius:F1}  H: {_toolbar.StrikeRadius:F1}"
                : "R: " + _toolbar.Radius.ToString("F1");
            GUIContent radiusContent = new GUIContent(radiusText);
            GUIStyle radiusStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };
            EditorGUI.LabelField(new Rect(x + 116f, y, 100f, indicatorHeight), radiusContent, radiusStyle);

            // Separator line.
            EditorGUI.DrawRect(new Rect(x + 196f, y + 4f, 1f, indicatorHeight - 8f), new Color(0.4f, 0.4f, 0.4f, 0.6f));

            // Shortcut hints.
            GUIContent shortcutContent = new GUIContent("[1/2/3/4] [Q/E]");
            GUIStyle hintStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) },
                alignment = TextAnchor.MiddleLeft
            };
            EditorGUI.LabelField(new Rect(x + 202f, y, width - 208f, indicatorHeight), shortcutContent, hintStyle);
        }

        private static void DrawTerrainStatsPanel()
        {
            if (_stats == null)
            {
                return;
            }

            float width = ToolbarWidth;
            float panelHeight = 24f;
            float x = (Screen.width - width) / 2f;
            float baseY = 8f + ToolbarHeight + 28f;

            if (_toolbar.Mode == BrushMode.Electric)
            {
                baseY += 20f;
            }

            float y = baseY + 24f + 4f; // indicator height + gap

            // Background panel.
            EditorGUI.DrawRect(new Rect(x, y, width, panelHeight), new Color(0.1f, 0.1f, 0.1f, 0.9f));

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
            EditorGUI.LabelField(new Rect(x + 6f, y, 42f, panelHeight), new GUIContent("Total"), labelStyle);
            EditorGUI.LabelField(new Rect(x + 48f, y, 62f, panelHeight), new GUIContent(FormatArea(_stats.TotalArea)), valueStyle);

            // "Built" column.
            EditorGUI.LabelField(new Rect(x + 118f, y, 42f, panelHeight), new GUIContent("Built"), labelStyle);
            EditorGUI.LabelField(new Rect(x + 160f, y, 62f, panelHeight), new GUIContent(FormatArea(_stats.AreaBuilt)), valueStyle);

            // "Mined" column.
            EditorGUI.LabelField(new Rect(x + 230f, y, 42f, panelHeight), new GUIContent("Mined"), labelStyle);
            EditorGUI.LabelField(new Rect(x + 272f, y, 62f, panelHeight), new GUIContent(FormatArea(_stats.AreaMined)), valueStyle);
        }

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

        private static void DrawBrushPreview()
        {
            if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout)
            {
                return;
            }

            Camera camera = _toolbar.TargetCamera != null ? _toolbar.TargetCamera : Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector2 worldPos = camera.ScreenToWorldPoint(Input.mousePosition);

            Handles.color = _toolbar.Mode switch
            {
                BrushMode.Add      => new Color(0.3f, 0.8f, 0.4f, 0.6f),
                BrushMode.Remove   => new Color(0.9f, 0.35f, 0.35f, 0.6f),
                BrushMode.Smooth   => new Color(0.5f, 0.6f, 1f, 0.6f),
                BrushMode.Electric => new Color(0.95f, 0.85f, 0.2f, 0.6f),
                _                  => new Color(1f, 1f, 1f, 0.6f)
            };

            Handles.DrawWireDisc(worldPos, Vector3.forward, _toolbar.Radius);
        }
    }
}
