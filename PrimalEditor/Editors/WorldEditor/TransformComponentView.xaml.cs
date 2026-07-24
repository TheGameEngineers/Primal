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
/// Interaction logic for TransformComponentView.xaml
/// </summary>
partial class TransformComponentView : UserControl
{
    private delegate void SetTransformAPI(IdType[] ids, float[] x, float[] y, float[] z, int count, int isLocal);

    private string _propertyName = string.Empty;
    private bool _disableUndoRedo;

    private readonly List<(Transform Transform, Vector3 Position)> _positions = [];
    private readonly List<(Transform Transform, Vector3 Rotation)> _rotations = [];
    private readonly List<(Transform Transform, Vector3 Scale)> _scales = [];

    public TransformComponentView()
    {
        InitializeComponent();
        Loaded += OnTransformComponentViewLoaded;
    }

    private void GetValues(MSTransform vm)
    {
        _positions.Clear();
        _rotations.Clear();
        _scales.Clear();

        if (vm.SelectedComponents.Count == 1)
        {
            var c = vm.SelectedComponents[0];
            _positions.Add((c, c.Position));
            _rotations.Add((c, c.Rotation));
            _scales.Add((c, c.Scale));
        }
        else
        {
            _positions.AddRange(vm.SelectedComponents.Select(x => (x, x.Position)));
            _rotations.AddRange(vm.SelectedComponents.Select(x => (x, x.Rotation)));
            _scales.AddRange(vm.SelectedComponents.Select(x => (x, x.Scale)));
        }
    }

    private void OnTransformComponentViewLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnTransformComponentViewLoaded;
        if (DataContext is MSTransform vm)
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

    private Action GetAction(
        IEnumerable<(Transform Transform, Vector3 Value)> values,
        Action<(Transform Transform, Vector3 Value)> action,
        SetTransformAPI transform)
    {
        var vm = DataContext as MSTransform;
        var oldValues = values.ToList();

        return new(() =>
        {
            _disableUndoRedo = true;
            var count = oldValues.Count;
            float[] x = new float[count], y = new float[count], z = new float[count];
            var index = 0;
            oldValues.ForEach(item =>
            {
                action(item);
                var v = item.Value;
                x[index] = v.X; y[index] = v.Y; z[index] = v.Z; ++index;
            });
            transform([.. oldValues.Select(x => x.Transform.Owner.EntityId)], x, y, z, count, 0);
            var currentSelection = MSEntity.CurrentSelection?.GetMSComponent<MSTransform>();
            currentSelection.Refresh();
            GetValues(currentSelection);
            _disableUndoRedo = false;
            _propertyName = string.Empty;

            var sx = currentSelection.ScaleX;
            var sy = currentSelection.ScaleY;
            var sz = currentSelection.ScaleZ;

            currentSelection.IsUniformScale = (sx.IsTheSameAs(sy) && sx.IsTheSameAs(sz));
        });
    }

    private void AddUndoRedo(
        IEnumerable<(Transform Transform, Vector3 Value)> values,
        Action<(Transform Transform, Vector3 Value)> action,
        SetTransformAPI transform, string name)
    {
        var undoAction = GetAction(values, action, transform);
        GetValues(DataContext as MSTransform);
        var redoAction = GetAction(values, action, transform);
        Project.UndoRedo.Add(new UndoRedoAction(undoAction, redoAction, name));
    }

    private void SetUndoRedo()
    {
        if (string.IsNullOrEmpty(_propertyName)) return;

        switch (_propertyName)
        {
            case nameof(MSTransform.PosX):
            case nameof(MSTransform.PosY):
            case nameof(MSTransform.PosZ):
                AddUndoRedo(_positions, (x) => x.Transform.Position = x.Value, EngineAPI.EntityAPI.SetPosition, "Position changed");
                break;

            case nameof(MSTransform.RotX):
            case nameof(MSTransform.RotY):
            case nameof(MSTransform.RotZ):
                AddUndoRedo(_rotations, (x) => x.Transform.Rotation = x.Value, EngineAPI.EntityAPI.SetRotation, "Rotation changed");
                break;

            case nameof(MSTransform.ScaleX):
            case nameof(MSTransform.ScaleY):
            case nameof(MSTransform.ScaleZ):
                AddUndoRedo(_scales, (x) => x.Transform.Scale = x.Value, EngineAPI.EntityAPI.SetScale, "Scale changed");
                break;
        }

        _propertyName = string.Empty;
    }

    private void OnScale_VectorBox_ValueChanging(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as MSTransform;
        if (vm.IsUniformScale && e.OriginalSource is Utilities.Controls.NumberBox numberBox)
        {
            _disableUndoRedo = true;
            vm.ScaleX = vm.ScaleY = vm.ScaleZ = (float?)numberBox.Value;
            _disableUndoRedo = false;
        }
    }

    private void Refresh(MSTransform vm, string name)
    {
        _disableUndoRedo = true;
        vm.Refresh();
        _disableUndoRedo = false;
        _propertyName = name;
        SetUndoRedo();
    }

    private void OnLocalRotation_VectorBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<Vector4> e)
    {
        var vm = DataContext as MSTransform;
        var ids = MSEntity.CurrentSelection.SelectedEntities.Select(x => x.EntityId).ToArray();
        var rotations = EngineAPI.EntityAPI.GetRotation(ids);
        for (var i = 0; i < rotations.Length; ++i)
        {
            var c = vm.SelectedComponents[i];
            c.Rotation = rotations[i];
        }

        Refresh(vm, nameof(MSTransform.RotX));
    }

    private void OnLocalPosition_VectorBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<Vector4> e)
    {
        var vm = DataContext as MSTransform;
        var ids = MSEntity.CurrentSelection.SelectedEntities.Select(x => x.EntityId).ToArray();
        var positions = EngineAPI.EntityAPI.GetPosition(ids);
        for (var i = 0; i < positions.Length; ++i)
        {
            var c = vm.SelectedComponents[i];
            c.Position = positions[i];
        }

        Refresh(vm, nameof(MSTransform.PosX));
    }

    private void On_VectorBox_PreviewMouse_LBU(object sender, MouseButtonEventArgs e) => SetUndoRedo();
}
