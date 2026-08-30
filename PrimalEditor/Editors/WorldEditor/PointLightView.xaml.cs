// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.DllWrappers;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for PointLightView.xaml
/// </summary>
partial class PointLightView : UserControl
{
    private string _propertyName = string.Empty;
    private bool _disableUndoRedo;

    private readonly List<(PointLight Light, Vector3 Attenuation)> _attenuations = [];
    private readonly List<(PointLight Light, float Range)> _ranges = [];

    public static readonly DependencyProperty SpecificLightProperty =
        DependencyProperty.Register(nameof(SpecificLight), typeof(FrameworkElement), typeof(PointLightView),
            new PropertyMetadata(null));

    public FrameworkElement SpecificLight
    {
        get => (FrameworkElement)GetValue(SpecificLightProperty);
        set => SetValue(SpecificLightProperty, value);
    }

    public PointLightView()
    {
        InitializeComponent();
        DataContextChanged += OnPointLightView_DataContextChanged;
    }

    private void OnPointLightView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is MSEntity vm)
        {
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
        _attenuations.Clear();
        _ranges.Clear();

        if (vm.SelectedEntities.Count == 1)
        {
            var light = vm.SelectedEntities[0] as PointLight;
            _attenuations.Add((light, light.Attenuation));
            _ranges.Add((light, light.Range));
        }
        else
        {
            var lights = vm.SelectedEntities.Cast<PointLight>();
            _attenuations.AddRange(lights.Select(x => (x, x.Attenuation)));
            _ranges.AddRange(lights.Select(x => (x, x.Range)));
        }
    }

    private Action GetAttenuationAction()
    {
        var oldValues = _attenuations.ToList();
        return new(() =>
        {
            _disableUndoRedo = true;
            var count = oldValues.Count;
            float[] a = new float[count], b = new float[count], c = new float[count];
            var lightIds = new int[count];
            var keys = new ulong[count];
            var index = 0;
            oldValues.ForEach(x =>
            {
                x.Light.Attenuation = x.Attenuation;
                a[index] = x.Attenuation.X; b[index] = x.Attenuation.Y; c[index] = x.Attenuation.Z;
                lightIds[index] = x.Light.LightId;
                keys[index] = LightSet.GetKey(x.Light.LightSetKey);
                ++index;
            });
            EngineAPI.SetLightAttenuation(lightIds, keys, a, b, c, count);
            MSEntity.Refresh();
            GetValues(MSEntity.CurrentSelection);
            _disableUndoRedo = false;
            _propertyName = string.Empty;
        });
    }

    private Action GetRangeAction()
    {
        var oldValues = _ranges.ToList();
        return new(() =>
        {
            _disableUndoRedo = true;
            var count = oldValues.Count;
            var ranges = new float[count];
            var lightIds = new int[count];
            var keys = new ulong[count];
            var index = 0;
            oldValues.ForEach(x =>
            {
                x.Light.Range = x.Range;
                ranges[index] = x.Range;
                lightIds[index] = x.Light.LightId;
                keys[index] = LightSet.GetKey(x.Light.LightSetKey);
                ++index;
            });
            EngineAPI.SetLightRange(lightIds, keys, ranges, count);
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
            case nameof(MSPointLight.AttenuationA):
            case nameof(MSPointLight.AttenuationB):
            case nameof(MSPointLight.AttenuationC):
                AddUndoRedo(GetAttenuationAction, "Light attenuation changed");
                break;

            case nameof(MSPointLight.Range):
                AddUndoRedo(GetRangeAction, "Light range changed");
                break;
        }

        _propertyName = string.Empty;
    }

    private void On_NumberBox_PreviewMouse_LBU(object sender, MouseButtonEventArgs e) => SetUndoRedo();
}
