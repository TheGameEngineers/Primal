// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrimalEditor.Utilities.Controls;

[DefaultEvent("ValueChanged"), DefaultProperty("Value")]
[TemplatePart(Name = "PART_textBox", Type = typeof(TextBox))]
class NumberBox : Control
{
    private double _mouseXStart;
    private double _originalValue;
    private double _lastValue;
    private double _multiplier;
    private bool _captured = false;
    private bool _valueChangedByMouse = false;
    private double _valueMagnitude = 1.0;

    public static readonly RoutedEvent ValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<double>), typeof(NumberBox));

    public event RoutedPropertyChangedEventHandler<double> ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    public static readonly RoutedEvent ValueChangingEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanging), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(NumberBox));

    public event RoutedEventHandler ValueChanging
    {
        add => AddHandler(ValueChangingEvent, value);
        remove => RemoveHandler(ValueChangingEvent, value);
    }

    public double Multiplier
    {
        get => (double)GetValue(MultiplierProperty);
        set => SetValue(MultiplierProperty, value);
    }

    public static readonly DependencyProperty MultiplierProperty =
        DependencyProperty.Register(nameof(Multiplier), typeof(double), typeof(NumberBox),
            new PropertyMetadata(1.0));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumberBox),
            new PropertyMetadata(double.MinValue, new PropertyChangedCallback(OnMinimumChanged)));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumberBox),
            new PropertyMetadata(double.MaxValue, new PropertyChangedCallback(OnMaximumChanged)));

    public double? Value
    {
        get => (double?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double?), typeof(NumberBox),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                new PropertyChangedCallback(OnValueChanging), new CoerceValueCallback(OnCoerceValue)));

    public bool IsInteger
    {
        get => (bool)GetValue(IsIntegerProperty);
        set => SetValue(IsIntegerProperty, value);
    }

    public static readonly DependencyProperty IsIntegerProperty =
        DependencyProperty.Register(nameof(IsInteger), typeof(bool), typeof(NumberBox),
            new PropertyMetadata(false));

    public bool WrapAround
    {
        get => (bool)GetValue(WrapAroundProperty);
        set => SetValue(WrapAroundProperty, value);
    }

    public static readonly DependencyProperty WrapAroundProperty =
        DependencyProperty.Register(nameof(WrapAround), typeof(bool), typeof(NumberBox),
            new PropertyMetadata(false));

    private static void OnMinimumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var numberBox = d as NumberBox;
        var min = (double)e.NewValue;
        var max = numberBox.Maximum;

        if (min > max)
        {
            numberBox.Minimum = max;
            min = max;
        }

        if (numberBox.Value < min)
        {
            numberBox.Value = min;
        }
    }

    private static void OnMaximumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var numberBox = d as NumberBox;
        var max = (double)e.NewValue;
        var min = numberBox.Minimum;

        if (max < min)
        {
            numberBox.Maximum = min;
            max = min;
        }

        if (numberBox.Value > max)
        {
            numberBox.Value = max;
        }
    }

    private static object OnCoerceValue(DependencyObject d, object baseValue)
    {
        var numberBox = d as NumberBox;
        if (baseValue is double value)
        {
            return ClampOrWrap(value, numberBox.Minimum, numberBox.Maximum, numberBox.WrapAround);
        }

        return baseValue;
    }

    private static void OnValueChanging(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var numberBox = d as NumberBox;
        if (Mouse.Captured != null || ValueChangedByTextBox(numberBox))
        {
            numberBox.RaiseEvent(new RoutedEventArgs(ValueChangingEvent));

            if (Mouse.Captured == null && !numberBox._valueChangedByMouse)
            {
                numberBox._lastValue = numberBox.Value ?? 0.0;
                if (numberBox.IsInteger) numberBox._originalValue = Math.Round(numberBox._originalValue);
                numberBox.OnValueChanged();
            }
        }
    }

    private void OnValueChanged()
    {
        if (IsInteger || !_originalValue.IsTheSameAs(_lastValue))
        {
            _valueMagnitude = Math.Pow(Math.Max(Math.Log10(Math.Abs(_lastValue)), 1.0), 4.0); // increase the sensitivity for larger values
            var args = new RoutedPropertyChangedEventArgs<double>(_originalValue, _lastValue) { RoutedEvent = ValueChangedEvent };
            RaiseEvent(args);
        }
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        _originalValue = ClampOrWrap(Value ?? 0.0, Minimum, Maximum, WrapAround);
        if (IsInteger) _originalValue = Math.Round(_originalValue);
    }

    private void OnMouseLBD(object sender, MouseButtonEventArgs e)
    {
        OnGotFocus(sender, e);

        var textBox = sender as TextBox;
        if (textBox.IsKeyboardFocused) return;

        Mouse.Capture(sender as UIElement);
        _captured = true;
        _valueChangedByMouse = false;
        _lastValue = _originalValue;
        e.Handled = true;
        _mouseXStart = e.GetPosition(this).X;
    }

    private void OnMouseLBU(object sender, MouseButtonEventArgs e)
    {
        if (_captured)
        {
            Mouse.Capture(null);
            _captured = false;
            if (_valueChangedByMouse)
            {
                OnValueChanged();
                e.Handled = true;
            }
            else
            {
                Keyboard.Focus(sender as IInputElement);
            }
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_captured)
        {
            var mouseX = e.GetPosition(this).X;
            var d = mouseX - _mouseXStart;
            _mouseXStart = mouseX;
            if (!d.IsTheSameAs(0.0))
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _multiplier = 0.001;
                else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _multiplier = 0.1;
                else _multiplier = 0.01;

                if (IsInteger) _multiplier *= 5;
                _lastValue += (d * _multiplier * Multiplier * _valueMagnitude);
                // NOTE: set _valueChangedByMouse before setting Value. It determines which
                //       events are raised when Value is changed.
                _valueChangedByMouse = true;
                Value = ClampOrWrap(_lastValue, Minimum, Maximum, WrapAround);
            }
        }
    }

    private static double ClampOrWrap(double value, double min, double max, bool wrap)
    {
        Debug.Assert(min <= max);
        if (!wrap)
        {
            return Math.Clamp(value, min, max);
        }

        value %= max;
        if (value < min) value += max;

        return value;
    }

    private static bool ValueChangedByTextBox(NumberBox numberBox)
    => (Mouse.Captured == null && !numberBox._valueChangedByMouse &&
        numberBox.GetTemplateChild("PART_textBox") is TextBox textBox && textBox.IsKeyboardFocused);


    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("PART_textBox") is TextBox textBox)
        {
            textBox.PreviewMouseLeftButtonDown += OnMouseLBD;
            textBox.PreviewMouseLeftButtonUp += OnMouseLBU;
            textBox.MouseMove += OnMouseMove;
            textBox.GotFocus += OnGotFocus;
        }
    }

    static NumberBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NumberBox),
            new FrameworkPropertyMetadata(typeof(NumberBox)));
    }
}
