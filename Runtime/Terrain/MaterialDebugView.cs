using System;
using System.Collections.Generic;
using UnityEngine;
using SDFTerrain.Materials;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Debug visualization mode for material state. Renders a texture over the planet showing
    /// material distribution using distinct colors per <see cref="MaterialId"/>.
    ///
    /// Supports visualization modes:
    /// - <see cref="MaterialDebugMode.MaterialId"/>: Each material rendered with its defined color.
    /// - <see cref="MaterialDebugMode.MaterialIdRaw"/>: Each material rendered with a numeric hash color (shows ID differences clearly).
    /// - <see cref="MaterialDebugMode.EditsOnly"/>: Shows only positions covered by material edits (overrides natural geology).
    /// - <see cref="MaterialDebugMode.MaterialBoundaries"/>: Highlights boundaries where adjacent samples have different materials.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class MaterialDebugView : MonoBehaviour
    {
        public enum MaterialDebugMode
        {
            /// <summary>Each material rendered with its defined color from MaterialColorMap.</summary>
            MaterialId,
            /// <summary>Each material rendered with a deterministic numeric color (MaterialId.Value-based).</summary>
            MaterialIdRaw,
            /// <summary>Shows only positions covered by material edits. Natural geology is shown as black.</summary>
            EditsOnly,
            /// <summary>Highlights boundaries where adjacent samples have different materials.</summary>
            MaterialBoundaries,
        }

        [SerializeField] private bool visible;
        [SerializeField] private MaterialDebugMode mode = MaterialDebugMode.MaterialId;
        [SerializeField] private int resolution = 128;

        private MaterialLayer _materialLayer;
        private TerrainField _field;
        private float _maxRadius;
        private Mesh _quadMesh;
        private Texture2D _texture;
        private Material _material;
        private bool _initialized;

        /// <summary>Prepares the debug quad for the given material layer and field.</summary>
        public void Initialize(TerrainField field, MaterialLayer materialLayer, float maxRadius)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));
            if (materialLayer == null)
                throw new ArgumentNullException(nameof(materialLayer));
            if (maxRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(maxRadius), maxRadius, "Max radius must be positive.");

            _field = field;
            _materialLayer = materialLayer;
            _maxRadius = maxRadius;
            _initialized = true;

            BuildQuadMesh(maxRadius);
            ApplyVisibility();
        }

        /// <summary>Re-samples the material layer and repaints the debug texture.</summary>
        public void Refresh()
        {
            if (!_initialized)
                throw new InvalidOperationException("Refresh requires Initialize to have been called first.");

            int res = Mathf.Max(2, resolution);
            _texture = BuildMaterialTexture(res);

            EnsureMaterial();
            _material.mainTexture = _texture;
        }

        Texture2D BuildMaterialTexture(int res)
        {
            var tex = new Texture2D(res, res, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Point;

            float step = (2f * _maxRadius) / res;
            float start = -_maxRadius;

            var db = _materialLayer.GetDatabase();
            Color[,] materialColors = new Color[res, res];
            MaterialId[,] materialIds = new MaterialId[res, res];
            bool[,] isEdit = new bool[res, res];

            // Sample all positions
            for (int py = 0; py < res; py++)
            {
                for (int px = 0; px < res; px++)
                {
                    Vector2 worldPos = new Vector2(
                        start + px * step,
                        start + py * step);

                    float sdf = _field.Sample(worldPos);

                    if (sdf > 0f)
                    {
                        materialColors[px, py] = Color.white;
                        materialIds[px, py] = MaterialId.Air;
                        isEdit[px, py] = false;
                    }
                    else
                    {
                        MaterialSample sample = _materialLayer.Sample(_field, worldPos);
                        materialIds[px, py] = sample.MaterialId;
                        materialColors[px, py] = MaterialColorMap.GetColor(sample.MaterialId, db);

                        // Detect if a material edit covers this position: scan edits in reverse.
                        isEdit[px, py] = AnyEditContains(_materialLayer, worldPos);
                    }
                }
            }

            // Apply mode-specific coloring
            for (int py = 0; py < res; py++)
            {
                for (int px = 0; px < res; px++)
                {
                    Color c = materialColors[px, py];

                    switch (mode)
                    {
                        case MaterialDebugMode.MaterialId:
                            // Already set above
                            break;

                        case MaterialDebugMode.MaterialIdRaw:
                            c = RawIdColor(materialIds[px, py]);
                            break;

                        case MaterialDebugMode.EditsOnly:
                            c = isEdit[px, py] && materialIds[px, py] != MaterialId.Air
                                ? materialColors[px, py]
                                : (materialIds[px, py] == MaterialId.Air ? Color.white : Color.black);
                            break;

                        case MaterialDebugMode.MaterialBoundaries:
                            c = BoundaryColor(materialIds, px, py);
                            break;
                    }

                    tex.SetPixel(px, py, c);
                }
            }

            tex.Apply();
            return tex;
        }

        static bool AnyEditContains(MaterialLayer layer, Vector2 position)
        {
            foreach (MaterialEdit edit in layer.Edits)
            {
                if (edit.Contains(position))
                    return true;
            }
            return false;
        }

        Color RawIdColor(MaterialId id)
        {
            if (id == MaterialId.Air) return Color.white;
            if (!id.IsValid) return Color.gray;
            // Deterministic color from the numeric ID
            float hue = ((id.Value * 0.618033988749895f) % 1.0f);
            return Color.HSVToRGB(hue, 0.8f, 1.0f);
        }

        Color BoundaryColor(MaterialId[,] ids, int x, int y)
        {
            MaterialId current = ids[x, y];
            if (current == MaterialId.Air) return Color.white;

            int res = ids.GetLength(0);
            bool isBoundary = false;

            // Check 4-neighborhood
            if (x > 0 && ids[x - 1, y] != current) isBoundary = true;
            if (x < res - 1 && ids[x + 1, y] != current) isBoundary = true;
            if (y > 0 && ids[x, y - 1] != current) isBoundary = true;
            if (y < res - 1 && ids[x, y + 1] != current) isBoundary = true;

            return isBoundary ? Color.yellow : Color.clear;
        }

        /// <summary>Re-samples if currently visible; no-op otherwise. Call after material layer changes.</summary>
        public void NotifyTerrainChanged()
        {
            if (visible)
                Refresh();
        }

        private void OnValidate()
        {
            if (_initialized)
                ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            var meshRenderer = GetComponent<MeshRenderer>();
            if (visible)
                Refresh();
            meshRenderer.enabled = visible;
        }

        private void EnsureMaterial()
        {
            if (_material != null) return;

            var meshRenderer = GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Sprites/Default");
            _material = new Material(shader);
            meshRenderer.material = _material;
        }

        private void BuildQuadMesh(float maxRadius)
        {
            if (_quadMesh == null)
                _quadMesh = new Mesh { name = "MaterialDebugQuad" };

            _quadMesh.Clear();
            _quadMesh.vertices = new[]
            {
                new Vector3(-maxRadius, -maxRadius, 0f),
                new Vector3(maxRadius, -maxRadius, 0f),
                new Vector3(maxRadius, maxRadius, 0f),
                new Vector3(-maxRadius, maxRadius, 0f),
            };
            _quadMesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            _quadMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quadMesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = _quadMesh;
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                if (Application.isPlaying) Destroy(_material);
                else DestroyImmediate(_material);
            }

            if (_texture != null)
            {
                if (Application.isPlaying) Destroy(_texture);
                else DestroyImmediate(_texture);
            }
        }
    }
}
