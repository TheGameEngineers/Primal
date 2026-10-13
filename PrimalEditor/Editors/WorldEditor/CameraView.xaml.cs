// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.DllWrappers;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using PrimalEditor.Utilities.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for CameraView.xaml
/// </summary>
public partial class CameraView : UserControl
{
    private string _propertyName = string.Empty;
    private bool _disableUndoRedo;

    private readonly List<(Camera Camera, Vector2 NearFarZ)> _nearFarZs = [];
    private readonly List<(PerspectiveCamera Camera, float FoV)> _fieldOfViews = [];
    private readonly List<(OrthographicCamera Camera, float Size)> _orthographicSizes = [];

    public CameraView()
    {
        InitializeComponent();
        DataContextChanged += OnCameraView_DataContextChanged;
    }

    private void OnCameraView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
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
        _nearFarZs.Clear();
        _fieldOfViews.Clear();
        _orthographicSizes.Clear();

        foreach (var entity in vm.SelectedEntities)
        {
            var camera = entity as Camera;
            Debug.Assert(camera != null);

            _nearFarZs.Add((camera, new Vector2(camera.NearZ, camera.FarZ)));

            if (camera is PerspectiveCamera perspectiveCamera)
            {
                _fieldOfViews.Add((perspectiveCamera, perspectiveCamera.FieldOfView));
            }
            else if (camera is OrthographicCamera orthographicCamera)
            {
                _orthographicSizes.Add((orthographicCamera, orthographicCamera.OrthographicSize));
            }
        }
    }

    private Action GetRangeAction()
    {
        var oldValues = _nearFarZs.ToList();
        return new(() =>
        {
            _disableUndoRedo = true;
            var count = oldValues.Count;
            var near = new float[count]; var far = new float[count];
            var ids = new IdType[count];
            var index = 0;
            oldValues.ForEach(item =>
            {
                item.Camera.NearZ = item.NearFarZ.X;
                item.Camera.FarZ = item.NearFarZ.Y;
                near[index] = item.NearFarZ.X; far[index] = item.NearFarZ.Y;
                ids[index] = item.Camera.CameraId;
                ++index;
            });
            EngineAPI.SetCameraRange(ids, near, far, count);
            MSEntity.Refresh();
            GetValues(MSEntity.CurrentSelection);
            _disableUndoRedo = false;
            _propertyName = string.Empty;
        });
    }

    private Action GetFovAction()
    {
        var oldValues = _fieldOfViews.ToList();
        return new(() =>
        {
            _disableUndoRedo = true;
            var count = oldValues.Count;
            var fovs = new float[count];
            var ids = new IdType[count];
            var index = 0;
            oldValues.ForEach(item =>
            {
                item.Camera.FieldOfView = item.FoV;
                fovs[index] = item.FoV;
                ids[index] = item.Camera.CameraId;
                ++index;
            });
            EngineAPI.SetCameraFieldOfView(ids, fovs, count);
            MSEntity.Refresh();
            GetValues(MSEntity.CurrentSelection);
            _disableUndoRedo = false;
            _propertyName = string.Empty;
        });
    }

    private Action GetOrthoSizeAction()
    {
        var oldValues = _orthographicSizes.ToList();
        return new(() =>
        {
            _disableUndoRedo = true;
            var count = oldValues.Count;
            var sizes = new float[count];
            var ids = new IdType[count];
            var index = 0;
            oldValues.ForEach(item =>
            {
                item.Camera.OrthographicSize = item.Size;
                sizes[index] = item.Size;
                ids[index] = item.Camera.CameraId;
                ++index;
            });
            EngineAPI.SetCameraOrthographicSize(ids, sizes, count);
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
            case nameof(MSCamera.NearZ):
            case nameof(MSCamera.FarZ):
                AddUndoRedo(GetRangeAction, "Camera near/far changed");
                break;

            case nameof(MSPerspectiveCamera.FieldOfView):
                AddUndoRedo(GetFovAction, "Camera field of view changed");
                break;

            case nameof(MSOrthographicCamera.OrthographicSize):
                AddUndoRedo(GetOrthoSizeAction, "Camera orthographic size changed");
                break;
        }

        _propertyName = string.Empty;
    }

    private void On_NumberBox_PreviewMouse_LBU(object sender, MouseButtonEventArgs e) => SetUndoRedo();
}
