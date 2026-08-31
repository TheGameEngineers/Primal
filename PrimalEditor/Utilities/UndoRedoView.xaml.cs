// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace PrimalEditor.Utilities;

/// <summary>
/// Interaction logic for UndoRedoView.xaml
/// </summary>
public partial class UndoRedoView : UserControl
{
    private INotifyCollectionChanged _prevUndoList;

    public UndoRedoView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from previous UndoList
        _prevUndoList?.CollectionChanged -= UndoList_CollectionChanged;
        _prevUndoList = null;

        if (DataContext is UndoRedo undoRedo && undoRedo.UndoList is INotifyCollectionChanged newUndoList)
        {
            _prevUndoList = newUndoList;
            newUndoList.CollectionChanged += UndoList_CollectionChanged;
        }
    }

    private void UndoList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if ((DataContext as UndoRedo).RedoList.Count > 0)
        {
            var h1 = undoStack.ActualHeight;
            var h2 = scrollViewer.ActualHeight;
            var y = Math.Max(h1 - h2 * 0.5, 0);
            scrollViewer.ScrollToVerticalOffset(y);
        }
        else
        {
            scrollViewer.ScrollToEnd();
        }
    }
}
