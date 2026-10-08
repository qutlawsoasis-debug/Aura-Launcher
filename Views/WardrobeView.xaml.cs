using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Microsoft.Extensions.DependencyInjection;
using AuraLauncher.Behaviors;
using AuraLauncher.Core;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.Services.Implementations;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Views;

/// <summary>
/// Логика взаимодействия для WardrobeView.xaml:
/// Настоящий 3D-просмотр скина игрока с живой анимацией взгляда/дыхания и вращением мышью на 360°.
/// </summary>
public partial class WardrobeView : UserControl
{
    private readonly AxisAngleRotation3D _rotY = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D _rotX = new(new Vector3D(1, 0, 0), 0);
    private WardrobeViewModel? _hookedVm;

    public WardrobeView()
    {
        InitializeComponent();

        AlivePlayer3D.SetBaseYaw(SkinDragArea, -14.0);
        AlivePlayer3D.SetBasePitch(SkinDragArea, 4.0);
        AlivePlayer3D.SetPhaseSeed(SkinDragArea, "WardrobeProfile");

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

    private void OnNickInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (DataContext is WardrobeViewModel vm)
            {
                _ = vm.ApplyNicknameChangeAsync();
            }
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnNickInputLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (DataContext is WardrobeViewModel vm)
            {
                _ = vm.ApplyNicknameChangeAsync();
            }
        }
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

    public bool IsDiagnosticMode { get; set; }

    public void Refresh3DModel()
    {
        try
        {
            WardrobeViewModel? vm = DataContext as WardrobeViewModel;
            if (vm == null && DataContext is MainViewModel mvm)
            {
                vm = mvm.WardrobeVM;
            }

            bool isSlim = vm?.IsSlimModel ?? false;
            if (IsDiagnosticMode)
            {
                AlivePlayer3D.SetSourceModel(SkinDragArea, null);
                var transformGroup = new Transform3DGroup();
                transformGroup.Children.Add(new RotateTransform3D(_rotY));
                transformGroup.Children.Add(new RotateTransform3D(_rotX));
                PlayerModelVisual.Transform = transformGroup;
                PlayerModelVisual.Content = SkinModel3DBuilder.BuildDiagnosticPlayerModel(isSlim);
                return;
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

            var modelGroup = SkinModel3DBuilder.BuildPlayerModel(skinBmp, isSlim);
            AlivePlayer3D.SetSourceModel(SkinDragArea, modelGroup);
        }
        catch (Exception ex)
        {
            App.Log($"[3D-SKIN: ERROR] Failed to build 3D player model: {ex.Message}");
        }
    }

    public void ResetToFrontSmooth()
    {
        AlivePlayer3D.ResetToBaseSmooth(SkinDragArea);
    }

    public void RotateTo(double yaw, double pitch = 0)
    {
        _rotY.Angle = yaw;
        _rotX.Angle = pitch;
        AlivePlayer3D.RotateTo(SkinDragArea, yaw, pitch);
    }

    public void RotateToFront() => RotateTo(0, 0);
    public void RotateToSide() => RotateTo(90, 0);
    public void RotateToBack() => RotateTo(180, 0);
}
