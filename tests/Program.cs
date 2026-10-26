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
Check(ZoomMath.Pixels(new Region(.5, .5, .00001, .00001), 1920, 1080) == (960, 540, 1, 1));
Check(ZoomMath.Pixels(full, 1920, 1080) == (0, 0, 1920, 1080));
Check(ZoomMath.Pixels(new Region(.9999, .9999, .5, .5), 1920, 1080) == (1919, 1079, 1, 1));
Console.WriteLine("Zoom geometry checks passed.");

Check(ZoomMath.CropGeometry(full, 320, 180) == "");
Check(ZoomMath.CropGeometry(new Region(.25, .25, .5, .5), 320, 180) == "80+45+80+45");
Check(ZoomMath.CropGeometry(new Region(.5, .1, .1, .8), 320, 180) == "160+18+128+18");
Check(ZoomMath.CropGeometry(new Region(.1, .5, .8, .1), 320, 180) == "32+90+32+72");
