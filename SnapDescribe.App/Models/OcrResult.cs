using System;
using System.Collections.Generic;
using System.Linq;

namespace SnapDescribe.App.Models;

public sealed class OcrResult
{
    public OcrResult(IReadOnlyList<OcrSegment> segments, string languages)
    {
        Segments = segments ?? throw new ArgumentNullException(nameof(segments));
        Languages = languages ?? string.Empty;
        PlainText = string.Join(Environment.NewLine + Environment.NewLine, Segments.Select(segment => segment.Text));
    }

    public IReadOnlyList<OcrSegment> Segments { get; }

    public string PlainText { get; }

    public string Languages { get; }
}
