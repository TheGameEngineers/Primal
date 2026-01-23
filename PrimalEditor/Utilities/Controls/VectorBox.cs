// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;

namespace PrimalEditor.Utilities.Controls;

public enum VectorType
{
    Vector2,
    Vector3,
    Vector4,
}

class VectorBox : Control
{
    public static readonly RoutedEvent ValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<Vector4>), typeof(VectorBox));

    public event RoutedPropertyChangedEventHandler<Vector4> ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    public static readonly RoutedEvent ValueChangingEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanging), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(VectorBox));

    public event RoutedEventHandler ValueChanging
    {
        add => AddHandler(ValueChangingEvent, value);
        remove => RemoveHandler(ValueChangingEvent, value);
    }

    public VectorType VectorType
    {
        get => (VectorType)GetValue(VectorTypeProperty);
        set => SetValue(VectorTypeProperty, value);
    }

    public static readonly DependencyProperty VectorTypeProperty =
        DependencyProperty.Register(nameof(VectorType), typeof(VectorType), typeof(VectorBox),
            new PropertyMetadata(VectorType.Vector3));

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(VectorBox),
            new PropertyMetadata(Orientation.Horizontal));

    public double Multiplier
    {
        get => (double)GetValue(MultiplierProperty);
        set => SetValue(MultiplierProperty, value);
    }

    public static readonly DependencyProperty MultiplierProperty =
        DependencyProperty.Register(nameof(Multiplier), typeof(double), typeof(VectorBox),
            new PropertyMetadata(1.0));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(VectorBox),
            new PropertyMetadata(double.MinValue));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(VectorBox),
            new PropertyMetadata(double.MaxValue));

    public double? X
    {
        get => (double?)GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    public static readonly DependencyProperty XProperty =
        DependencyProperty.Register(nameof(X), typeof(double?), typeof(VectorBox),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public double? Y
    {
        get => (double?)GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    public static readonly DependencyProperty YProperty =
        DependencyProperty.Register(nameof(Y), typeof(double?), typeof(VectorBox),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public double? Z
    {
        get => (double?)GetValue(ZProperty);
        set => SetValue(ZProperty, value);
    }

    public static readonly DependencyProperty ZProperty =
        DependencyProperty.Register(nameof(Z), typeof(double?), typeof(VectorBox),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public double? W
    {
        get => (double?)GetValue(WProperty);
        set => SetValue(WProperty, value);
    }

    public static readonly DependencyProperty WProperty =
        DependencyProperty.Register(nameof(W), typeof(double?), typeof(VectorBox),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool WrapAround
    {
        get => (bool)GetValue(WrapAroundProperty);
        set => SetValue(WrapAroundProperty, value);
    }

    public static readonly DependencyProperty WrapAroundProperty =
        DependencyProperty.Register(nameof(WrapAround), typeof(bool), typeof(VectorBox),
            new PropertyMetadata(false));

    private void OnNumberBoxValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (e.OriginalSource is NumberBox numberBox)
        {
            Vector4 newValue = new() { X = (float)(X ?? 0f), Y = (float)(Y ?? 0f), Z = (float)(Z ?? 0f), W = (float)(W ?? 0f) };

            var index = Orientation == Orientation.Horizontal ? Grid.GetColumn(numberBox) : Grid.GetRow(numberBox);
            index = Math.Clamp(index, 0, 3);
            var oldValue = newValue;
            oldValue[index] = (float)e.OldValue;

            var args = new RoutedPropertyChangedEventArgs<Vector4>(oldValue, newValue) { RoutedEvent = ValueChangedEvent };
            RaiseEvent(args);
        }
    }

    private void OnNumberBoxValueChanging(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(ValueChangingEvent, e.OriginalSource));

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        AddHandler(NumberBox.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>(OnNumberBoxValueChanged));
        AddHandler(NumberBox.ValueChangingEvent, new RoutedEventHandler(OnNumberBoxValueChanging));
    }

    static VectorBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(VectorBox),
            new FrameworkPropertyMetadata(typeof(VectorBox)));
    }
}
