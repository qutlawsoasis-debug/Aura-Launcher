using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Views;

public partial class WorkshopView : UserControl
{
    private Window? _parentWindow;

    public WorkshopView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _parentWindow = Window.GetWindow(this);
        if (_parentWindow != null)
        {
            _parentWindow.PreviewKeyDown += OnWindowPreviewKeyDown;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_parentWindow != null)
        {
            _parentWindow.PreviewKeyDown -= OnWindowPreviewKeyDown;
            _parentWindow = null;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is WorkshopViewModel oldVm)
        {
            oldVm.SubTabChanged -= OnSubTabChanged;
        }
        if (e.NewValue is WorkshopViewModel newVm)
        {
            newVm.SubTabChanged += OnSubTabChanged;
        }
    }

    private void OnSubTabChanged()
    {
        WorldsScrollViewer?.ScrollToTop();
        ScrollItemsControlToTop(ModsItemsControl);
        ScreenshotsScrollViewer?.ScrollToTop();
    }

    private static void ScrollItemsControlToTop(DependencyObject? control)
    {
        if (control == null) return;
        if (System.Windows.Media.VisualTreeHelper.GetChildrenCount(control) > 0 &&
            System.Windows.Media.VisualTreeHelper.GetChild(control, 0) is ScrollViewer sv)
        {
            sv.ScrollToTop();
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is WorkshopViewModel vm)
            {
                if (vm.IsScreenshotPreviewOpen)
                {
                    vm.CloseScreenshotPreviewCommand.Execute(null);
                    e.Handled = true;
                }
                else if (vm.IsRestoreModalOpen)
                {
                    vm.CloseRestoreDialogCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }

    private void OnScreenshotOverlayMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender && DataContext is WorkshopViewModel vm)
        {
            vm.CloseScreenshotPreviewCommand.Execute(null);
        }
    }

    private void OnRestoreOverlayMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender && DataContext is WorkshopViewModel vm)
        {
            vm.CloseRestoreDialogCommand.Execute(null);
        }
    }

    private void OnModalContentMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }
}
