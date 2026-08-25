using System.Collections.Generic;
using UnityEngine;
using SDFTerrain.Materials;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Gizmo-based diagnostic that visualizes the geological layer materials along
    /// cross-section lines through the planet. Drawn in the Scene view so you can
    /// compare the material colors against the rendered terrain mesh side by side.
    ///
    /// Also logs a structured table to the Console on Start so you can see depth,
    /// SDF, noise, and material at every sample point.
    /// </summary>
    public class GeologicalLayerDiagnostic : MonoBehaviour
    {
        [Tooltip("Spacing between sample points along each axis (world units)")]
        [SerializeField] private float sampleSpacing = 0.5f;

        [Tooltip("Draw Gizmo spheres at each sample point colored by material")]
        [SerializeField] private bool drawSampleSpheres = true;

        [Tooltip("Radius of each sample sphere")]
        [SerializeField] private float sphereRadius = 0.25f;

        [Tooltip("Draw layer boundary markers where the material changes")]
        [SerializeField] private bool drawBoundaries = true;

        private MaterialLayer _materialLayer;
        private GeologicalProfile _profile;
        private TerrainField _field;
        private float _planetRadius;
        private MaterialDatabase _database;

        public void Initialize(TerrainField field, MaterialLayer materialLayer, GeologicalProfile profile, float planetRadius)
        {
            _field = field;
            _materialLayer = materialLayer;
            _profile = profile;
            _planetRadius = planetRadius;
            _database = materialLayer.GetDatabase();

            LogRadialScan();
        }

        private void LogRadialScan()
        {
            if (_field == null || _materialLayer == null) return;

            // Scan along the vertical axis (Y) from above the surface through the center.
            var samples = ScanLine(new Vector2(0f, _planetRadius + 2f), Vector2.down, _planetRadius * 2f + 4f);
            LogScanTable("Vertical scan (X=0, top-to-center)", samples);

            // Scan along horizontal axis (X) from right edge through center.
            var hSamples = ScanLine(new Vector2(_planetRadius + 2f, 0f), Vector2.left, _planetRadius * 2f + 4f);
            LogScanTable("Horizontal scan (Y=0, right-to-center)", hSamples);
        }

        private List<ScanPoint> ScanLine(Vector2 origin, Vector2 direction, float totalLength)
        {
            var points = new List<ScanPoint>();
            float range = Mathf.Max(1f, _planetRadius + 2f);

            for (float d = 0f; d < totalLength; d += sampleSpacing)
            {
                Vector2 pos = origin + direction * d;
                points.Add(SampleAt(pos));
            }
            return points;
        }

        private ScanPoint SampleAt(Vector2 pos)
        {
            float editedSdf = _field.Sample(pos);

            if (editedSdf > 0f)
            {
                return new ScanPoint(pos, editedSdf, 0f, "air", Color.white);
            }

            // Use base (unedited) SDF for geological depth, so diagnostic shows the
            // natural strata even where the terrain has been modified by edits.
            float baseSdf = _field.SampleBase(pos);
            float depth = -baseSdf;
            MaterialSample sample = _materialLayer.Sample(_field, pos);
            Color color = MaterialColorMap.GetColor(sample.MaterialId, _database);
            string name = _database.HasMaterial(sample.MaterialId)
                ? _database.GetName(sample.MaterialId)
                : sample.MaterialId.ToString();

            return new ScanPoint(pos, baseSdf, depth, name, color);
        }

        private void LogScanTable(string title, List<ScanPoint> samples)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== {title} ===");

            string lastMaterial = null;
            int boundaryCount = 0;

            for (int i = 0; i < samples.Count; i++)
            {
                var p = samples[i];

                if (p.MaterialId != lastMaterial)
                {
                    if (lastMaterial != null)
                    {
                        sb.AppendLine($"  >>> BOUNDARY at depth {p.Depth:F1}: {lastMaterial} -> {p.MaterialId}");
                        boundaryCount++;
                    }
                    lastMaterial = p.MaterialId;
                }

                sb.AppendLine($"  pos=({p.Position.x:F1},{p.Position.y:F1}) depth={p.Depth:F1} sdf={p.Sdf:F2} material={p.MaterialId}");
            }

            sb.AppendLine($"  ({boundaryCount} boundaries found)");
            Debug.Log(sb.ToString());
        }

        private struct ScanPoint
        {
            public readonly Vector2 Position;
            public readonly float Sdf;
            public readonly float Depth;
            public readonly string MaterialId;
            public readonly Color Color;

            public ScanPoint(Vector2 position, float sdf, float depth, string materialId, Color color)
            {
                Position = position;
                Sdf = sdf;
                Depth = depth;
                MaterialId = materialId;
                Color = color;
            }
        }

        private void OnDrawGizmos()
        {
            if (_field == null || _materialLayer == null || _profile == null) return;

            float range = _planetRadius + 2f;

            // Draw vertical scan (X = 0)
            DrawScanLine(new Vector2(0f, range), Vector2.down, range * 2f + 4f, Color.green);

            // Draw horizontal scan (Y = 0)
            DrawScanLine(new Vector2(range, 0f), Vector2.left, range * 2f + 4f, Color.cyan);
        }

        private void DrawScanLine(Vector2 origin, Vector2 direction, float totalLength, Color lineColor)
        {
            var samples = new List<ScanPoint>();
            string lastMaterial = null;

            for (float d = 0f; d < totalLength; d += sampleSpacing)
            {
                Vector2 pos = origin + direction * d;
                var sample = SampleAt(pos);
                samples.Add(sample);

                if (sample.MaterialId != lastMaterial && lastMaterial != null)
                {
                    // Draw boundary marker
                    if (drawBoundaries)
                    {
                        Gizmos.color = Color.yellow;
                        Gizmos.DrawWireSphere(pos, sphereRadius * 2f);
                    }
                }
                lastMaterial = sample.MaterialId;

                if (drawSampleSpheres && sample.Depth > 0f)
                {
                    Gizmos.color = sample.Color;
                    Gizmos.DrawSphere(pos, sphereRadius);
                }
            }
        }
    }
}
