using JustVID;
static void Check(bool condition) { if (!condition) throw new Exception("Zoom regression"); }
var full = new Region(0, 0, 1, 1);
var crop = ZoomMath.Select(full, 75, 75, 25, 25, 100, 100);
Check(crop == new Region(.25, .25, .5, .5));
var tiny = ZoomMath.Select(full, 50, 50, 51, 51, 100, 100);
Check(Math.Abs(tiny.Width - .01) < 1e-9);
var nested = ZoomMath.Select(crop, 0, 0, 50, 50, 100, 100);
Check(nested == new Region(.25, .25, .25, .25));
var clamped = ZoomMath.Select(full, -20, -10, 200, 120, 100, 100);
Check(clamped == full);
Console.WriteLine("Zoom geometry checks passed.");
