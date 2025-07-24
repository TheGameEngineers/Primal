// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrimalEditor.Utilities.Controls;
// Adapted from https://stackoverflow.com/a/23151248/3111536 [WPF Drag & drop from ListBox with SelectionMode Multiple]
class ListViewItemEx : ListViewItem
{
    private bool _deferSelection = false;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1 && IsSelected)
        {
            // the user may start drag by clicking into selected items
            // delay destroying the selection to the Up event
            _deferSelection = true;
        }
        else
        {
            base.OnMouseLeftButtonDown(e);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_deferSelection)
        {
            try
            {
                base.OnMouseLeftButtonDown(e);
            }
            finally
            {
                _deferSelection = false;
            }
        }
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        // abort deferred Down
        _deferSelection = false;
        base.OnMouseLeave(e);
    }
}

class ListViewEx : ListView
{
    protected override DependencyObject GetContainerForItemOverride() => new ListViewItemEx();
}