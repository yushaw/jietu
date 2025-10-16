namespace SnapDescribe.App.Models;

public sealed class OcrSegment
{
    public OcrSegment(int index, string text, OcrBoundingBox? bounds = null, double confidence = 0d)
    {
        Index = index;
        Text = text;
        Bounds = bounds;
        Confidence = confidence;
    }

    public int Index { get; }

    public string Text { get; }

    public OcrBoundingBox? Bounds { get; }

    public double Confidence { get; }
}
