// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PrimalEditor.Utilities.Controls;

/// <summary>
/// Supported vector dimensionalities for the control.
/// </summary>
public enum VectorType
{
    Vector2,
    Vector3,
    Vector4
}

/// <summary>
/// A WPF control that exposes editable vector components (X, Y, Z, W).
/// Designed to be templated in XAML (see Generic.xaml) and to work with a custom NumberBox control.
/// Provides copy/paste commands that use an internal clipboard matching by VectorType.
/// Raises routed events when a component's value changes or is in the process of changing.
/// </summary>
class VectorBox : Control
{
    /// <summary>
    /// Simple container used as an internal clipboard entry for copy/paste operations.
    /// Stores the vector type (dimensionality) and the Vector4 value.
    /// </summary>
    private class VectorCopy
    {
        public VectorType Type { get; set; }
        public Vector4 Value { get; set; }
    }

    // Public commands used from XAML (ContextMenu items bind to these via x:Static)
    public static readonly RoutedUICommand CopyVectorCommand =
        new("CopyVector", "CopyVector", typeof(VectorBox));
    public static readonly RoutedUICommand PasteVectorCommand =
        new("PasteVector", "PasteVector", typeof(VectorBox));

    // Internal clipboard for vector copy/paste. Null when no copy is available.
    private static VectorCopy _clipboard;

    // Routed event fired after a single component change completes.
    public static readonly RoutedEvent ValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<Vector4>), typeof(VectorBox));

    public event RoutedPropertyChangedEventHandler<Vector4> ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value); 
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    // Routed event fired while a component value is being changed (for live updates).
    public static readonly RoutedEvent ValueChangingEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanging), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(VectorBox));

    public event RoutedEventHandler ValueChanging
    {
        add => AddHandler(ValueChangingEvent, value);
        remove => RemoveHandler(ValueChangingEvent, value);
    }

    /// <summary>
    /// The dimensionality of the vector exposed by this control (2/3/4).
    /// Affects which components are considered required for copy/paste.
    /// </summary>
    public VectorType VectorType
    {
        get => (VectorType)GetValue(VectorTypeProperty);
        set => SetValue(VectorTypeProperty, value);
    }

    public static readonly DependencyProperty VectorTypeProperty =
        DependencyProperty.Register(nameof(VectorType), typeof(VectorType), typeof(VectorBox),
            new PropertyMetadata(VectorType.Vector3));

    /// <summary>
    /// Layout orientation for the component NumberBoxes (Horizontal or Vertical).
    /// The orientation also determines whether Grid.GetColumn or Grid.GetRow is used to map a NumberBox to a component index.
    /// </summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(VectorBox),
            new PropertyMetadata(Orientation.Horizontal));

    /// <summary>
    /// Multiplier applied to delta changes in the UI NumberBox (if the templated NumberBox supports a multiplier).
    /// </summary>
    public double Multiplier
    {
        get => (double)GetValue(MultiplierProperty);
        set => SetValue(MultiplierProperty, value);
    }
    public static readonly DependencyProperty MultiplierProperty =
        DependencyProperty.Register(nameof(Multiplier), typeof(double), typeof(VectorBox),
            new PropertyMetadata(1.0));

    /// <summary>
    /// Minimum allowed value for components.
    /// </summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(VectorBox),
            new PropertyMetadata(double.MinValue));

    /// <summary>
    /// Maximum allowed value for components.
    /// </summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(VectorBox),
            new PropertyMetadata(double.MaxValue));

    /// <summary>
    /// X component of the vector. Nullable to allow uninitialized state.
    /// Bound two-way by default so UI can update the backing value.
    /// </summary>
    public double? X
    {
        get => (double?)GetValue(XProperty);
        set => SetValue(XProperty, value);
    }
    public static readonly DependencyProperty XProperty =
        DependencyProperty.Register(nameof(X), typeof(double?), typeof(VectorBox),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>
    /// Y component of the vector. Nullable to allow uninitialized state.
    /// </summary>
    public double? Y
    {
        get => (double?)GetValue(YProperty);
        set => SetValue(YProperty, value);
    }
    public static readonly DependencyProperty YProperty =
        DependencyProperty.Register(nameof(Y), typeof(double?), typeof(VectorBox),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>
    /// Z component of the vector. Nullable to allow uninitialized state.
    /// </summary>
    public double? Z
    {
        get => (double?)GetValue(ZProperty);
        set => SetValue(ZProperty, value);
    }
    public static readonly DependencyProperty ZProperty =
        DependencyProperty.Register(nameof(Z), typeof(double?), typeof(VectorBox),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>
    /// W component of the vector. Nullable to allow uninitialized state.
    /// </summary>
    public double? W
    {
        get => (double?)GetValue(WProperty);
        set => SetValue(WProperty, value);
    }
    public static readonly DependencyProperty WProperty =
        DependencyProperty.Register(nameof(W), typeof(double?), typeof(VectorBox),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>
    /// If true, component values will wrap around when hitting limits (if supported by the templated NumberBox).
    /// Kept as a dependency property so it can be set from XAML templates.
    /// </summary>
    public bool WrapAround
    {
        get => (bool)GetValue(WrapAroundProperty);
        set => SetValue(WrapAroundProperty, value);
    }
    public static readonly DependencyProperty WrapAroundProperty =
        DependencyProperty.Register(nameof(WrapAround), typeof(bool), typeof(VectorBox),
            new PropertyMetadata(false));

    /// <summary>
    /// Handler for NumberBox.ValueChangedEvent. Converts the sender NumberBox into a vector component
    /// index based on layout orientation, constructs old and new Vector4 values, and raises the ValueChanged routed event.
    /// </summary>
    private void OnNumberBoxValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Ensure the original source is the templated NumberBox control
        if (e.OriginalSource is NumberBox numberBox)
        {
            // Build the "new" vector using current component properties (coerce null => 0f)
            Vector4 newValue = new() { X = (float)(X ?? 0f), Y = (float)(Y ?? 0f), Z = (float)(Z ?? 0f), W = (float)(W ?? 0f) };

            // Determine which component changed: column for horizontal layout, row for vertical layout.
            var index = Orientation == Orientation.Horizontal ? Grid.GetColumn(numberBox) : Grid.GetRow(numberBox);

            // Clamp index to the 0..3 range to prevent out-of-range access.
            index = Math.Clamp(index, 0, 3);

            // oldValue is same as newValue except the changed component is set to the old numeric value.
            var oldValue = newValue;
            oldValue[index] = (float)e.OldValue;

            // Raise a routed event with the old/new Vector4 values.
            var args = new RoutedPropertyChangedEventArgs<Vector4>(oldValue, newValue) { RoutedEvent = ValueChangedEvent };
            RaiseEvent(args);
        }
    }

    /// <summary>
    /// Forwards NumberBox.ValueChangingEvent as the control's ValueChanging routed event.
    /// ValueChanging is raised with the original source so listeners can determine which component is actively changing.
    /// </summary>
    private void OnNumberBoxValueChanging(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(ValueChangingEvent, e.OriginalSource));

    /// <summary>
    /// Show context menu on right-click even if the control doesn't have keyboard focus.
    /// This mirrors common behavior in editors where right-click opens a context menu.
    /// </summary>
    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (ContextMenu != null && !IsKeyboardFocusWithin)
        {
            ContextMenu.PlacementTarget = this;
            ContextMenu.Placement = PlacementMode.MousePoint;
            ContextMenu.IsOpen = true;
            e.Handled = true;
            return;
        }

        base.OnPreviewMouseRightButtonDown(e);
    }

    /// <summary>
    /// CanExecute for the copy command. Ensures all required components for the current VectorType have values.
    /// </summary>
    private static void OnCopyCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (sender is VectorBox vb)
        {
            bool canCopy = vb.VectorType switch
            {
                VectorType.Vector2 => vb.X.HasValue && vb.Y.HasValue,
                VectorType.Vector3 => vb.X.HasValue && vb.Y.HasValue && vb.Z.HasValue,
                VectorType.Vector4 => vb.X.HasValue && vb.Y.HasValue && vb.Z.HasValue && vb.W.HasValue,
                _ => false
            };
            e.CanExecute = canCopy;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Executes the copy command by storing the current vector into the internal clipboard.
    /// Uses Vector4 as a fixed container even for Vector2/Vector3; the VectorType field records the dimensionality.
    /// </summary>
    private static void OnCopyExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (sender is VectorBox vb)
        {
            _clipboard = new()
            {
                Type = vb.VectorType,
                Value = new((float)(vb.X ?? 0f), (float)(vb.Y ?? 0f), (float)(vb.Z ?? 0f), (float)(vb.W ?? 0f))
            };
        }
    }

    /// <summary>
    /// CanExecute for the paste command. Only enabled when clipboard exists and matches this control's VectorType.
    /// </summary>
    private static void OnPasteCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (sender is VectorBox vb)
        {
            e.CanExecute = _clipboard != null && _clipboard.Type == vb.VectorType;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Executes paste by assigning clipboard values to the control's component properties.
    /// Implicit float->double conversion is used for assignment.
    /// </summary>
    private static void OnPasteExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (sender is VectorBox vb && _clipboard != null && _clipboard.Type == vb.VectorType)
        {
            // assign values from clipboard (float -> double implicit conversion)
            vb.X = _clipboard.Value.X;
            vb.Y = _clipboard.Value.Y;
            vb.Z = _clipboard.Value.Z;
            vb.W = _clipboard.Value.W;
        }
    }

    /// <summary>
    /// Default constructor wires up command bindings and listens to NumberBox routed events from the control template.
    /// </summary>
    public VectorBox()
    {
        CommandBindings.Add(new CommandBinding(CopyVectorCommand, OnCopyExecuted, OnCopyCanExecute));
        CommandBindings.Add(new CommandBinding(PasteVectorCommand, OnPasteExecuted, OnPasteCanExecute));
        AddHandler(NumberBox.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>(OnNumberBoxValueChanged));
        AddHandler(NumberBox.ValueChangingEvent, new RoutedEventHandler(OnNumberBoxValueChanging));
    }

    /// <summary>
    /// Ensure WPF looks up the default control template for VectorBox using the type as the key.
    /// </summary>
    static VectorBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(VectorBox),
            new FrameworkPropertyMetadata(typeof(VectorBox)));
    }
}