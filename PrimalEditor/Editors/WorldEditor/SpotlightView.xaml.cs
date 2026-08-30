// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.DllWrappers;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for SpotlightView.xaml
/// </summary>
partial class SpotlightView : UserControl
{
    private string _propertyName = string.Empty;
    private bool _disableUndoRedo;

    private readonly List<(Spotlight Light, float Umbra)> _umbras = [];
    private readonly List<(Spotlight Light, float Penumbra)> _penumbras = [];

    public SpotlightView()
    {
        InitializeComponent();
        DataContextChanged += OnSpotlightView_DataContextChanged;
    }

    private void OnSpotlightView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
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
        _umbras.Clear();
        _penumbras.Clear();

        if (vm.SelectedEntities.Count == 1)
        {
            var light = vm.SelectedEntities[0] as Spotlight;
            _umbras.Add((light, light.Umbra));
            _penumbras.Add((light, light.Penumbra));
        }
        else
        {
            var lights = vm.SelectedEntities.Cast<Spotlight>();
            _umbras.AddRange(lights.Select(x => (x, x.Umbra)));
            _penumbras.AddRange(lights.Select(x => (x, x.Penumbra)));
        }
    }

    private Action GetAction(
        IEnumerable<(Spotlight Light, float Value)> values,
        Action<(Spotlight Light, float Value)> action,
        Func<(Spotlight Light, float Value), float> getUmbra,
        Func<(Spotlight Light, float Value), float> getPenumbra)
    {
        var oldValues = values.ToList();
        return new(() =>
        {
            _disableUndoRedo = true;
            var count = oldValues.Count;
            float[] umbras = new float[count], penumbras = new float[count];
            var lightIds = new int[count];
            var keys = new ulong[count];
            var index = 0;
            oldValues.ForEach(x =>
            {
                action(x);
                umbras[index] = getUmbra(x);
                penumbras[index] = getPenumbra(x);
                lightIds[index] = x.Light.LightId;
                keys[index] = LightSet.GetKey(x.Light.LightSetKey);
                ++index;
            });
            EngineAPI.SetLightConeAngles(lightIds, keys, umbras, penumbras, count);
            MSEntity.Refresh();
            GetValues(MSEntity.CurrentSelection);
            _disableUndoRedo = false;
            _propertyName = string.Empty;
        });
    }

    private void AddUndoRedo(
        IEnumerable<(Spotlight Light, float Value)> values,
        Action<(Spotlight Light, float Value)> action,
        Func<(Spotlight Light, float Value), float> getUmbra,
        Func<(Spotlight Light, float Value), float> getPenumbra, string name)
    {
        var undoAction = GetAction(values, action, getUmbra, getPenumbra);
        GetValues(DataContext as MSEntity);
        var redoAction = GetAction(values, action, getUmbra, getPenumbra);
        Project.UndoRedo.Add(new UndoRedoAction(undoAction, redoAction, name));
    }

    private void SetUndoRedo()
    {
        if (string.IsNullOrEmpty(_propertyName)) return;

        switch (_propertyName)
        {
            case nameof(MSSpotlight.Umbra):
                AddUndoRedo(_umbras, (x) => x.Light.Umbra = x.Value, (x) => x.Value, (x) => x.Light.Penumbra, "Light umbra changed");
                break;

            case nameof(MSSpotlight.Penumbra):
                AddUndoRedo(_penumbras, (x) => x.Light.Penumbra = x.Value, (x) => x.Light.Umbra, (x) => x.Value, "Light penumbra changed");
                break;
        }

        _propertyName = string.Empty;
    }

    private void On_ScalarBox_PreviewMouse_LBU(object sender, MouseButtonEventArgs e) => SetUndoRedo();
}
