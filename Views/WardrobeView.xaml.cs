using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace AuraLauncher.Views;

/// <summary>
/// Логика взаимодействия для WardrobeView.xaml:
/// Поворот фигурки перетаскиванием мыши по горизонтали,
/// по отпусканию доворот к 0° (спереди) или 180° (сзади) за 500 мс.
/// </summary>
public partial class WardrobeView : UserControl
{
    private bool _isDragging;
    private Point _lastPos;
    private double _currentAngle = 0; // 0..360

    public WardrobeView()
    {
        InitializeComponent();
    }

    private void Skin_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDragging = true;
            _lastPos = e.GetPosition(this);
            SkinDragArea?.CaptureMouse();
        }
    }

    private void Skin_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            var currentPos = e.GetPosition(this);
            double deltaX = currentPos.X - _lastPos.X;
            _lastPos = currentPos;

            _currentAngle = (_currentAngle + deltaX * 1.5) % 360;
            if (_currentAngle < 0) _currentAngle += 360;

            ApplyAngle(_currentAngle);
        }
    }

    private void Skin_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            SkinDragArea?.ReleaseMouseCapture();

            // По отпусканию доворот к 0° (спереди) или 180° (сзади) за 500 мс
            double targetAngle = (_currentAngle > 90 && _currentAngle < 270) ? 180 : 0;
            AnimateSnapTo(targetAngle);
        }
    }

    public void RotateToBack()
    {
        _currentAngle = 180;
        ApplyAngle(180);
    }

    public void RotateToFront()
    {
        _currentAngle = 0;
        ApplyAngle(0);
    }

    private void ApplyAngle(double angle)
    {
        if (FigureScale == null || ImgFront == null || ImgBack == null) return;

        // Нормализация в радианы для ScaleX
        double rad = angle * Math.PI / 180.0;
        double cos = Math.Cos(rad);

        FigureScale.ScaleX = Math.Abs(cos) < 0.05 ? 0.05 : Math.Abs(cos);

        // Если угол между 90° и 270° — показываем спину, иначе лицо
        bool isBack = angle > 90 && angle < 270;
        ImgFront.Opacity = isBack ? 0 : 1;
        ImgBack.Opacity = isBack ? 1 : 0;
    }

    private void AnimateSnapTo(double targetAngle)
    {
        var anim = new DoubleAnimation
        {
            From = _currentAngle,
            To = targetAngle,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var clock = anim.CreateClock();
        clock.CurrentTimeInvalidated += (s, ev) =>
        {
            if (clock.CurrentProgress.HasValue)
            {
                double prog = clock.CurrentProgress.Value;
                double cur = _currentAngle + (targetAngle - _currentAngle) * prog;
                ApplyAngle(cur);
            }
        };
        clock.Completed += (s, ev) =>
        {
            _currentAngle = targetAngle;
            ApplyAngle(targetAngle);
        };
        ApplyAnimationClock(RenderTransformProperty, null);
        clock.Controller?.Begin();
    }
}
