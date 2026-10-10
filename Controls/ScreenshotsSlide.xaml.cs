using System;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace AuraLauncher.Controls;

public partial class ScreenshotsSlide : UserControl, ISlideLifecycle
{
    public ScreenshotsSlide()
    {
        InitializeComponent();
    }

    public void OnEntered()
    {
        if (ScreenshotZoomTransform != null)
        {
            ScreenshotZoomTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            ScreenshotZoomTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);

            var zoomX = new DoubleAnimation(1.0, 1.06, TimeSpan.FromSeconds(6));
            var zoomY = new DoubleAnimation(1.0, 1.06, TimeSpan.FromSeconds(6));

            ScreenshotZoomTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, zoomX);
            ScreenshotZoomTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, zoomY);
        }
    }

    public void OnExited()
    {
        if (ScreenshotZoomTransform != null)
        {
            ScreenshotZoomTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            ScreenshotZoomTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
            ScreenshotZoomTransform.ScaleX = 1.0;
            ScreenshotZoomTransform.ScaleY = 1.0;
        }
    }
}
