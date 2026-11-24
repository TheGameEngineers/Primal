// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace PrimalEditor.Editors;

public class TreeViewSelectionMarginConverter : IValueConverter
{
    public double IndentSize { get; set; } = 13.0; // Matches the ItemsPresenter margin

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TreeViewItem item) return new Thickness(0);

        int depth = 0;
        DependencyObject current = VisualTreeHelper.GetParent(item);

        // Fallback for logical tree if visual tree isn't fully rendered yet
        if (current == null)
        {
            ItemsControl parent = ItemsControl.ItemsControlFromItemContainer(item);
            while (parent != null && parent is not TreeView)
            {
                depth++;
                parent = ItemsControl.ItemsControlFromItemContainer(parent);
            }
        }
        else
        {
            while (current != null)
            {
                if (current is TreeViewItem) depth++;
                if (current is TreeView) break;
                current = VisualTreeHelper.GetParent(current);
            }
        }

        double leftMargin = -(depth * IndentSize);
        return new Thickness(leftMargin, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}


public class TreeViewLineConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // 0 = Root
        // 1 = Normal
        // 2 = RootLast
        // 3 = Last

        if (value is not TreeViewItem item) return "1";

        ItemsControl ic = ItemsControl.ItemsControlFromItemContainer(item);
        if (ic == null) return "1";

        var isRoot = ic is TreeView;
        var isLast = ic.ItemContainerGenerator.IndexFromContainer(item) == ic.Items.Count - 1;

        if (isRoot && isLast) return "2";
        if (isRoot) return "0";
        if (isLast) return "3";

        return "1";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>
/// Interaction logic for JointHierarchyView.xaml
/// </summary>
public partial class JointHierarchyView : UserControl
{
    public static readonly DependencyProperty SelectedJointProperty =
        DependencyProperty.Register(nameof(SelectedJoint), typeof(SkeletonJoint), typeof(JointHierarchyView),
            new PropertyMetadata(null));

    internal SkeletonJoint SelectedJoint
    {
        get => (SkeletonJoint)GetValue(SelectedJointProperty);
        set => SetValue(SelectedJointProperty, value);
    }

    internal event EventHandler SelectedJointChanged;

    private void OnTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        SelectedJoint = e.NewValue as SkeletonJoint;
        SelectedJointChanged?.Invoke(this, EventArgs.Empty);
    }

    public JointHierarchyView()
    {
        InitializeComponent();
        jointTreeView.ItemContainerGenerator.StatusChanged += (s, e) =>
        {
            if (jointTreeView.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            {
                var item = jointTreeView.ItemContainerGenerator
                .ContainerFromIndex(0) as TreeViewItem;
                item?.IsSelected = true;
                item?.IsExpanded = true;
                item?.Focus();
            }
        };
    }
}
