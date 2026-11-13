using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NargTrophy.Banners;

/// <summary>
/// Builds the icons at startup: the trophy icon on a cloth in the banner's tint (docs/05, "trophy icon inside a
/// banner-shaped frame"). Two frames, so the source reads at a glance, on the HUD too:
/// - Trophy banner: a crossbar with the cloth hanging from it.
/// - War standard: the same on a pole with a finial.
/// Drawn once per banner on the CPU; the trophy sprite is copied through a RenderTexture because game textures
/// aren't readable. Falls back to the plain trophy icon when there's no GPU (dedicated server) or on any error.
/// </summary>
internal static class BannerIcons
{
    private const int Size = 128;

    private static readonly Color32 Wood = new(92, 62, 36, 255);
    private static readonly Color32 Finial = new(200, 160, 74, 255);
    private static readonly Color32 Outline = new(14, 10, 8, 230);
    private static readonly Color DefaultCloth = new(0.42f, 0.35f, 0.27f);

    public static Sprite? Banner(Sprite? trophy, Color? tint) => Make(trophy, tint, standard: false);
    public static Sprite? Standard(Sprite? trophy, Color? tint) => Make(trophy, tint, standard: true);

    private static Sprite? Make(Sprite? trophy, Color? tint, bool standard)
    {
        if (trophy == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return trophy;
        try
        {
            Shape shape = standard ? Shape.ForStandard : Shape.ForBanner;
            var pixels = new Color32[Size * Size];
            Color cloth = tint ?? DefaultCloth;
            Color32 clothColour = cloth;
            Color32 clothEdge = cloth * 0.7f;

            // Shape (y = 0 at the top while drawing; flipped when written).
            var mask = new bool[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                Color32? colour = shape.Paint(x, y, clothColour, clothEdge);
                if (colour == null) continue;
                pixels[Index(x, y)] = colour.Value;
                mask[y * Size + x] = true;
            }

            // Dark outline around the whole shape, for contrast on any background.
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (mask[y * Size + x] || !NearMask(mask, x, y, 2)) continue;
                pixels[Index(x, y)] = Outline;
            }

            // Trophy on the cloth.
            Color32[] trophyPixels = ReadSprite(trophy, shape.TrophySize);
            int left = shape.TrophyCentreX - shape.TrophySize / 2;
            int top = shape.TrophyCentreY - shape.TrophySize / 2;
            for (int ty = 0; ty < shape.TrophySize; ty++)
            for (int tx = 0; tx < shape.TrophySize; tx++)
            {
                // trophyPixels is bottom-up, like any Texture2D.
                Color32 src = trophyPixels[(shape.TrophySize - 1 - ty) * shape.TrophySize + tx];
                if (src.a == 0) continue;
                int i = Index(left + tx, top + ty);
                pixels[i] = Blend(pixels[i], src);
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not build a banner icon ({e.Message}). Using the trophy icon.");
            return trophy;
        }
    }

    /// <summary>The sprite drawn into a square of <paramref name="size"/> pixels, keeping its aspect. Bottom-up.</summary>
    private static Color32[] ReadSprite(Sprite sprite, int size)
    {
        Texture source = sprite.texture;
        Rect rect = sprite.textureRect;
        var uv = new Rect(rect.x / source.width, rect.y / source.height, rect.width / source.width, rect.height / source.height);
        float aspect = rect.width / rect.height;
        float w = aspect >= 1f ? size : size * aspect;
        float h = aspect >= 1f ? size / aspect : size;

        RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, size, size, 0);
            Graphics.DrawTexture(new Rect((size - w) / 2f, (size - h) / 2f, w, h), source, uv, 0, 0, 0, 0);
            GL.PopMatrix();

            var read = new Texture2D(size, size, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            read.Apply();
            Color32[] pixels = read.GetPixels32();
            UnityEngine.Object.Destroy(read);
            return pixels;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private static int Index(int x, int yFromTop) => (Size - 1 - yFromTop) * Size + x;

    private static bool NearMask(bool[] mask, int x, int y, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) continue;
            if (mask[ny * Size + nx]) return true;
        }
        return false;
    }

    private static Color32 Blend(Color32 under, Color32 over)
    {
        float a = over.a / 255f;
        float outA = a + under.a / 255f * (1f - a);
        if (outA <= 0f) return default;
        byte Mix(byte o, byte u) => (byte)Mathf.Clamp(Mathf.RoundToInt((o * a + u * (under.a / 255f) * (1f - a)) / outA), 0, 255);
        return new Color32(Mix(over.r, under.r), Mix(over.g, under.g), Mix(over.b, under.b), (byte)Mathf.RoundToInt(outA * 255f));
    }

    /// <summary>Frame geometry on the 128 px canvas, y measured from the top.</summary>
    private sealed class Shape
    {
        public static readonly Shape ForBanner = new()
        {
            BarLeft = 12, BarRight = 116, BarTop = 8, BarBottom = 16,
            ClothLeft = 24, ClothRight = 104, ClothTop = 16, ClothBottom = 98, TipY = 122,
            TrophyCentreX = 64, TrophyCentreY = 56, TrophySize = 66,
        };

        public static readonly Shape ForStandard = new()
        {
            HasPole = true, PoleLeft = 14, PoleRight = 21, PoleTop = 6, PoleBottom = 126,
            BarLeft = 14, BarRight = 116, BarTop = 12, BarBottom = 19,
            ClothLeft = 32, ClothRight = 108, ClothTop = 19, ClothBottom = 96, TipY = 118,
            TrophyCentreX = 70, TrophyCentreY = 56, TrophySize = 60,
        };

        public bool HasPole;
        public int PoleLeft, PoleRight, PoleTop, PoleBottom;
        public int BarLeft, BarRight, BarTop, BarBottom;
        public int ClothLeft, ClothRight, ClothTop, ClothBottom, TipY;
        public int TrophyCentreX, TrophyCentreY, TrophySize;

        private const int EdgeWidth = 4;

        public Color32? Paint(int x, int y, Color32 cloth, Color32 edge)
        {
            if (HasPole)
            {
                float fx = x - (PoleLeft + PoleRight) / 2f, fy = y - PoleTop;
                if (fx * fx + fy * fy <= 25f) return Finial;
                if (x >= PoleLeft && x <= PoleRight && y >= PoleTop && y <= PoleBottom) return Wood;
            }
            if (x >= BarLeft && x <= BarRight && y >= BarTop && y <= BarBottom) return Wood;

            if (!InCloth(x, y)) return null;
            // A darker band just inside the cloth's edge.
            bool inner = InCloth(x - EdgeWidth, y) && InCloth(x + EdgeWidth, y) && InCloth(x, y + EdgeWidth);
            return inner ? cloth : edge;
        }

        private bool InCloth(int x, int y)
        {
            if (x < ClothLeft || x > ClothRight || y < ClothTop) return false;
            if (y <= ClothBottom) return true;
            if (y > TipY) return false;
            // Pointed bottom: the cloth narrows to a tip at the centre.
            float centre = (ClothLeft + ClothRight) / 2f;
            float half = (ClothRight - ClothLeft) / 2f * (1f - (y - ClothBottom) / (float)(TipY - ClothBottom));
            return Mathf.Abs(x - centre) <= half;
        }
    }
}
