// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace PrimalEditor.Editors;

class PointsToGeometryConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3 ||
            values[0] is not IEnumerable<Point> rawPoints ||
            values[2] is not MatrixTransform transform)
        {
            return Geometry.Empty;
        }

        using var enumerator = rawPoints.GetEnumerator();
        if (!enumerator.MoveNext())
            return Geometry.Empty;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var first = new Point(enumerator.Current.X, enumerator.Current.Y);
            ctx.BeginFigure(first, false, false);

            var hasMore = false;
            while (enumerator.MoveNext())
            {
                hasMore = true;
                ctx.LineTo(new(enumerator.Current.X, enumerator.Current.Y), true, false);
            }

            if (!hasMore)
                ctx.LineTo(first, true, false);
        }

        // Attached once; mutating transform.Matrix updates the GPU/MIL transform
        // without re-running this converter or rebuilding the geometry.
        geometry.Transform = transform;
        return geometry;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

static class ChartMath
{
    public static double Snap(double value) => Math.Round(value) + 0.5;

    /// <summary>
    /// Returns 10 ticks when zoomed in (ScaleX >= 5 px/tick), and scales in
    /// multiples of 10 ticks when zoomed out so vertical lines stay ~50 px apart.
    /// </summary>
    public static int GetHorizontalTickStep(double scaleX, double targetPixelSpacing = 50.0)
    {
        if (scaleX <= 0 || double.IsNaN(scaleX) || double.IsInfinity(scaleX))
            return 10;

        var rawTicks = targetPixelSpacing / scaleX;
        var multipleOfTen = (int)Math.Round(rawTicks / 10.0, MidpointRounding.AwayFromZero) * 10;
        return Math.Max(10, multipleOfTen);
    }

    /// <summary>
    /// Computes a clean 1-2-5 step size in data units so horizontal grid lines
    /// stay spaced at roughly <paramref name="targetPixelSpacing"/> pixels.
    /// </summary>
    public static double GetNiceStep(double scaleY, double targetPixelSpacing = 50.0)
    {
        if (scaleY <= 0 || double.IsNaN(scaleY) || double.IsInfinity(scaleY))
            return 1.0;

        var rawStep = targetPixelSpacing / scaleY;
        var exponent = Math.Floor(Math.Log10(rawStep));
        var baseVal = Math.Pow(10, exponent);
        var fraction = rawStep / baseVal;

        var niceFraction = fraction < 1.4142 ? 1.0      // sqrt(1 * 2) ~ 1.4142 
                            : fraction < 3.1623 ? 2.0   // sqrt(2 * 5) ~ 3.1623
                            : fraction < 7.0711 ? 5.0   // sqrt(5 * 10) ~ 7.0711
                            : 10.0;

        return niceFraction * baseVal;
    }
}

class PlotGridElement : FrameworkElement
{
    public double ScaleX { get; set; } = 5.0;
    public double ScaleY { get; set; } = 1.0;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double TargetVerticalGridSpacing { get; set; } = 50.0;
    public double TargetHorizontalGridSpacing { get; set; } = 50.0;
    public Pen GridPen { get; set; } = new(new SolidColorBrush(Colors.DimGray), 1.0);

    public Brush GridBrush
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                GridPen = new(field, 1.0);
            }
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0 || ScaleX <= 0 || ScaleY <= 0) return;

        // Vertical grid lines: 10-tick intervals when zoomed in,
        // multiples of 10 (~50px apart) when zoomed out
        var majorStep = ChartMath.GetHorizontalTickStep(ScaleX, TargetVerticalGridSpacing);
        var firstTick = (int)Math.Floor(-OffsetX / ScaleX);
        var lastTick = (int)Math.Ceiling((w - OffsetX) / ScaleX);
        var startMajor = firstTick - (((firstTick % majorStep) + majorStep) % majorStep);

        for (int tick = startMajor; tick <= lastTick; tick += majorStep)
        {
            var sx = ChartMath.Snap(OffsetX + tick * ScaleX);
            if (sx >= 0 && sx <= w)
                dc.DrawLine(GridPen, new(sx, 0), new(sx, h));
        }

        // Horizontal grid lines every ~50 pixels
        var stepY = ChartMath.GetNiceStep(ScaleY, TargetHorizontalGridSpacing);
        var yMax = OffsetY / ScaleY;
        var yMin = (OffsetY - h) / ScaleY;

        var firstIdx = (long)Math.Floor(yMin / stepY);
        var lastIdx = (long)Math.Ceiling(yMax / stepY);

        for (long i = firstIdx; i <= lastIdx; i++)
        {
            var sy = ChartMath.Snap(OffsetY - (i * stepY) * ScaleY);
            if (sy >= 0 && sy <= h)
                dc.DrawLine(GridPen, new(0, sy), new(w, sy));
        }
    }
}

class CrosshairElement : FrameworkElement
{
    public double ScaleX { get; set; } = 5.0;
    public double OffsetX { get; set; }
    public Point MousePosition { get; set; }
    public Pen GridPen { get; set; } = new(new SolidColorBrush(Colors.Moccasin) { Opacity = 0.5 }, 1.0) { DashStyle = DashStyles.Dash };

    public Brush GridBrush
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                GridPen = new(field, 1.0) { DashStyle = DashStyles.Dash };
            }
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0 || ScaleX <= 0) return;

        var nearestTick = (long)Math.Round((MousePosition.X - OffsetX) / ScaleX);
        var sx = OffsetX + (nearestTick * ScaleX);

        if (sx >= 0 && sx <= w)
            dc.DrawLine(GridPen, new(sx, 0), new(sx, h));

        var sy = MousePosition.Y;
        if (sy >= 0 && sy <= h)
            dc.DrawLine(GridPen, new(0, sy), new(w, sy));
    }
}

class XAxisElement : FrameworkElement
{
    public double ScaleX { get; set; } = 5.0;
    public double OffsetX { get; set; }
    public double TargetVerticalGridSpacing { get; set; } = 50.0;
    public Pen TickPen { get; set; } = new(Brushes.DimGray, 1.0);
    public Brush TextBrush { get; set; } = Brushes.DimGray;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0 || ScaleX <= 0) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");

        // Top axis baseline
        //dc.DrawLine(TickPen, new(0, 0.5), new(w, 0.5));

        var majorStep = ChartMath.GetHorizontalTickStep(ScaleX, TargetVerticalGridSpacing);
        var firstTick = (int)Math.Floor(-OffsetX / ScaleX);
        var lastTick = (int)Math.Ceiling((w - OffsetX) / ScaleX);

        for (int tick = firstTick; tick <= lastTick; tick++)
        {
            var sx = ChartMath.Snap(OffsetX + tick * ScaleX);
            if (sx < 0 || sx > w) continue;

            var isMajor = ((tick % majorStep) + majorStep) % majorStep == 0;

            if (isMajor)
            {
                // Large tick + numeric label aligned with vertical grid lines
                dc.DrawLine(TickPen, new(sx, 0), new(sx, 10));

                var text = new FormattedText(
                    tick.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    11,
                    TextBrush,
                    dpi);

                dc.DrawText(text, new(sx - text.Width / 2.0, 12));
            }
            else if (ScaleX >= 2.0)
            {
                // Small tick every integer unit when individual ticks are distinguishable
                dc.DrawLine(TickPen, new(sx, 0), new(sx, 5));
            }
            else if (((tick % 10) + 10) % 10 == 0)
            {
                // Small tick every 10 units when zoomed out between 1.0 and 2.0 px/tick
                dc.DrawLine(TickPen, new(sx, 0), new(sx, 5));
            }
        }
    }
}

public class YAxisElement : FrameworkElement
{
    public double ScaleY { get; set; } = 1.0;
    public double OffsetY { get; set; }
    public double TargetHorizontalGridSpacing { get; set; } = 50.0;
    public Pen TickPen { get; set; } = new(Brushes.DimGray, 1.0);
    public Brush TextBrush { get; set; } = Brushes.DimGray;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0 || ScaleY <= 0) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");

        // Right axis baseline
        //var axisX = ChartMath.Snap(w - 1);
        //dc.DrawLine(TickPen, new(axisX, 0), new(axisX, h));

        var stepY = ChartMath.GetNiceStep(ScaleY, TargetHorizontalGridSpacing);
        var yMax = OffsetY / ScaleY;
        var yMin = (OffsetY - h) / ScaleY;

        var firstIdx = (long)Math.Floor(yMin / stepY);
        var lastIdx = (long)Math.Ceiling(yMax / stepY);

        for (long i = firstIdx; i <= lastIdx; i++)
        {
            var val = Math.Round(i * stepY, 10);
            var sy = ChartMath.Snap(OffsetY - val * ScaleY);
            if (sy < 0 || sy > h) continue;

            dc.DrawLine(TickPen, new(w - 6, sy), new(w, sy));

            var text = new FormattedText(
                val.ToString("G6", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                11,
                TextBrush,
                dpi);

            dc.DrawText(text, new(w - 9 - text.Width, sy - text.Height / 2.0));
        }
    }
}

// NOTE: we could also inherit from ViewModeBase, but I choose not to, just to keep
//       the control separated from the editor's code.
class ChartSeries : INotifyPropertyChanged
{
    public string Name
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    } = string.Empty;

    public Brush Stroke
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    } = Brushes.DodgerBlue;

    public double StrokeThickness
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    } = 1.0;

    public ObservableCollection<Point> Points
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field?.CollectionChanged -= OnPointsCollectionChanged;

            field = value ?? [];
            field.CollectionChanged += OnPointsCollectionChanged;

            OnPropertyChanged();
            OnPropertyChanged(nameof(PointsRevision));
        }
    } = [];

    // Increments whenever items inside Points are added/removed/reset
    public int PointsRevision { get; private set; }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void OnPointsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        PointsRevision++;
        OnPropertyChanged(nameof(PointsRevision));
    }

    public ChartSeries()
    {
        Points.CollectionChanged += OnPointsCollectionChanged;
    }
}
/// <summary>
/// Interaction logic for AnimationEditor.xaml
/// </summary>
partial class AnimationCurveView : UserControl
{
    public static readonly DependencyProperty SeriesSourceProperty =
        DependencyProperty.Register(nameof(SeriesSource), typeof(IEnumerable<ChartSeries>), typeof(AnimationCurveView),
            new PropertyMetadata(null, OnSeriesSourceChanged));

    public static readonly DependencyProperty DefaultTickSpacingProperty =
        DependencyProperty.Register(nameof(DefaultTickSpacing), typeof(double), typeof(AnimationCurveView),
            new PropertyMetadata(5.0, OnLayoutParameterChanged));

    public static readonly DependencyProperty MinTickSpacingProperty =
        DependencyProperty.Register(nameof(MinTickSpacing), typeof(double), typeof(AnimationCurveView),
            new PropertyMetadata(1.0, OnLayoutParameterChanged)); // 10 ticks in 10 px = 1 px/tick

    public static readonly DependencyProperty MaxTickSpacingProperty =
        DependencyProperty.Register(nameof(MaxTickSpacing), typeof(double), typeof(AnimationCurveView),
            new PropertyMetadata(100.0, OnLayoutParameterChanged)); // 1 tick = 100 px

    public static readonly DependencyProperty TargetVerticalGridSpacingProperty =
        DependencyProperty.Register(nameof(TargetVerticalGridSpacing), typeof(double), typeof(AnimationCurveView),
            new PropertyMetadata(50.0, OnLayoutParameterChanged));

    public static readonly DependencyProperty TargetHorizontalGridSpacingProperty =
        DependencyProperty.Register(nameof(TargetHorizontalGridSpacing), typeof(double), typeof(AnimationCurveView),
            new PropertyMetadata(50.0, OnLayoutParameterChanged));

    internal IEnumerable<ChartSeries> SeriesSource
    {
        get => (IEnumerable<ChartSeries>)GetValue(SeriesSourceProperty);
        set => SetValue(SeriesSourceProperty, value);
    }

    public double DefaultTickSpacing
    {
        get => (double)GetValue(DefaultTickSpacingProperty);
        set => SetValue(DefaultTickSpacingProperty, value);
    }

    public double MinTickSpacing
    {
        get => (double)GetValue(MinTickSpacingProperty);
        set => SetValue(MinTickSpacingProperty, value);
    }

    public double MaxTickSpacing
    {
        get => (double)GetValue(MaxTickSpacingProperty);
        set => SetValue(MaxTickSpacingProperty, value);
    }

    public double TargetVerticalGridSpacing
    {
        get => (double)GetValue(TargetVerticalGridSpacingProperty);
        set => SetValue(TargetVerticalGridSpacingProperty, value);
    }

    public double TargetHorizontalGridSpacing
    {
        get => (double)GetValue(TargetHorizontalGridSpacingProperty);
        set => SetValue(TargetHorizontalGridSpacingProperty, value);
    }

    public MatrixTransform DataToScreenTransform { get; } = new();

    private double _scaleX = 5.0;
    private double _scaleY = 1.0;
    private double _offsetX = 10.0;
    private double _offsetY = 100.0;

    private bool _isPanning;
    private Point _lastMousePos;
    private bool _hasAutoFittedY;
    private Cursor _defaultCursor;
    public AnimationCurveView()
    {
        InitializeComponent();
        _scaleX = Math.Clamp(DefaultTickSpacing, MinTickSpacing, MaxTickSpacing);
    }

    private static void OnLayoutParameterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AnimationCurveView ctrl) return;

        ctrl._scaleX = Math.Clamp(ctrl._scaleX, ctrl.MinTickSpacing, ctrl.MaxTickSpacing);
        ctrl.GridVisual.TargetVerticalGridSpacing = ctrl.TargetVerticalGridSpacing;
        ctrl.GridVisual.TargetHorizontalGridSpacing = ctrl.TargetHorizontalGridSpacing;
        ctrl.XAxisVisual.TargetVerticalGridSpacing = ctrl.TargetVerticalGridSpacing;
        ctrl.YAxisVisual.TargetHorizontalGridSpacing = ctrl.TargetHorizontalGridSpacing;
        ctrl.UpdateViewport();
    }

    private static void OnSeriesSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AnimationCurveView ctrl) return;

        ctrl.DetachCollectionHandlers(e.OldValue as IEnumerable<ChartSeries>);
        ctrl.AttachCollectionHandlers(e.NewValue as IEnumerable<ChartSeries>);

        ctrl._hasAutoFittedY = false;
        ctrl.AutoFitVerticalRange();
    }

    private void AttachCollectionHandlers(IEnumerable<ChartSeries> seriesList)
    {
        if (seriesList == null) return;

        if (seriesList is INotifyCollectionChanged observable)
            observable.CollectionChanged += OnSeriesCollectionChanged;

        foreach (var series in seriesList)
            series.PropertyChanged += OnSeriesItemPropertyChanged;
    }

    private void DetachCollectionHandlers(IEnumerable<ChartSeries> seriesList)
    {
        if (seriesList == null) return;

        if (seriesList is INotifyCollectionChanged observable)
            observable.CollectionChanged -= OnSeriesCollectionChanged;

        foreach (var series in seriesList)
            series.PropertyChanged -= OnSeriesItemPropertyChanged;
    }

    private void OnSeriesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (ChartSeries s in e.OldItems)
                s.PropertyChanged -= OnSeriesItemPropertyChanged;

        if (e.NewItems != null)
            foreach (ChartSeries s in e.NewItems)
                s.PropertyChanged += OnSeriesItemPropertyChanged;

        AutoFitVerticalRange();
    }

    private void OnSeriesItemPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChartSeries.Points) or nameof(ChartSeries.PointsRevision))
            AutoFitVerticalRange();
    }

    private void OnPlotSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_hasAutoFittedY && PlotHost.ActualHeight > 0)
            AutoFitVerticalRange();
        else
            UpdateViewport();
    }

    public void AutoFitVerticalRange()
    {
        var h = PlotHost.ActualHeight;
        if (h <= 0) return;

        var minY = double.PositiveInfinity;
        var maxY = double.NegativeInfinity;

        if (SeriesSource != null)
        {
            foreach (var series in SeriesSource)
            {
                if (series?.Points == null) continue;
                foreach (var pt in series.Points)
                {
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.Y > maxY) maxY = pt.Y;
                }
            }
        }

        if (double.IsInfinity(minY) || double.IsInfinity(maxY))
        {
            minY = 0;
            maxY = 10;
        }
        else if (Math.Abs(maxY - minY) < 1e-12)
        {
            var pad = Math.Abs(minY) > 1e-6 ? Math.Abs(minY) * 0.1 : 1.0;
            minY -= pad;
            maxY += pad;
        }

        var span = maxY - minY;
        var paddedMin = minY - span * 0.1;
        var paddedMax = maxY + span * 0.1;

        _scaleY = h / (paddedMax - paddedMin);
        _offsetY = paddedMax * _scaleY;
        _hasAutoFittedY = true;

        UpdateViewport();
    }

    private void UpdateViewport()
    {
        DataToScreenTransform.Matrix = new(_scaleX, 0, 0, -_scaleY, _offsetX, _offsetY);

        GridVisual.ScaleX = _scaleX;
        GridVisual.ScaleY = _scaleY;
        GridVisual.OffsetX = _offsetX;
        GridVisual.OffsetY = _offsetY;
        GridVisual.InvalidateVisual();

        XAxisVisual.ScaleX = _scaleX;
        XAxisVisual.OffsetX = _offsetX;
        XAxisVisual.InvalidateVisual();

        YAxisVisual.ScaleY = _scaleY;
        YAxisVisual.OffsetY = _offsetY;
        YAxisVisual.InvalidateVisual();

        CrosshairVisual.ScaleX = _scaleX;
        CrosshairVisual.OffsetX = _offsetX;
        CrosshairVisual.InvalidateVisual();
    }

    private void OnPlotMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1)
        {
            _scaleX = Math.Clamp(DefaultTickSpacing, MinTickSpacing, MaxTickSpacing);
            _offsetX = 10.0;
            AutoFitVerticalRange();
            return;
        }
    }

    private void OnPlotMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _lastMousePos = e.GetPosition(PlotHost);
        PlotHost.CaptureMouse();
        _defaultCursor = PlotHost.Cursor;
        PlotHost.Cursor = Cursors.SizeAll;
    }

    private void OnPlotMouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(PlotHost);
        CrosshairVisual.MousePosition = current;
        CrosshairVisual.InvalidateVisual();

        var x = (int)Math.Round((current.X - _offsetX) / _scaleX);
        var y = (_offsetY - current.Y) / _scaleY;
        MouseCoordinates.Text = $"{x}, {y:G4}";

        if (_isPanning)
        {
            _offsetX += current.X - _lastMousePos.X;
            _offsetY += current.Y - _lastMousePos.Y;
            _lastMousePos = current;
            UpdateViewport();
        }
    }

    private void OnPlotMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        PlotHost.ReleaseMouseCapture();
        PlotHost.Cursor = _defaultCursor ?? Cursors.Arrow;
    }

    private void OnPlotLostMouseCapture(object sender, MouseEventArgs e)
    {
        _isPanning = false;
        PlotHost.Cursor = _defaultCursor ?? Cursors.Arrow;
    }

    private void OnPlotMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point mouse = e.GetPosition(PlotHost);
        var factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;

        // Horizontal zoom anchored at cursor X, clamped to [1.0, 100.0] px/tick
        var dataX = (mouse.X - _offsetX) / _scaleX;
        var newScaleX = Math.Clamp(_scaleX * factor, MinTickSpacing, MaxTickSpacing);
        _offsetX = mouse.X - dataX * newScaleX;
        _scaleX = newScaleX;

        // Vertical zoom only when Shift is held
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            var dataY = (_offsetY - mouse.Y) / _scaleY;
            var newScaleY = Math.Clamp(_scaleY * factor, 1e-9, 1e9);
            _offsetY = mouse.Y + dataY * newScaleY;
            _scaleY = newScaleY;
        }

        UpdateViewport();
        e.Handled = true;
    }

    private void OnPlotMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        _scaleX = Math.Clamp(DefaultTickSpacing, MinTickSpacing, MaxTickSpacing);
        _offsetX = 10.0;
        AutoFitVerticalRange();
    }
}
