using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Microsoft.Extensions.DependencyInjection;
using AuraLauncher.Core;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.Services.Implementations;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Views;

/// <summary>
/// Логика взаимодействия для WardrobeView.xaml:
/// Настоящий 3D-просмотр скина игрока (кубоиды, Viewport3D).
/// Перетаскивание мышью вращает вокруг оси Y и наклоняет по X (до ±20°) с инерцией (~400 мс).
/// Двойной клик плавно возвращает вид спереди (0°) за 400 мс.
/// </summary>
public partial class WardrobeView : UserControl
{
    private readonly AxisAngleRotation3D _rotY = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D _rotX = new(new Vector3D(1, 0, 0), 0);

    private double _yawAngle = 0;
    private double _pitchAngle = 0;

    private bool _isDragging;
    private Point _lastPos;
    private DateTime _lastMoveTime;
    private double _yawVelocity;
    private double _pitchVelocity;

    private Storyboard? _inertiaStoryboard;
    private WardrobeViewModel? _hookedVm;

    public WardrobeView()
    {
        InitializeComponent();

        var transformGroup = new Transform3DGroup();
        transformGroup.Children.Add(new RotateTransform3D(_rotY));
        transformGroup.Children.Add(new RotateTransform3D(_rotX));
        PlayerModelVisual.Transform = transformGroup;

        Loaded += (s, e) =>
        {
            HookViewModel();
            Refresh3DModel();
        };

        DataContextChanged += (s, e) =>
        {
            HookViewModel();
            Refresh3DModel();
        };
    }

    private void HookViewModel()
    {
        if (_hookedVm != null)
        {
            _hookedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _hookedVm = null;
        }

        WardrobeViewModel? vm = DataContext as WardrobeViewModel;
        if (vm == null && DataContext is MainViewModel mvm)
        {
            vm = mvm.WardrobeVM;
        }

        if (vm != null)
        {
            _hookedVm = vm;
            _hookedVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WardrobeViewModel.SkinPath) ||
            e.PropertyName == nameof(WardrobeViewModel.IsSlimModel) ||
            e.PropertyName == nameof(WardrobeViewModel.SkinDisplayName))
        {
            Dispatcher.Invoke(Refresh3DModel);
        }
    }

    public void Refresh3DModel()
    {
        try
        {
            WardrobeViewModel? vm = DataContext as WardrobeViewModel;
            if (vm == null && DataContext is MainViewModel mvm)
            {
                vm = mvm.WardrobeVM;
            }

            var skinService = App.Services?.GetService<ISkinService>();

            BitmapSource? skinBmp = null;
            if (skinService != null)
            {
                skinBmp = skinService.LoadSkinImage(vm?.SkinPath) as BitmapSource;
            }

            if (skinBmp == null)
            {
                skinBmp = SkinService.LoadDefaultSteveBitmap();
            }

            bool isSlim = vm?.IsSlimModel ?? false;
            var modelGroup = SkinModel3DBuilder.BuildPlayerModel(skinBmp, isSlim);
            PlayerModelVisual.Content = modelGroup;
        }
        catch (Exception ex)
        {
            App.Log($"[3D-SKIN: ERROR] Failed to build 3D player model: {ex.Message}");
        }
    }

    // ==========================================
    // УПРАВЛЕНИЕ МЫШЬЮ И ИНЕРЦИЯ
    // ==========================================

    private void Skin_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.ClickCount == 2)
            {
                // Двойной клик: плавный возврат к виду спереди за 400 мс
                ResetToFrontSmooth();
                return;
            }

            _inertiaStoryboard?.Stop();
            _yawAngle = _rotY.Angle;
            _pitchAngle = _rotX.Angle;

            _isDragging = true;
            _lastPos = e.GetPosition(SkinDragArea);
            _lastMoveTime = DateTime.UtcNow;
            _yawVelocity = 0;
            _pitchVelocity = 0;
            SkinDragArea?.CaptureMouse();
        }
    }

    private void Skin_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            var currentPos = e.GetPosition(SkinDragArea);
            var now = DateTime.UtcNow;
            double dt = (now - _lastMoveTime).TotalSeconds;
            _lastMoveTime = now;

            double dx = currentPos.X - _lastPos.X;
            double dy = currentPos.Y - _lastPos.Y;
            _lastPos = currentPos;

            // Чувствительность вращения:
            _yawAngle += dx * 0.7;
            _pitchAngle = Math.Clamp(_pitchAngle + dy * 0.4, -20.0, 20.0);

            _rotY.Angle = _yawAngle;
            _rotX.Angle = _pitchAngle;

            if (dt > 0.002)
            {
                double vYaw = (dx * 0.7) / dt;
                double vPitch = (dy * 0.4) / dt;
                _yawVelocity = _yawVelocity * 0.4 + vYaw * 0.6;
                _pitchVelocity = _pitchVelocity * 0.4 + vPitch * 0.6;
            }
        }
    }

    private void Skin_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            SkinDragArea?.ReleaseMouseCapture();

            var sinceLastMove = (DateTime.UtcNow - _lastMoveTime).TotalMilliseconds;
            if (sinceLastMove < 150 && (Math.Abs(_yawVelocity) > 20 || Math.Abs(_pitchVelocity) > 10))
            {
                // Инерция с затуханием (~400 мс)
                double targetYaw = _yawAngle + _yawVelocity * 0.16;
                double targetPitch = Math.Clamp(_pitchAngle + _pitchVelocity * 0.12, -20.0, 20.0);
                AnimateTo(targetYaw, targetPitch, TimeSpan.FromMilliseconds(400));
            }
        }
    }

    public void ResetToFrontSmooth()
    {
        _inertiaStoryboard?.Stop();

        // Нормализация угла для кратчайшего доворота к 0°
        double normYaw = _rotY.Angle % 360.0;
        if (normYaw > 180.0) normYaw -= 360.0;
        else if (normYaw < -180.0) normYaw += 360.0;

        _rotY.Angle = normYaw;
        _yawAngle = normYaw;

        AnimateTo(0.0, 0.0, TimeSpan.FromMilliseconds(400));
    }

    private void AnimateTo(double targetYaw, double targetPitch, TimeSpan duration)
    {
        _inertiaStoryboard?.Stop();

        var animYaw = new DoubleAnimation
        {
            From = _rotY.Angle,
            To = targetYaw,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var animPitch = new DoubleAnimation
        {
            From = _rotX.Angle,
            To = targetPitch,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(animYaw, _rotY);
        Storyboard.SetTargetProperty(animYaw, new PropertyPath(AxisAngleRotation3D.AngleProperty));

        Storyboard.SetTarget(animPitch, _rotX);
        Storyboard.SetTargetProperty(animPitch, new PropertyPath(AxisAngleRotation3D.AngleProperty));

        _inertiaStoryboard = new Storyboard();
        _inertiaStoryboard.Children.Add(animYaw);
        _inertiaStoryboard.Children.Add(animPitch);

        _inertiaStoryboard.Completed += (s, e) =>
        {
            _yawAngle = targetYaw;
            _pitchAngle = targetPitch;
            _rotY.Angle = targetYaw;
            _rotX.Angle = targetPitch;
        };

        _inertiaStoryboard.Begin();
    }

    // ==========================================
    // ПРОГРАММНЫЕ МЕТОДЫ ДЛЯ ТЕСТОВ И СКРИНШОТОВ
    // ==========================================

    public void RotateTo(double yaw, double pitch = 0)
    {
        _inertiaStoryboard?.Stop();
        _yawAngle = yaw;
        _pitchAngle = pitch;
        _rotY.Angle = yaw;
        _rotX.Angle = pitch;
    }

    public void RotateToFront() => RotateTo(0, 0);
    public void RotateToSide() => RotateTo(90, 0);
    public void RotateToBack() => RotateTo(180, 0);
}
