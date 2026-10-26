namespace JustVID;
public readonly record struct Region(double X, double Y, double Width, double Height);
public static class ZoomMath
{
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
