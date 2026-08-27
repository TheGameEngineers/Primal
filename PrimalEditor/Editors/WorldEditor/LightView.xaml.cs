// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.DllWrappers;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using PrimalEditor.Utilities.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for LightView.xaml
/// </summary>
partial class LightView : UserControl
{
    private string _propertyName = string.Empty;
    private bool _disableUndoRedo;

    private readonly List<(Light Light, string LightSetKey)> _lightSetKeys = [];
    private readonly List<(Light Light, Color Color)> _colors = [];
    private readonly List<(Light Light, float Intensity)> _intensities = [];

    public static readonly DependencyProperty SpecificLightProperty =
        DependencyProperty.Register(nameof(SpecificLight), typeof(FrameworkElement), typeof(LightView),
            new PropertyMetadata(null));

    public FrameworkElement SpecificLight
    {
        get => (FrameworkElement)GetValue(SpecificLightProperty);
        set => SetValue(SpecificLightProperty, value);
    }

    public LightView()
    {
        InitializeComponent();
        DataContextChanged += OnLightView_DataContextChanged;
    }

    private void OnLightView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is MSEntity vm)
        {
            var type = vm.GetType();
            var r = type.GetProperty(nameof(MSLight.ColorR))?.GetValue(vm) as float?;
            var g = type.GetProperty(nameof(MSLight.ColorG))?.GetValue(vm) as float?;
            var b = type.GetProperty(nameof(MSLight.ColorB))?.GetValue(vm) as float?;
            SetColorButtonBackground(r, g, b);

            GetValues(vm);

            vm.PropertyChanged += (_, e) =>
            {
                _propertyName = e.PropertyName;

                if (Mouse.LeftButton == MouseButtonState.Released && !_disableUndoRedo)
                {
                    SetUndoRedo();
                }
            };
        }
    }

    private void GetValues(MSEntity vm)
    {
        _lightSetKeys.Clear();
        _colors.Clear();
        _intensities.Clear();

        if (vm.SelectedEntities.Count == 1)
        {
            var light = vm.SelectedEntities[0] as Light;
            _lightSetKeys.Add((light, light.LightSetKey));
            _colors.Add((light, light.Color));
            _intensities.Add((light, light.Intensity));
        }
        else
        {
            var lights = vm.SelectedEntities.Cast<Light>();
            _lightSetKeys.AddRange(lights.Select(x => (x, x.LightSetKey)));
            _colors.AddRange(lights.Select(x => (x, x.Color)));
            _intensities.AddRange(lights.Select(x => (x, x.Intensity)));
        }
    }

    private Action GetLightSetKeyAction()
    {
        var oldValues = _lightSetKeys.ToList();
        return new(() =>
        {
            _disableUndoRedo = true;
            oldValues.ForEach(x => x.Light.LightSetKey = x.LightSetKey);
            MSEntity.Refresh();
            GetValues(MSEntity.CurrentSelection);
            _disableUndoRedo = false;
            _propertyName = string.Empty;
        });
    }

    private Action GetColorAction()
    {
        var oldValues = _colors.ToList();
        return new(() =>
        {
            _disableUndoRedo = true;
            var count = oldValues.Count;
            float[] r = new float[count], g = new float[count], b = new float[count];
            var lightIds = new int[count];
            var keys = new ulong[count];
            var index = 0;
            oldValues.ForEach(x =>
            {
                x.Light.Color = x.Color;
                r[index] = x.Color.ScR; g[index] = x.Color.ScG; b[index] = x.Color.ScB;
                lightIds[index] = x.Light.LightId;
                keys[index] = LightSet.GetKey(x.Light.LightSetKey);
                ++index;
            });
            EngineAPI.SetLightColor(lightIds, keys, r, g, b, count);
            MSEntity.Refresh();
            var vm = MSEntity.CurrentSelection;
            GetValues(vm);
            var type = vm.GetType();
            var red = type.GetProperty(nameof(MSLight.ColorR))?.GetValue(vm) as float?;
            var green = type.GetProperty(nameof(MSLight.ColorG))?.GetValue(vm) as float?;
            var blue = type.GetProperty(nameof(MSLight.ColorB))?.GetValue(vm) as float?;
            SetColorButtonBackground(red, green, blue);
            _disableUndoRedo = false;
            _propertyName = string.Empty;
        });
    }

    private Action GetIntensityAction()
    {
        var oldValues = _intensities.ToList();
        return new(() =>
        {
            _disableUndoRedo = true;
            var count = oldValues.Count;
            var intensities = new float[count];
            var lightIds = new int[count];
            var keys = new ulong[count];
            var index = 0;
            oldValues.ForEach(x =>
            {
                x.Light.Intensity = x.Intensity;
                intensities[index] = x.Intensity;
                lightIds[index] = x.Light.LightId;
                keys[index] = LightSet.GetKey(x.Light.LightSetKey);
                ++index;
            });
            EngineAPI.SetLightIntensity(lightIds, keys, intensities, count);
            MSEntity.Refresh();
            GetValues(MSEntity.CurrentSelection);
            _disableUndoRedo = false;
            _propertyName = string.Empty;
        });
    }

    private void AddUndoRedo(Func<Action> action, string name)
    {
        var undoAction = action();
        GetValues(DataContext as MSEntity);
        var redoAction = action();
        Project.UndoRedo.Add(new UndoRedoAction(undoAction, redoAction, name));
    }

    private void SetUndoRedo()
    {
        if (string.IsNullOrEmpty(_propertyName)) return;

        switch (_propertyName)
        {
            case nameof(MSLight.LightSetKey):
                AddUndoRedo(GetLightSetKeyAction, "Light set key changed");
                break;

            case nameof(MSLight.ColorR):
            case nameof(MSLight.ColorG):
            case nameof(MSLight.ColorB):
                AddUndoRedo(GetColorAction, "Light color changed");
                break;

            case nameof(MSLight.Intensity):
                AddUndoRedo(GetIntensityAction, "Light intensity changed");
                break;
        }

        _propertyName = string.Empty;
    }

    private void UseLightSet(string lightSetKey)
    {
        if (LightSet.GetKey(lightSetKey) == LightSet.InvalidKey)
        {
            _ = LightSet.AddLightSet(lightSetKey, true);
        }
        DataContext.GetType().GetProperty(nameof(MSLight.LightSetKey))?.SetValue(DataContext, lightSetKey);
    }

    private void SetColorButtonBackground(double? r, double? g, double? b)
    {
        if (r.HasValue && g.HasValue && b.HasValue)
        {
            var color = Color.FromScRgb(1.0f, (float)r.Value, (float)g.Value, (float)b.Value);
            lightColorButton.Background = new SolidColorBrush(color);
        }
        else
        {
            var uri = new Uri("pack://application:,,,/Resources/TextureEditor/Checker64.png");
            //lightColorButton.Background = new ImageBrush() { ImageSource = new BitmapImage(uri) { DecodePixelWidth = 32, DecodePixelHeight = 32 } };
            lightColorButton.Background = new VisualBrush()
            {
                Visual = new Image()
                {
                    Source = new BitmapImage(uri),
                    Width = 32,
                    Height = 32,
                    Stretch = Stretch.UniformToFill,
                },
                TileMode = TileMode.None,
                Viewport = new Rect(0, 0, 32, 32),
                ViewportUnits = BrushMappingMode.Absolute
            };
        }
    }

    private void OnColor_VectorBox_ValueChanging(object sender, RoutedEventArgs e)
    {
        var v = sender as VectorBox;
        SetColorButtonBackground(v.X, v.Y, v.Z);
    }

    private void OnLightSetKey_ComboBox_DropDownClosed(object sender, EventArgs e)
    {
        var comboBox = sender as ComboBox;
        if (comboBox.SelectedItem is string text && !string.IsNullOrEmpty(text.Trim()))
        {
            UseLightSet(text);
        }
    }

    // Disable mouse wheel scrolling for the light set combo box to prevent accidental changes
    private void OnLightSetKey_ComboBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => e.Handled = true;

    private void OnNewLightSet_ComboBoxItem_Selected(object sender, RoutedEventArgs e)
    {
        // NOTE: Defer showing the dialog and modifying the combo box until after the
        // current selection event has finished. Creating a new light set modifies
        // the underlying ObservableCollection that the ComboBox is bound to and
        // doing that while the ComboBox is processing selection can cause a
        // "collection modified" InvalidOperationException. Use the Dispatcher to
        // run the dialog on the UI queue after the current handler completes.
        var current = DataContext.GetType().GetProperty(nameof(MSLight.LightSetKey))?.GetValue(DataContext) as string ?? string.Empty;
        Dispatcher.BeginInvoke(() =>
        {
            var dlg = new NewLightSetDialog()
            {
                Owner = Application.Current.MainWindow,
                CurrentLightSet = current,
            };

            var lightSet = dlg.ShowDialog() == true ? dlg.LightSetName : dlg.CurrentLightSet;
            lightSetComboBox.SelectedItem = lightSet;
            UseLightSet(lightSet);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void On_NumberBox_PreviewMouse_LBU(object sender, MouseButtonEventArgs e) => SetUndoRedo();
}
