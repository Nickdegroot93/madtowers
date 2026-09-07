using System.Collections.Generic;
using UnityEngine;

/// <summary>One silhouette family for HUD marks. Rasterized once at 160 px with analytic
/// edge coverage; chapter tint is applied by the Image. No per-frame generation.</summary>
public static class HudGlyphs
{
    public enum Mark { Blocks, Height, Flood, Airtight, Void, Puzzle, Heart, EmptyHeart }
    private static readonly Dictionary<Mark, Sprite> Cache = new Dictionary<Mark, Sprite>();
    public static Mark ForLevel(LevelDefinition level)
    {
        if (level != null && level.Modifiers != null)
            foreach (var modifier in level.Modifiers)
            {
                if (modifier is HeightLimitWavesModifier) return Mark.Puzzle;
                if (modifier is RisingFloodModifier) return Mark.Flood;
                if (modifier is AirPocketModifier) return Mark.Airtight;
                if (modifier is VoidZoneModifier) return Mark.Void;
            }
        if (level != null && level.TargetType == LevelTargetType.ClearWaves) return Mark.Puzzle;
        return level != null && (level.TargetType == LevelTargetType.ReachHeight || level.TargetType == LevelTargetType.TimedReachHeight)
            ? Mark.Height : Mark.Blocks;
    }
    public static Sprite Get(Mark mark)
    {
        if (Cache.TryGetValue(mark, out var sprite) && sprite != null) return sprite;
        const int size = 160;
        var pixels = new Color32[size * size];
        var heart = new List<Vector2>();
        if (mark == Mark.Heart || mark == Mark.EmptyHeart)
        {
            // Cubic outline: broad shoulders, a deep readable cleft, softly pointed base.
            Curve(heart, new Vector2(0,-.78f), new Vector2(-.15f,-.61f), new Vector2(-.87f,-.14f), new Vector2(-.82f,.35f));
            Curve(heart, new Vector2(-.82f,.35f), new Vector2(-.76f,.82f), new Vector2(-.25f,.91f), new Vector2(0,.49f));
            Curve(heart, new Vector2(0,.49f), new Vector2(.25f,.91f), new Vector2(.76f,.82f), new Vector2(.82f,.35f));
            Curve(heart, new Vector2(.82f,.35f), new Vector2(.87f,-.14f), new Vector2(.15f,-.61f), new Vector2(0,-.78f));
        }
        var mountain = new[] { new Vector2(-.88f,-.58f), new Vector2(-.18f,.78f), new Vector2(.58f,-.58f) };
        var peak = new[] { new Vector2(-.18f,-.58f), new Vector2(.40f,.40f), new Vector2(.90f,-.58f) };
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            var p = new Vector2((x + .5f) / size * 2 - 1, (y + .5f) / size * 2 - 1);
            float d;
            switch (mark)
            {
                case Mark.Heart: case Mark.EmptyHeart:
                    d = Polygon(p, heart);
                    if (mark == Mark.EmptyHeart) d = Mathf.Max(d, -d - .075f);
                    break;
                case Mark.Height: case Mark.Flood:
                    d = Mathf.Min(Polygon(p, mountain), Polygon(p, peak));
                    if (mark == Mark.Flood)
                    {
                        d = Mathf.Max(d, -.15f - p.y);
                        for (int row = 0; row < 2; row++)
                            d = Mathf.Min(d, Mathf.Max(Mathf.Abs(p.y + .32f + row * .28f - .055f * Mathf.Sin(p.x * 8)) - .065f, Mathf.Abs(p.x) - .87f));
                    }
                    break;
                case Mark.Void:
                    float angle = Mathf.Atan2(p.y, p.x), r = p.magnitude;
                    float rim = .61f + .075f * Mathf.Sin(angle * 3) + .045f * Mathf.Sin(angle * 7);
                    d = Mathf.Abs(r - rim) - .105f;
                    d = Mathf.Min(d, (p - new Vector2(.06f,.03f)).magnitude - .17f);
                    break;
                case Mark.Airtight:
                    d = Mathf.Abs(Box(p, Vector2.zero, new Vector2(.72f,.69f))) - .085f;
                    d = Mathf.Min(d, (p - new Vector2(-.18f,-.14f)).magnitude - .17f);
                    d = Mathf.Min(d, (p - new Vector2(.23f,.23f)).magnitude - .12f);
                    break;
                case Mark.Puzzle:
                    d = Mathf.Min(Box(p,new Vector2(-.41f,-.4f),new Vector2(.32f,.33f)),
                        Mathf.Min(Box(p,new Vector2(.31f,-.4f),new Vector2(.32f,.33f)), Box(p,new Vector2(.31f,.33f),new Vector2(.32f,.33f))));
                    break;
                default:
                    d = Mathf.Min(Box(p,new Vector2(-.42f,-.38f),new Vector2(.34f,.30f)),
                        Mathf.Min(Box(p,new Vector2(.39f,-.38f),new Vector2(.34f,.30f)), Box(p,new Vector2(0,.34f),new Vector2(.34f,.30f))));
                    break;
            }
            byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(.5f - d * size * .5f) * 255);
            pixels[y * size + x] = new Color32(255,255,255,alpha);
        }
        var texture = new Texture2D(size,size,TextureFormat.RGBA32,false) { name = "HUD " + mark, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(pixels); texture.Apply(false,true);
        sprite = Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),160);
        sprite.name = "HUD " + mark; sprite.hideFlags = HideFlags.HideAndDontSave;
        Cache[mark] = sprite; return sprite;
    }
    private static float Box(Vector2 p, Vector2 center, Vector2 half)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x-center.x),Mathf.Abs(p.y-center.y)) - half;
        return new Vector2(Mathf.Max(q.x,0),Mathf.Max(q.y,0)).magnitude + Mathf.Min(Mathf.Max(q.x,q.y),0);
    }
    private static float Polygon(Vector2 p, IList<Vector2> points)
    {
        float distance = float.MaxValue; bool inside = false;
        for (int i=0,j=points.Count-1;i<points.Count;j=i++)
        {
            Vector2 a=points[j], b=points[i], e=b-a;
            distance=Mathf.Min(distance,(p-a-e*Mathf.Clamp01(Vector2.Dot(p-a,e)/Mathf.Max(e.sqrMagnitude,.00001f))).sqrMagnitude);
            if ((a.y>p.y)!=(b.y>p.y) && p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x) inside=!inside;
        }
        return Mathf.Sqrt(distance)*(inside?-1:1);
    }
    private static void Curve(List<Vector2> points, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        for(int i=0;i<20;i++) { float t=i/20f,u=1-t;points.Add(u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d); }
    }
}
