// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Utilities;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for SkeletonEditorView.xaml
/// </summary>
public partial class SkeletonEditorView : UserControl
{
    public SkeletonEditorView()
    {
        InitializeComponent();
        
        jointTreeView.ItemContainerGenerator.StatusChanged += (s, e) =>
        {
            if (jointTreeView.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            {
                var item = jointTreeView.ItemContainerGenerator
                .ContainerFromIndex(0) as TreeViewItem;
                item?.IsSelected = true;
                item?.Focus();
            }
        };
    }
}
