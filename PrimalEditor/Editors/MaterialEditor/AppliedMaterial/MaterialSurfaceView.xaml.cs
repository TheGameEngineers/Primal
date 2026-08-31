// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.Utilities.Controls;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for MaterialSurfaceView.xaml
/// </summary>
partial class MaterialSurfaceView : UserControl
{
    private bool _enableUpdate = true;

    private void SetValues(List<float?> m)
    {
        _enableUpdate = false;

        baseColor.X = m[0];
        baseColor.Y = m[1];
        baseColor.Z = m[2];
        baseColor.W = m[3];
        emissiveColor.X = m[4];
        emissiveColor.Y = m[5];
        emissiveColor.Z = m[6];
        emissiveIntensity.Value = m[7];
        metallic.Value = m[8];
        roughness.Value = m[9];

        OnVectorBox_ValueChanging(baseColor, null);
        OnVectorBox_ValueChanging(emissiveColor, null);

        _enableUpdate = true;
    }

    private List<float?> GetValues()
    {
        return
        [
            (float?)baseColor.X,
            (float?)baseColor.Y,
            (float?)baseColor.Z,
            (float?)baseColor.W,
            (float?)emissiveColor.X,
            (float?)emissiveColor.Y,
            (float?)emissiveColor.Z,
            (float?)emissiveIntensity.Value,
            (float?)metallic.Value,
            (float?)roughness.Value
        ];
    }

    private void OnVectorBox_ValueChanging(object sender, RoutedEventArgs e)
    {
        var v = sender as VectorBox;
        if (v.VectorType == VectorType.Vector4)
        {
            var color = Colors.Black;
            if (v.X.HasValue && v.Y.HasValue && v.Z.HasValue && v.W.HasValue)
            {
                color = Color.FromScRgb((float)v.W.Value, (float)v.X.Value, (float)v.Y.Value, (float)v.Z.Value);
            }
            baseColorButton.Background = new SolidColorBrush(color);
        }
        else if (v.VectorType == VectorType.Vector3)
        {
            var color = Colors.Black;
            if (v.X.HasValue && v.Y.HasValue && v.Z.HasValue)
            {
                color = Color.FromScRgb(1.0f, (float)v.X.Value, (float)v.Y.Value, (float)v.Z.Value);
            }
            emissiveColorButton.Background = new SolidColorBrush(color);
        }
    }

    private void Update()
    {
        if (_enableUpdate)
        {
            var vm = DataContext as AppliedMaterialProxy;
            vm.SetMaterialSurfaceArray(GetValues());
            AppliedMaterialProxy.IsDirty = true;
        }
    }

    private void OnVectorBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<Vector4> e) => Update();

    private void OnScalarBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Update();

    private void OnAppliedMaterialProxyPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppliedMaterialProxy.M) &&
            sender is AppliedMaterialProxy vm)
        {
            SetValues(vm.M);
        }
    }

    public MaterialSurfaceView()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (e.OldValue is AppliedMaterialProxy oldVm)
            {
                oldVm.PropertyChanged -= OnAppliedMaterialProxyPropertyChanged;
            }

            if (e.NewValue is AppliedMaterialProxy newVm)
            {
                newVm.PropertyChanged += OnAppliedMaterialProxyPropertyChanged;
                SetValues(newVm.M);
            }
        };
    }
}
