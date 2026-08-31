// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace PrimalEditor.Dictionaries;

partial class ControlTemplates : ResourceDictionary
{
    private static void MoveUpFocus(UIElement element)
    {
        DependencyObject parent = element;
        while ((parent = VisualTreeHelper.GetParent(parent)) != null && Keyboard.Focus(parent as UIElement) == element) ;
    }

    private static void UpdateTextBoxSource(TextBox textBox, BindingExpression exp)
    {
        if (textBox.Tag is ICommand command && command.CanExecute(textBox.Text))
        {
            command.Execute(textBox.Text);
        }
        else
        {
            exp.UpdateSource();
        }
    }

    private void OnTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        var textBox = sender as TextBox;
        var exp = textBox.GetBindingExpression(TextBox.TextProperty);
        if (exp == null) return;

        if (e.Key is Key.Enter or Key.Tab)
        {
            UpdateTextBoxSource(textBox, exp);
            if (e.Key is Key.Enter)
            {
                MoveUpFocus(textBox);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            exp.UpdateTarget();
            MoveUpFocus(textBox);
        }
    }

    private void OnTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        var textBox = sender as TextBox;
        var exp = textBox.GetBindingExpression(TextBox.TextProperty);
        exp?.UpdateTarget();

        (sender as TextBox).SelectAll();
    }

    private void OnTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var textBox = sender as TextBox;
        if (!textBox.IsVisible) return;
        var exp = textBox.GetBindingExpression(TextBox.TextProperty);
        if (exp != null)
        {
            UpdateTextBoxSource(textBox, exp);
        }
    }

    private void OnTextBoxRename_KeyDown(object sender, KeyEventArgs e)
    {
        var textBox = sender as TextBox;
        var exp = textBox.GetBindingExpression(TextBox.TextProperty);
        if (exp == null) return;

        if (e.Key == Key.Enter)
        {
            UpdateTextBoxSource(textBox, exp);
            textBox.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            UpdateTextBoxSource(textBox, exp);
        }
        else if (e.Key == Key.Escape)
        {
            exp.UpdateTarget();
            textBox.Visibility = Visibility.Collapsed;
        }
    }

    private void OnTextBoxRename_LostFocus(object sender, RoutedEventArgs e)
    {
        var textBox = sender as TextBox;
        if (!textBox.IsVisible) return;
        var exp = textBox.GetBindingExpression(TextBox.TextProperty);
        if (exp != null)
        {
            exp.UpdateTarget();
            textBox.Visibility = Visibility.Collapsed;
        }
    }

    private void OnClose_Button_Click(object sender, RoutedEventArgs e)
    {
        var window = (Window)((FrameworkElement)sender).TemplatedParent;
        window.Close();
    }

    private void OnMaximizeRestore_Button_Click(object sender, RoutedEventArgs e)
    {
        var window = (Window)((FrameworkElement)sender).TemplatedParent;
        window.WindowState = (window.WindowState == WindowState.Normal) ?
            WindowState.Maximized : WindowState.Normal;
    }

    private void OnMinimize_Button_Click(object sender, RoutedEventArgs e)
    {
        var window = (Window)((FrameworkElement)sender).TemplatedParent;
        window.WindowState = WindowState.Minimized;
    }

    private void OnHorizontalSlider_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var slider = sender as Slider;
        if (slider.TickPlacement != System.Windows.Controls.Primitives.TickPlacement.None)
        {
            slider.Value = Math.Clamp(slider.Value + Math.Sign(e.Delta) * slider.TickFrequency, slider.Minimum, slider.Maximum);
        }
    }

    private void OnScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;

        var lines = SystemParameters.WheelScrollLines;
        // Honor the Windows “scroll one screen at a time” setting
        if (lines == -1)
        {
            // Let the default handler do a page scroll
            return;
        }

        // Scale by Delta (120 = one “notch”). The *10 factor is a common
        // empirical value that feels close to native apps; tweak if you like.
        var offset = e.Delta * 10.0 * lines / 120.0;

        sv.ScrollToVerticalOffset(sv.VerticalOffset - offset);
        e.Handled = true;   // stop the default (non-accelerated) handling
    }
}