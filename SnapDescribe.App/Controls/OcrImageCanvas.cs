using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Controls;

public class OcrImageCanvas : Control
{
    private Bitmap? _image;
    private List<OcrSegment> _segments = new();
    private OcrSegment? _hoveredSegment;
    private HashSet<OcrSegment> _selectedSegments = new();
    private Point? _selectionStart;
    private Rect? _selectionRect;
    private double _scale = 1.0;
    private Point _imageOffset;

    public static readonly StyledProperty<Bitmap?> ImageProperty =
        AvaloniaProperty.Register<OcrImageCanvas, Bitmap?>(nameof(Image));

    public static readonly StyledProperty<IEnumerable<OcrSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<OcrImageCanvas, IEnumerable<OcrSegment>?>(nameof(Segments));

    public Bitmap? Image
    {
        get => GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    public IEnumerable<OcrSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public event EventHandler<OcrSegment>? SegmentClicked;
    public event EventHandler<IReadOnlyList<OcrSegment>>? SelectionChanged;

    static OcrImageCanvas()
    {
        AffectsRender<OcrImageCanvas>(ImageProperty, SegmentsProperty);
        AffectsMeasure<OcrImageCanvas>(ImageProperty);
    }

    public OcrImageCanvas()
    {
        ClipToBounds = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ImageProperty)
        {
            _image = change.GetNewValue<Bitmap?>();
            InvalidateVisual();
        }
        else if (change.Property == SegmentsProperty)
        {
            var segments = change.GetNewValue<IEnumerable<OcrSegment>?>();
            _segments = segments?.ToList() ?? new List<OcrSegment>();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_image is null)
        {
            return new Size(200, 200);
        }

        return new Size(_image.PixelSize.Width, _image.PixelSize.Height);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var point = e.GetPosition(this);

        if (_selectionStart.HasValue)
        {
            // Drawing selection rectangle
            var start = _selectionStart.Value;
            var rect = new Rect(
                Math.Min(start.X, point.X),
                Math.Min(start.Y, point.Y),
                Math.Abs(point.X - start.X),
                Math.Abs(point.Y - start.Y)
            );
            _selectionRect = rect;
            InvalidateVisual();
            return;
        }

        // Check hover
        var imagePoint = ScreenToImage(point);
        var newHovered = FindSegmentAt(imagePoint);

        if (newHovered != _hoveredSegment)
        {
            _hoveredSegment = newHovered;
            InvalidateVisual();

            if (_hoveredSegment is not null)
            {
                Cursor = new Cursor(StandardCursorType.Hand);
                ToolTip.SetTip(this, _hoveredSegment.Text);
            }
            else
            {
                Cursor = Cursor.Default;
                ToolTip.SetTip(this, null);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;

        if (properties.IsLeftButtonPressed)
        {
            var imagePoint = ScreenToImage(point);
            var segment = FindSegmentAt(imagePoint);

            if (segment is not null)
            {
                // Single segment click
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    _selectedSegments.Clear();
                }

                if (_selectedSegments.Contains(segment))
                {
                    _selectedSegments.Remove(segment);
                }
                else
                {
                    _selectedSegments.Add(segment);
                }

                SegmentClicked?.Invoke(this, segment);
                SelectionChanged?.Invoke(this, _selectedSegments.ToList());
                InvalidateVisual();
            }
            else
            {
                // Start drag selection
                _selectionStart = point;
                _selectionRect = null;
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    _selectedSegments.Clear();
                }
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_selectionStart.HasValue && _selectionRect.HasValue)
        {
            // Complete drag selection
            var selectedByRect = _segments.Where(seg =>
            {
                if (seg.Bounds is null) return false;
                var segRect = ImageToScreen(new Rect(seg.Bounds.Value.X, seg.Bounds.Value.Y, seg.Bounds.Value.Width, seg.Bounds.Value.Height));
                return _selectionRect.Value.Intersects(segRect);
            }).ToList();

            foreach (var seg in selectedByRect)
            {
                if (!_selectedSegments.Contains(seg))
                {
                    _selectedSegments.Add(seg);
                }
            }

            if (_selectedSegments.Count > 0)
            {
                SelectionChanged?.Invoke(this, _selectedSegments.ToList());
            }
        }

        _selectionStart = null;
        _selectionRect = null;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoveredSegment = null;
        Cursor = Cursor.Default;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_image is null)
        {
            return;
        }

        // Calculate scaling to fit
        var availableSize = Bounds.Size;
        var imageSize = new Size(_image.PixelSize.Width, _image.PixelSize.Height);
        _scale = Math.Min(availableSize.Width / imageSize.Width, availableSize.Height / imageSize.Height);
        var scaledSize = new Size(imageSize.Width * _scale, imageSize.Height * _scale);
        _imageOffset = new Point(
            (availableSize.Width - scaledSize.Width) / 2,
            (availableSize.Height - scaledSize.Height) / 2
        );

        // Draw image
        var imageRect = new Rect(_imageOffset, scaledSize);
        context.DrawImage(_image, imageRect);

        // Draw bounding boxes
        foreach (var segment in _segments)
        {
            if (segment.Bounds is null) continue;

            var bounds = segment.Bounds.Value;
            var rect = ImageToScreen(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));

            var isHovered = segment == _hoveredSegment;
            var isSelected = _selectedSegments.Contains(segment);

            if (isSelected)
            {
                // Selected: blue semi-transparent fill + solid border
                var fillBrush = new SolidColorBrush(Color.FromArgb(60, 59, 130, 246));
                var borderBrush = new SolidColorBrush(Color.FromArgb(255, 59, 130, 246));
                context.FillRectangle(fillBrush, rect);
                context.DrawRectangle(new Pen(borderBrush, 2), rect);
            }
            else if (isHovered)
            {
                // Hovered: yellow semi-transparent fill + border
                var fillBrush = new SolidColorBrush(Color.FromArgb(40, 251, 191, 36));
                var borderBrush = new SolidColorBrush(Color.FromArgb(200, 251, 191, 36));
                context.FillRectangle(fillBrush, rect);
                context.DrawRectangle(new Pen(borderBrush, 1.5), rect);
            }
            else
            {
                // Default: subtle border
                var borderBrush = new SolidColorBrush(Color.FromArgb(80, 100, 116, 139));
                context.DrawRectangle(new Pen(borderBrush, 1), rect);
            }
        }

        // Draw selection rectangle
        if (_selectionRect.HasValue)
        {
            var fillBrush = new SolidColorBrush(Color.FromArgb(30, 59, 130, 246));
            var borderBrush = new SolidColorBrush(Color.FromArgb(150, 59, 130, 246));
            context.FillRectangle(fillBrush, _selectionRect.Value);
            context.DrawRectangle(new Pen(borderBrush, 1, DashStyle.Dash), _selectionRect.Value);
        }
    }

    private OcrSegment? FindSegmentAt(Point imagePoint)
    {
        if (_image is null) return null;

        foreach (var segment in _segments)
        {
            if (segment.Bounds is null) continue;

            var bounds = segment.Bounds.Value;
            var rect = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            if (rect.Contains(imagePoint))
            {
                return segment;
            }
        }

        return null;
    }

    private Point ScreenToImage(Point screenPoint)
    {
        if (_image is null) return screenPoint;

        var x = (screenPoint.X - _imageOffset.X) / _scale;
        var y = (screenPoint.Y - _imageOffset.Y) / _scale;
        return new Point(x, y);
    }

    private Rect ImageToScreen(Rect imageRect)
    {
        var x = imageRect.X * _scale + _imageOffset.X;
        var y = imageRect.Y * _scale + _imageOffset.Y;
        var width = imageRect.Width * _scale;
        var height = imageRect.Height * _scale;
        return new Rect(x, y, width, height);
    }

    public void ClearSelection()
    {
        _selectedSegments.Clear();
        InvalidateVisual();
    }

    public IReadOnlyList<OcrSegment> GetSelectedSegments()
    {
        return _selectedSegments.ToList();
    }

    public void HighlightSegment(OcrSegment segment)
    {
        _selectedSegments.Clear();
        if (segment is not null && _segments.Contains(segment))
        {
            _selectedSegments.Add(segment);
        }
        InvalidateVisual();
    }

    public void SelectAll()
    {
        _selectedSegments.Clear();
        foreach (var seg in _segments)
        {
            _selectedSegments.Add(seg);
        }
        SelectionChanged?.Invoke(this, _selectedSegments.ToList());
        InvalidateVisual();
    }
}
