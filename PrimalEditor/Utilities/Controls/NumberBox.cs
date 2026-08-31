// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrimalEditor.Utilities.Controls;

/// <summary>
/// A compact numeric editor control that supports keyboard/text input and
/// mouse-drag adjustments. Values may be clamped or wrapped to a range.
/// </summary>
[DefaultEvent("ValueChanged"), DefaultProperty("Value")]
[TemplatePart(Name = "PART_textBox", Type = typeof(TextBox))]
class NumberBox : Control
{
    // Internal state used for mouse-drag value adjustments.
    private double _mouseXStart;
    private double _originalValue;
    private double _lastValue;
    private double _multiplier;
    private bool _captured = false;
    private bool _valueChangedByMouse = false;
    // Value magnitude scales sensitivity for large values.
    private double _valueMagnitude = 1.0;

    /// <summary>
    /// Routed event raised after the numeric Value has changed.
    /// </summary>
    public static readonly RoutedEvent ValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<double>), typeof(NumberBox));

    /// <summary>
    /// Occurs when the Value has changed (after a change completes).
    /// </summary>
    public event RoutedPropertyChangedEventHandler<double> ValueChanged
    {
        add { AddHandler(ValueChangedEvent, value); }
        remove { RemoveHandler(ValueChangedEvent, value); }
    }

    /// <summary>
    /// Routed event raised while the Value is changing (e.g. during editing).
    /// </summary>
    public static readonly RoutedEvent ValueChangingEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanging), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(NumberBox));

    /// <summary>
    /// Occurs while the Value is being changed (for live feedback).
    /// </summary>
    public event RoutedEventHandler ValueChanging
    {
        add { AddHandler(ValueChangingEvent, value); }
        remove { RemoveHandler(ValueChangingEvent, value); }
    }

    /// <summary>
    /// Multiplier applied to mouse-drag delta. Default is 1.0.
    /// </summary>
    public double Multiplier
    {
        get => (double)GetValue(MultiplierProperty);
        set => SetValue(MultiplierProperty, value);
    }
    public static readonly DependencyProperty MultiplierProperty =
        DependencyProperty.Register(nameof(Multiplier), typeof(double), typeof(NumberBox),
            new PropertyMetadata(1.0));

    /// <summary>
    /// Minimum allowed value. If Value is less than Minimum, it is coerced.
    /// </summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumberBox),
            new PropertyMetadata(double.MinValue, new PropertyChangedCallback(OnMinimumChanged)));

    /// <summary>
    /// Maximum allowed value. If Value is greater than Maximum, it is coerced.
    /// </summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumberBox),
            new PropertyMetadata(double.MaxValue, new PropertyChangedCallback(OnMaximumChanged)));

    /// <summary>
    /// The numeric value displayed/edited by the control. Nullable to allow uninitialized state.
    /// This property is two-way bindable by default.
    /// </summary>
    public double? Value
    {
        get => (double?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double?), typeof(NumberBox),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                new PropertyChangedCallback(OnValueChanging), new CoerceValueCallback(OnCoerceValue)));

    /// <summary>
    /// If true, the NumberBox treats values as integers and adjusts sensitivity accordingly.
    /// </summary>
    public bool IsInteger
    {
        get => (bool)GetValue(IsIntegerProperty);
        set => SetValue(IsIntegerProperty, value);
    }
    public static readonly DependencyProperty IsIntegerProperty =
        DependencyProperty.Register(nameof(IsInteger), typeof(bool), typeof(NumberBox),
            new PropertyMetadata(false));

    /// <summary>
    /// If true, values that exceed the range [Minimum, Maximum] will wrap around instead of clamping.
    /// </summary>
    public bool WrapAround
    {
        get => (bool)GetValue(WrapAroundProperty);
        set => SetValue(WrapAroundProperty, value);
    }
    public static readonly DependencyProperty WrapAroundProperty =
        DependencyProperty.Register(nameof(WrapAround), typeof(bool), typeof(NumberBox),
            new PropertyMetadata(false));

    // Called when Minimum property changes. Ensures invariants and coerces Value if necessary.
    private static void OnMinimumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var numberBox = d as NumberBox;
        var min = (double)e.NewValue;
        var max = numberBox.Maximum;

        // Ensure Minimum <= Maximum
        if (min > max)
        {
            numberBox.SetCurrentValue(MinimumProperty, max);
            min = max;
        }

        // Coerce Value upward if below new minimum
        if (numberBox.Value < min)
        {
            numberBox.SetCurrentValue(ValueProperty, min);
        }
    }

    // Called when Maximum property changes. Ensures invariants and coerces Value if necessary.
    private static void OnMaximumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var numberBox = d as NumberBox;
        var max = (double)e.NewValue;
        var min = numberBox.Minimum;

        // Ensure Maximum >= Minimum
        if (max < min)
        {
            numberBox.SetCurrentValue(MaximumProperty, min);
            max = min;
        }

        // Coerce Value downward if above new maximum
        if (numberBox.Value > max)
        {
            numberBox.SetCurrentValue(ValueProperty, max);
        }
    }

    // Coerce callback for Value property: clamps or wraps to the configured range.
    private static object OnCoerceValue(DependencyObject d, object baseValue)
    {
        var numberBox = d as NumberBox;
        if (baseValue is double value)
        {
            return ClampOrWrap(value, numberBox.Minimum, numberBox.Maximum, numberBox.WrapAround);
        }

        return baseValue;
    }

    // Called when Value changes. Raises ValueChanging and ValueChanged events depending on context.
    private static void OnValueChanging(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var numberBox = d as NumberBox;

        // Only raise events if change is caused by mouse capture or by the textbox (keyboard).
        if (Mouse.Captured != null || ValueChangedByTextBox(numberBox))
        {
            numberBox.RaiseEvent(new RoutedEventArgs(ValueChangingEvent));

            // If change is not caused by mouse, raise final changed event.
            if (Mouse.Captured == null && !numberBox._valueChangedByMouse)
            {
                numberBox._lastValue = numberBox.Value ?? 0.0;
                if (numberBox.IsInteger) numberBox._originalValue = Math.Round(numberBox._originalValue);
                numberBox.OnValueChanged();
            }
        }
    }

    // Internal helper that decides whether to raise ValueChanged and updates magnitude used for sensitivity.
    private void OnValueChanged()
    {
        // If integer mode or value actually changed, update magnitude & raise ValueChanged.
        if (IsInteger || !_originalValue.IsTheSameAs(_lastValue))
        {
            // Increase sensitivity for large absolute values so mouse-dragging feels natural.
            _valueMagnitude = Math.Pow(Math.Max(Math.Log10(Math.Abs(_lastValue)), 1.0), 4.0);
            var args = new RoutedPropertyChangedEventArgs<double>(_originalValue, _lastValue) { RoutedEvent = ValueChangedEvent };
            RaiseEvent(args);
        }
    }

    // Record the original value when focus is gained (used for change delta computation).
    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        _originalValue = ClampOrWrap(Value ?? 0.0, Minimum, Maximum, WrapAround);
        if (IsInteger) _originalValue = Math.Round(_originalValue);
    }

    // Mouse left-button down on the textbox: begin capture for drag-to-change behavior.
    private void OnMouseLBD(object sender, MouseButtonEventArgs e)
    {
        OnGotFocus(sender, e);

        var textBox = sender as TextBox;
        // If the textbox already has keyboard focus, let it handle clicks (for caret placement).
        if (textBox.IsKeyboardFocused) return;

        // Begin capturing mouse for interactive drag changes.
        Mouse.Capture(sender as UIElement);
        _captured = true;
        _valueChangedByMouse = false;
        _lastValue = _originalValue;
        e.Handled = true;
        _mouseXStart = e.GetPosition(this).X;
    }

    // Mouse left-button up: stop capture and either commit the mouse-driven change or focus the textbox.
    private void OnMouseLBU(object sender, MouseButtonEventArgs e)
    {
        if (_captured)
        {
            Mouse.Capture(null);
            _captured = false;
            if (_valueChangedByMouse)
            {
                // Commit and notify listeners that a change occurred via dragging.
                OnValueChanged();
                e.Handled = true;
            }
            else
            {
                // No drag happened; give keyboard focus to the textbox for editing.
                Keyboard.Focus(sender as IInputElement);
            }
        }
    }

    // Mouse move while captured: translate horizontal movement into value changes.
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_captured)
        {
            var mouseX = e.GetPosition(this).X;
            var d = mouseX - _mouseXStart;
            _mouseXStart = mouseX;
            if (!d.IsTheSameAs(0.0))
            {
                // Adjust multiplier by modifier keys for fine/coarse control.
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _multiplier = 0.001;
                else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _multiplier = 0.1;
                else _multiplier = 0.01;

                if (IsInteger) _multiplier *= 5;

                // Update the last value based on movement, sensitivity, and configured multiplier.
                _lastValue += (d * _multiplier * Multiplier * _valueMagnitude);

                // NOTE: set _valueChangedByMouse before setting Value. It determines which
                //       events are raised when Value is changed.
                _valueChangedByMouse = true;
                SetCurrentValue(ValueProperty, ClampOrWrap(_lastValue, Minimum, Maximum, WrapAround));
            }
        }
    }

    // Helper that either clamps or wraps a value into [min, max].
    private static double ClampOrWrap(double value, double min, double max, bool wrap)
    {
        Debug.Assert(min <= max);
        if (!wrap)
        {
            return Math.Clamp(value, min, max);
        }

        // Wrapping semantics: modulo by max and shift into range if below min.
        value %= max;
        if (value < min) value += max;

        return value;
    }

    // Determines if the value change was caused by typing/keyboard focus inside the text box.
    private static bool ValueChangedByTextBox(NumberBox numberBox)
        => (Mouse.Captured == null && !numberBox._valueChangedByMouse &&
            numberBox.GetTemplateChild("PART_textBox") is TextBox textBox && textBox.IsKeyboardFocused);

    // Hook up template parts (text box) event handlers when the control template is applied.
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("PART_textBox") is TextBox textBox)
        {
            // Use preview mouse events so we can intercept clicks for drag behavior.
            textBox.PreviewMouseLeftButtonDown += OnMouseLBD;
            textBox.PreviewMouseLeftButtonUp += OnMouseLBU;
            textBox.MouseMove += OnMouseMove;
            textBox.GotFocus += OnGotFocus;
        }
    }

    static NumberBox()
    {
        // Associate the default style for this control type (Generic.xaml).
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NumberBox),
             new FrameworkPropertyMetadata(typeof(NumberBox)));
    }
}