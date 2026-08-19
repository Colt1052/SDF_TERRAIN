using System;
using UnityEngine;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Runtime-toggleable debug visualization of a planet's raw <see cref="TerrainField"/>,
    /// independent of the Marching Squares mesh (per CLAUDE.md's "never build black box systems" —
    /// this exposes the density field itself, not just its meshed derivative). A quad covering the
    /// planet's bounding box is textured via <see cref="SDFDebugTexture"/>; toggling the `visible`
    /// Inspector checkbox at runtime shows/hides it without touching ChunkTerrainRenderer.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class SDFDebugView : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private int resolution = 128;

        private TerrainField _field;
        private float _maxRadius;
        private Mesh _quadMesh;
        private Texture2D _texture;
        private Material _material;
        private bool _initialized;

        /// <summary>Prepares the debug quad for the given field; must be called before Refresh.</summary>
        public void Initialize(TerrainField field, float maxRadius)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            if (maxRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRadius), maxRadius, "Max radius must be positive.");
            }

            _field = field;
            _maxRadius = maxRadius;
            _initialized = true;

            BuildQuadMesh(maxRadius);
            ApplyVisibility();
        }

        /// <summary>Re-samples the field and repaints the debug texture.</summary>
        public void Refresh()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("Refresh requires Initialize to have been called first.");
            }

            TerrainFieldSampler.Result sampled = TerrainFieldSampler.Sample(_field, resolution, _maxRadius);
            _texture = SDFDebugTexture.Build(sampled, _texture);

            EnsureMaterial();
            _material.mainTexture = _texture;
        }

        /// <summary>Re-samples if currently visible; no-op otherwise. Call after the field changes.</summary>
        public void NotifyTerrainChanged()
        {
            if (visible)
            {
                Refresh();
            }
        }

        private void OnValidate()
        {
            if (_initialized)
            {
                ApplyVisibility();
            }
        }

        private void ApplyVisibility()
        {
            var meshRenderer = GetComponent<MeshRenderer>();

            if (visible)
            {
                Refresh();
            }

            meshRenderer.enabled = visible;
        }

        private void EnsureMaterial()
        {
            if (_material != null)
            {
                return;
            }

            var meshRenderer = GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Sprites/Default");
            _material = new Material(shader);
            meshRenderer.material = _material;
        }

        private void BuildQuadMesh(float maxRadius)
        {
            if (_quadMesh == null)
            {
                _quadMesh = new Mesh { name = "SDFDebugQuad" };
            }

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
                if (Application.isPlaying)
                {
                    Destroy(_material);
                }
                else
                {
                    DestroyImmediate(_material);
                }
            }

            if (_texture != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_texture);
                }
                else
                {
                    DestroyImmediate(_texture);
                }
            }
        }
    }
}
