using UnityEngine;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Converts a raw <see cref="TerrainFieldSampler.Result"/> into a colored Texture2D for
    /// debugging: solid samples are blue, air samples are red, and a thin band around the true
    /// zero-crossing is drawn white so the raw field's surface is visible independent of any
    /// Marching Squares mesh. A pure function of its inputs, like the mesh/collider builders it
    /// sits alongside.
    /// </summary>
    public static class SDFDebugTexture
    {
        private static readonly Color SolidColor = new Color(0.2f, 0.4f, 1f, 1f);
        private static readonly Color AirColor = new Color(1f, 0.3f, 0.3f, 1f);
        private static readonly Color SurfaceColor = Color.white;

        /// <summary>
        /// Builds (or resizes/repaints, if <paramref name="existing"/> matches the sample grid's
        /// dimensions) a Texture2D visualizing the given sample grid.
        /// </summary>
        public static Texture2D Build(TerrainFieldSampler.Result sampled, Texture2D existing = null)
        {
            float[,] samples = sampled.Samples;
            int width = samples.GetLength(0);
            int height = samples.GetLength(1);

            Texture2D texture = existing;
            if (texture == null || texture.width != width || texture.height != height)
            {
                texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            // Surface band width in "sample value" units (samples are signed distances in world
            // units), sized to the grid's own cell size so it reads as ~1-2 pixels wide regardless
            // of resolution.
            float surfaceBandWidth = sampled.CellSize * 1.5f;

            var pixels = new Color32[width * height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    float value = samples[x, y];
                    Color color = Mathf.Abs(value) < surfaceBandWidth
                        ? SurfaceColor
                        : (value < 0f ? SolidColor : AirColor);

                    pixels[(y * width) + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return texture;
        }
    }
}
