// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.Content;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for AmbientLightView.xaml
/// </summary>
public partial class AmbientLightView : UserControl
{
    public AmbientLightView()
    {
        InitializeComponent();
    }

    private void OnEnvMap_Border_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            var vm = (sender as FrameworkElement).DataContext as MSAmbientLight;
            var textures = files
                .Where(x => Path.GetExtension(x).ToLower() == Asset.AssetFileExtension && Asset.TryGetAssetInfo(x)?.Type == AssetType.Texture);

            var oldEnvMap = vm.EnvMap;

            foreach (var texture in textures)
            {
                var newEnvMap = Asset.TryGetAssetInfo(texture);
                vm.EnvMap = newEnvMap;
                // TODO: implement asset flags for AssetInfo so we can determine if a texture is a cube map without having to read it.
                if (newEnvMap != null && vm.EnvMap.Guid != oldEnvMap.Guid)
                {
                    Project.UndoRedo.Add(new UndoRedoAction(
                        () => { vm.EnvMap = oldEnvMap; MSEntity.Refresh(); },
                        () => { vm.EnvMap = newEnvMap; MSEntity.Refresh(); },
                        "Ambient light environment map changed"));
                    break;
                }
            }
        }
    }

    private void OnEnvMapBorder_Mouse_LBD(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1 && sender is FrameworkElement { DataContext: MSAmbientLight vm })
        {
            ContentBrowserView.OpenAssetEditor(AssetRegistry.GetAssetInfo(vm.EnvMap.Guid));
        }
    }
}
