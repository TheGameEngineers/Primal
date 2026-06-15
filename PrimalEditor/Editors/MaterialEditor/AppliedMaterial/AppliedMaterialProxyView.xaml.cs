// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.Content;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for AppliedMaterialProxyView.xaml
/// </summary>
public partial class AppliedMaterialProxyView : UserControl
{
    private string _previousName = string.Empty;

    private void OnTexture_Border_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

            var file = files?.Where(x => Path.GetExtension(x).ToLower() == Asset.AssetFileExtension && Asset.TryGetAssetInfo(x)?.Type == AssetType.Texture).FirstOrDefault();
            if (!string.IsNullOrEmpty(file?.Trim()) && sender is FrameworkElement { DataContext: InputProxy input })
            {
                var assetInfo = Asset.TryGetAssetInfo(file);
                if (assetInfo != null)
                {
                    input.Asset = assetInfo;
                    AppliedMaterialProxy.IsDirty = true;
                }
            }
        }
    }

    private void OnInputBorder_Mouse_LBD(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1 && sender is FrameworkElement { DataContext: InputProxy vm } && vm.Asset != null)
        {
            ContentBrowserView.OpenAssetEditor(AssetRegistry.GetAssetInfo(vm.Asset.Guid));
        }
    }

    private void OnRemoveInput_Button_Click(object sender, RoutedEventArgs e)
    {
        if(sender is FrameworkElement { DataContext: InputProxy input })
        {
            input.Asset = Texture.Default;
            AppliedMaterialProxy.IsDirty = true;
        }
    }

    private void OnName_TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var textBox = sender as TextBox;
        _previousName = textBox.Text.Trim();
    }

    private void OnName_TextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var textBox = sender as TextBox;
        var name = textBox.Text.Trim();
        AppliedMaterialProxy.IsNameDirty = string.IsNullOrEmpty(name) || name != _previousName;
    }

    public AppliedMaterialProxyView()
    {
        InitializeComponent();
    }
}
