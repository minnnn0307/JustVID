namespace JustVID;
public readonly record struct Region(double X, double Y, double Width, double Height);
public static class ZoomMath
{
    // Native codec cropping uses integral source pixels, including 1-pixel crops.
    public static (int X, int Y, int Width, int Height) Pixels(Region crop, int width, int height)
    {
        int x = Math.Clamp((int)Math.Floor(crop.X * width), 0, width - 1);
        int y = Math.Clamp((int)Math.Floor(crop.Y * height), 0, height - 1);
        int w = Math.Clamp((int)Math.Ceiling(crop.Width * width), 1, width - x);
        int h = Math.Clamp((int)Math.Ceiling(crop.Height * height), 1, height - y);
        return (x, y, w, h);
    }
    // VLC 3's WxH+X+Y path treats W/H as absolute right/bottom in its display
    // implementation. Border counts avoid that ambiguity for off-center crops.
    public static string CropGeometry(Region crop, int width, int height)
    {
        var p = Pixels(crop, width, height);
        if (p == (0, 0, width, height)) return "";
        return $"{p.X}+{p.Y}+{width - p.X - p.Width}+{height - p.Y - p.Height}";
    }
    // Maps a selection within the displayed viewport into the current source crop.
    public static Region Select(Region current, double x1, double y1, double x2, double y2, double width, double height)
    {
        var x = Math.Clamp(Math.Min(x1, x2) / width, 0, 1);
        var y = Math.Clamp(Math.Min(y1, y2) / height, 0, 1);
        var w = Math.Min(1 - x, Math.Max(Math.Abs(x2 - x1) / width, 0.000001));
        var h = Math.Min(1 - y, Math.Max(Math.Abs(y2 - y1) / height, 0.000001));
        return new(current.X + x * current.Width, current.Y + y * current.Height, w * current.Width, h * current.Height);
    }
}
