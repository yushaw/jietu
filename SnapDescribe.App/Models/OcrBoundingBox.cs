namespace SnapDescribe.App.Models;

public readonly record struct OcrBoundingBox(int X, int Y, int Width, int Height)
{
    public static OcrBoundingBox FromPixels(int x, int y, int width, int height) => new(x, y, width, height);
}
