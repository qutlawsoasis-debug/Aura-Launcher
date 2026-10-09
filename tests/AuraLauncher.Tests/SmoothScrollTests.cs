using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AuraLauncher.Behaviors;
using Xunit;

namespace AuraLauncher.Tests;

public class SmoothScrollTests
{
    private static void RunOnStaThread(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured != null)
        {
            throw new InvalidOperationException("STA test failed", captured);
        }
    }

    [Fact]
    public void ApplyWheelDelta_FastMultiNotchScroll_DoesNotResetTargetAt300Px()
    {
        RunOnStaThread(() =>
        {
            var content = new Border { Width = 200, Height = 2000 };
            var scrollViewer = new ScrollViewer
            {
                Width = 200,
                Height = 200,
                Content = content
            };
            SmoothScroll.SetIsEnabled(scrollViewer, true);

            scrollViewer.Measure(new Size(200, 200));
            scrollViewer.Arrange(new Rect(0, 0, 200, 200));
            scrollViewer.UpdateLayout();

            Assert.True(scrollViewer.ScrollableHeight > 1000);

            for (int i = 0; i < 6; i++)
            {
                SmoothScroll.ApplyWheelDelta(scrollViewer, -120);
            }

            double target = SmoothScroll.GetTargetVerticalOffset(scrollViewer);
            Assert.Equal(600.0, target, precision: 2);

            for (int step = 0; step < 60; step++)
            {
                SmoothScroll.StepForTesting(scrollViewer, 1.0 / 60.0);
                scrollViewer.UpdateLayout();
            }

            Assert.Equal(600.0, scrollViewer.VerticalOffset, precision: 0);
        });
    }

    [Fact]
    public void SetSmoothVerticalOffset_UpdatesScrollOffsetImmediately()
    {
        RunOnStaThread(() =>
        {
            var content = new Border { Width = 200, Height = 1200 };
            var scrollViewer = new ScrollViewer
            {
                Width = 200,
                Height = 200,
                Content = content
            };
            SmoothScroll.SetIsEnabled(scrollViewer, true);

            scrollViewer.Measure(new Size(200, 200));
            scrollViewer.Arrange(new Rect(0, 0, 200, 200));
            scrollViewer.UpdateLayout();

            SmoothScroll.SetSmoothVerticalOffset(scrollViewer, 450.0);
            scrollViewer.UpdateLayout();

            Assert.Equal(450.0, scrollViewer.VerticalOffset, precision: 1);
            Assert.Equal(450.0, SmoothScroll.GetTargetVerticalOffset(scrollViewer), precision: 1);
        });
    }

    [Fact]
    public void ExternalScroll_CancelsActiveSmoothScrollAndSyncsTarget()
    {
        RunOnStaThread(() =>
        {
            var content = new Border { Width = 200, Height = 2000 };
            var scrollViewer = new ScrollViewer
            {
                Width = 200,
                Height = 200,
                Content = content
            };
            SmoothScroll.SetIsEnabled(scrollViewer, true);

            scrollViewer.Measure(new Size(200, 200));
            scrollViewer.Arrange(new Rect(0, 0, 200, 200));
            scrollViewer.UpdateLayout();

            SmoothScroll.ApplyWheelDelta(scrollViewer, -120 * 5);
            Assert.Equal(500.0, SmoothScroll.GetTargetVerticalOffset(scrollViewer), precision: 1);

            SmoothScroll.StepForTesting(scrollViewer, 1.0 / 60.0);
            scrollViewer.UpdateLayout();

            // Simulate user dragging ScrollBar thumb or external ScrollToVerticalOffset
            scrollViewer.ScrollToVerticalOffset(950.0);
            scrollViewer.UpdateLayout();

            Assert.Equal(950.0, scrollViewer.VerticalOffset, precision: 1);
            Assert.Equal(950.0, SmoothScroll.GetTargetVerticalOffset(scrollViewer), precision: 1);

            // Stepping further should NOT pull the ScrollViewer back toward 500
            SmoothScroll.StepForTesting(scrollViewer, 1.0 / 60.0);
            scrollViewer.UpdateLayout();
            Assert.Equal(950.0, scrollViewer.VerticalOffset, precision: 1);
        });
    }

    [Fact]
    public void ResetAllScrollViewers_ResetsVerticalOffsetAndTargetToZero()
    {
        RunOnStaThread(() =>
        {
            var content = new Border { Width = 200, Height = 2000 };
            var scrollViewer = new ScrollViewer
            {
                Width = 200,
                Height = 200,
                Content = content
            };
            var container = new Grid();
            container.Children.Add(scrollViewer);

            SmoothScroll.SetIsEnabled(scrollViewer, true);

            container.Measure(new Size(200, 200));
            container.Arrange(new Rect(0, 0, 200, 200));
            container.UpdateLayout();

            SmoothScroll.SetSmoothVerticalOffset(scrollViewer, 650.0);
            container.UpdateLayout();
            Assert.Equal(650.0, scrollViewer.VerticalOffset, precision: 1);

            SmoothScroll.ResetAllScrollViewers(container);
            container.UpdateLayout();

            Assert.Equal(0.0, scrollViewer.VerticalOffset, precision: 1);
            Assert.Equal(0.0, SmoothScroll.GetTargetVerticalOffset(scrollViewer), precision: 1);
        });
    }
}
