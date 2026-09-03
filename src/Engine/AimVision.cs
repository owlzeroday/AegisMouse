using AegisMouse.Core;

namespace AegisMouse.Engine;

/// <summary>Detected target: bounding box in capture-space pixels.</summary>
public sealed class AimTarget
{
    public int Left { get; init; }
    public int Top { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int CenterX => Left + Width / 2;
    public int CenterY => Top + Height / 2;
    public int Area => Width * Height;
    /// <summary>Crosshair aim point (head offset applied by the caller).</summary>
    public int AimY(int offsetPct) => Top + Height * offsetPct / 100;
}

/// <summary>
/// Pure-C# color-vision core: HSV threshold + scanline connected components.
/// No native dependencies — works on any pixel buffer (BGRA32).
/// </summary>
public static class AimVision
{
    /// <summary>
    /// Returns connected blobs matching the configured HSV range.
    /// Pixels are BGRA (B=byte0, G=byte1, R=byte2, A=byte3), row-major.
    /// </summary>
    public static List<AimTarget> Detect(ReadOnlySpan<byte> bgra, int width, int height, AimAssistSettings s)
    {
        var blobs = new List<AimTarget>();
        if (bgra.Length < width * height * 4 || width < 2 || height < 2) return blobs;

        // Two-pass scanline connected components with run-length encoding.
        // NOTE: union-find state lives in a dedicated label->parent map, NOT in the
        // per-run list. The old scheme stored parents in run entries (labels[lab-1]),
        // but multiple runs can share one label, so FindRoot could cycle 1->2->1 and
        // hang forever on adversarial/real screen content. This map is indexed by
        // label id, so cycles are impossible.
        var label = new int[width];
        Array.Clear(label);
        var labels = new List<(int x0, int x1, int y, int lab)>();
        var parent = new Dictionary<int, int>();
        int nextLabel = 0;

        for (int y = 0; y < height; y++)
        {
            var row = bgra.Slice(y * width * 4, width * 4);
            var cur = new int[width];

            int x = 0;
            while (x < width)
            {
                // find next run of matching pixels
                if (!InRange(row, x, s)) { x++; continue; }
                int x0 = x;
                while (x < width && InRange(row, x, s)) x++;
                int x1 = x - 1;

                // label the run: union with overlapping runs of the previous row
                int lab = 0;
                for (int i = Math.Max(0, x0 - 1); i <= Math.Min(width - 1, x1 + 1); i++)
                {
                    int p = label[i];
                    if (p != 0)
                    {
                        if (lab == 0) lab = p;
                        else if (lab != p) Union(parent, lab, p);
                    }
                }
                if (lab == 0) { lab = ++nextLabel; parent[lab] = lab; }
                for (int i = x0; i <= x1; i++) cur[i] = lab;
                labels.Add((x0, x1, y, lab));
            }

            label = cur;
        }

        // Collect bboxes per root label, skipping tiny noise blobs.
        var boxes = new Dictionary<int, (int l, int t, int r, int b, int count)>();

        for (int i = 0; i < labels.Count; i++)
        {
            var (x0, x1, y, lab) = labels[i];
            int root = FindRoot(parent, lab);
            if (!boxes.TryGetValue(root, out var b))
                b = (x0, y, x1, y, 0);
            b = (Math.Min(b.l, x0), Math.Min(b.t, y), Math.Max(b.r, x1), Math.Max(b.b, y), b.count + (x1 - x0 + 1));
            boxes[root] = b;
        }

        foreach (var b in boxes.Values)
        {
            int w = b.r - b.l + 1, h = b.b - b.t + 1;
            if (w < s.MinTargetSizePx || h < s.MinTargetSizePx) continue;
            blobs.Add(new AimTarget { Left = b.l, Top = b.t, Width = w, Height = h });
        }

        blobs.Sort((a, b2) => b2.Area.CompareTo(a.Area));
        return blobs;
    }

    /// <summary>
    /// Picks the target nearest to the crosshair (cx, cy) that lies within the FOV radius.
    /// Returns null when nothing qualifies.
    /// </summary>
    public static AimTarget? SelectTarget(List<AimTarget> targets, int cx, int cy, int fovRadius)
    {
        AimTarget? best = null;
        double bestDist = double.MaxValue;
        double fovSq = (long)fovRadius * fovRadius;
        foreach (var t in targets)
        {
            double dx = t.CenterX - cx, dy = t.CenterY - cy;
            double d2 = dx * dx + dy * dy;
            if (d2 > fovSq) continue;
            if (d2 < bestDist) { bestDist = d2; best = t; }
        }
        return best;
    }

    /// <summary>HSV of one pixel. Hue 0..179, S/V 0..255 (OpenCV convention).</summary>
    public static (int hue, int sat, int val) HsvOf(byte b, byte g, byte r)
    {
        byte max = Math.Max(r, Math.Max(g, b));
        byte min = Math.Min(r, Math.Min(g, b));
        byte d = (byte)(max - min);

        int hue;
        if (d == 0) hue = 0;
        else if (max == r) hue = (int)(((60.0 * ((g - b) / (double)d) % 360) + 360) % 360);
        else if (max == g) hue = (int)(60.0 * ((b - r) / (double)d + 2));
        else hue = (int)(60.0 * ((r - g) / (double)d + 4));
        return (hue / 2, max == 0 ? 0 : d * 255 / max, max);
    }

    static bool InRange(ReadOnlySpan<byte> bgra, int x, AimAssistSettings s)
    {
        int i = x * 4;
        var (hue, sat, val) = HsvOf(bgra[i], bgra[i + 1], bgra[i + 2]);
        if (hue < s.HueMin || hue > s.HueMax) return false;
        if (val < s.ValMin) return false;
        if (sat < s.SatMin) return false;
        return true;
    }

    // ── union-find over label ids (path compression; iterative, cycle-free) ──
    static void Union(Dictionary<int, int> parent, int a, int b)
    {
        int ra = FindRoot(parent, a), rb = FindRoot(parent, b);
        if (ra == rb) return;
        parent[ra] = rb;
    }

    static int FindRoot(Dictionary<int, int> parent, int lab)
    {
        // Path compression with a visited chain (bounded by label count).
        int root = lab;
        int guard = 0;
        while (parent.TryGetValue(root, out int p) && p != root && guard++ < parent.Count + 16)
            root = p;
        // Compress the original chain toward the root.
        int cur = lab;
        guard = 0;
        while (parent.TryGetValue(cur, out int pc) && pc != root && guard++ < parent.Count + 16)
        {
            parent[cur] = root;
            cur = pc;
        }
        return root;
    }
}
