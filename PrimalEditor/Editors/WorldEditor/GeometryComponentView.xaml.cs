// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.Content;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for GeometryComponentView.xaml
/// </summary>
partial class GeometryComponentView : UserControl
{
    public GeometryComponentView()
    {
        InitializeComponent();
    }

    private static void ResetGeometry(List<(Components.Geometry Geometry, Guid Guid, List<AppliedMaterial> Materials)> selection)
    {
        var entities = selection.Select(x => x.Geometry.Owner).ToList();
        var scene = entities[0].ParentScene;
        var enableList = scene.DisableAndUpdate(entities);
        selection.ForEach(x =>
        {
            x.Geometry.SetGeometry(x.Guid);
            x.Geometry.SetMaterials(x.Materials);
        });
        scene.EnableAndUpdate(enableList);
        MSEntity.CurrentSelection?.GetMSComponent<MSGeometry>().Refresh();
    }

    private async void OnGeometry_Border_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

            var file = files?.Where(x => Path.GetExtension(x).ToLower() == Asset.AssetFileExtension && Asset.TryGetAssetInfo(x)?.Type == AssetType.Mesh).FirstOrDefault();
            if (!string.IsNullOrEmpty(file?.Trim()) && DataContext is MSGeometry vm)
            {
                var assetInfo = Asset.TryGetAssetInfo(file);

                if (assetInfo != null && vm.GeometryGuid != assetInfo.Guid)
                {
                    var selection = vm.SelectedComponents.Where(geometry => geometry.GeometryGuid != assetInfo.Guid).ToList();
                    var undoSelection = selection.Select(geometry => (geometry, geometry.GeometryGuid, geometry.MaterialsList)).ToList();

                    await Task.Run(() => vm.SetGeometry(assetInfo.Guid));

                    var redoSelection = selection.Select(geometry => (geometry, assetInfo.Guid, geometry.MaterialsList)).ToList();

                    Project.UndoRedo.Add(new UndoRedoAction(
                        () => ResetGeometry(undoSelection),
                        () => ResetGeometry(redoSelection),
                        $"Set geometry {assetInfo.FileName}"));
                }
            }
        }
    }

    private void OnTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl tabControl && tabControl.SelectedIndex == -1) tabControl.SelectedIndex = 0;
    }

    private void OnGeometryBorder_Mouse_LBD(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1 && DataContext is MSGeometry vm && vm.GeometryGuid != Guid.Empty)
        {
            ContentBrowserView.OpenAssetEditor(AssetRegistry.GetAssetInfo(vm.GeometryGuid));
        }
    }
}
