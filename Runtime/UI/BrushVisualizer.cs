using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.UI
{
    /// <summary>
    /// Draws a circular world-space outline showing the brush footprint.
    /// Attach alongside BrushToolbar. The ring follows the mouse and updates
    /// its color/size when the toolbar's settings change.
    /// </summary>
    public class BrushVisualizer : MonoBehaviour
    {
        private const int SegmentCount = 48;

        [SerializeField] private BrushToolbar brushToolbar;
        [SerializeField] private Transform planetCenter;
        [SerializeField] private Camera targetCamera;

        private LineRenderer _lineRenderer;

        private void Awake()
        {
            _lineRenderer = GetComponent<LineRenderer>();
            if (_lineRenderer == null)
            {
                _lineRenderer = gameObject.AddComponent<LineRenderer>();
            }

            _lineRenderer.positionCount = SegmentCount + 1;
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.widthMultiplier = 0.05f;
            _lineRenderer.numCornerVertices = 8;
            _lineRenderer.startWidth = 0.05f;
            _lineRenderer.endWidth = 0.05f;
            _lineRenderer.startColor = Color.white;
            _lineRenderer.endColor = Color.white;
            _lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        }

        private void Update()
        {
            Camera camera = targetCamera != null ? targetCamera : Camera.main;
            if (camera == null || brushToolbar == null)
            {
                return;
            }

            Vector2 worldPos = camera.ScreenToWorldPoint(Input.mousePosition);
            float radius = brushToolbar.Radius;
            BrushMode mode = brushToolbar.Mode;

            Color color = GetModeColor(mode, alpha: 0.8f);
            Vector3 center = new Vector3(worldPos.x, worldPos.y, 0f);

            // Build ring vertices
            Vector3[] positions = new Vector3[SegmentCount + 1];
            for (int i = 0; i <= SegmentCount; i++)
            {
                float angle = (i / (float)SegmentCount) * Mathf.PI * 2f;
                positions[i] = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y + Mathf.Sin(angle) * radius,
                    center.z
                );
            }

            _lineRenderer.SetPositions(positions);
            _lineRenderer.startColor = color;
            _lineRenderer.endColor = color;
            _lineRenderer.enabled = true;
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
    }
}
