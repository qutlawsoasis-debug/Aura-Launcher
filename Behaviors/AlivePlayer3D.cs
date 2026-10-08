using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AuraLauncher.Core;

namespace AuraLauncher.Behaviors;

public static class AlivePlayer3D
{
    public static readonly DependencyProperty SourceModelProperty =
        DependencyProperty.RegisterAttached(
            "SourceModel",
            typeof(Model3DGroup),
            typeof(AlivePlayer3D),
            new PropertyMetadata(null, OnSourceModelChanged));

    public static readonly DependencyProperty BaseYawProperty =
        DependencyProperty.RegisterAttached(
            "BaseYaw",
            typeof(double),
            typeof(AlivePlayer3D),
            new PropertyMetadata(-14.0, OnBaseAnglesChanged));

    public static readonly DependencyProperty BasePitchProperty =
        DependencyProperty.RegisterAttached(
            "BasePitch",
            typeof(double),
            typeof(AlivePlayer3D),
            new PropertyMetadata(4.0, OnBaseAnglesChanged));

    public static readonly DependencyProperty PhaseSeedProperty =
        DependencyProperty.RegisterAttached(
            "PhaseSeed",
            typeof(string),
            typeof(AlivePlayer3D),
            new PropertyMetadata(null, OnPhaseSeedChanged));

    private static readonly DependencyProperty ControllerProperty =
        DependencyProperty.RegisterAttached(
            "Controller",
            typeof(PlayerRigController),
            typeof(AlivePlayer3D),
            new PropertyMetadata(null));

    public static Model3DGroup? GetSourceModel(DependencyObject obj) => (Model3DGroup?)obj.GetValue(SourceModelProperty);
    public static void SetSourceModel(DependencyObject obj, Model3DGroup? value) => obj.SetValue(SourceModelProperty, value);

    public static double GetBaseYaw(DependencyObject obj) => (double)obj.GetValue(BaseYawProperty);
    public static void SetBaseYaw(DependencyObject obj, double value) => obj.SetValue(BaseYawProperty, value);

    public static double GetBasePitch(DependencyObject obj) => (double)obj.GetValue(BasePitchProperty);
    public static void SetBasePitch(DependencyObject obj, double value) => obj.SetValue(BasePitchProperty, value);

    public static string? GetPhaseSeed(DependencyObject obj) => (string?)obj.GetValue(PhaseSeedProperty);
    public static void SetPhaseSeed(DependencyObject obj, string? value) => obj.SetValue(PhaseSeedProperty, value);

    public static void RotateTo(FrameworkElement host, double yaw, double pitch = 0.0)
    {
        var controller = EnsureController(host);
        controller.SetExactAngles(yaw, pitch);
    }

    public static void ResetToBaseSmooth(FrameworkElement host)
    {
        var controller = EnsureController(host);
        controller.TriggerReturnToBase();
    }

    private static PlayerRigController EnsureController(FrameworkElement host)
    {
        if (host.GetValue(ControllerProperty) is not PlayerRigController controller)
        {
            controller = new PlayerRigController(host);
            host.SetValue(ControllerProperty, controller);
        }
        return controller;
    }

    private static void OnSourceModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement host)
        {
            var controller = EnsureController(host);
            controller.SetSourceModel(e.NewValue as Model3DGroup);
        }
    }

    private static void OnBaseAnglesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement host)
        {
            var controller = EnsureController(host);
            controller.UpdateBaseAngles(GetBaseYaw(host), GetBasePitch(host));
        }
    }

    private static void OnPhaseSeedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement host)
        {
            var controller = EnsureController(host);
            controller.UpdatePhaseSeed(e.NewValue as string);
        }
    }

    private sealed class PlayerRigController
    {
        private readonly FrameworkElement _host;
        private Viewport3D? _viewport;
        private ModelVisual3D? _modelVisual;
        private Model3DGroup? _sourceModel;

        private readonly AxisAngleRotation3D _charYawRotation = new(new Vector3D(0, 1, 0), -14.0);
        private readonly AxisAngleRotation3D _charPitchRotation = new(new Vector3D(1, 0, 0), 4.0);
        private readonly Transform3DGroup _characterRootTransform = new();

        private readonly AxisAngleRotation3D _headYawRotation = new(new Vector3D(0, 1, 0), 0.0);
        private readonly AxisAngleRotation3D _headPitchRotation = new(new Vector3D(1, 0, 0), 0.0);
        private readonly AxisAngleRotation3D _headRollRotation = new(new Vector3D(0, 0, 1), 0.0);

        private readonly AxisAngleRotation3D _upperBodyYawRotation = new(new Vector3D(0, 1, 0), 0.0);
        private readonly AxisAngleRotation3D _upperBodyPitchRotation = new(new Vector3D(1, 0, 0), 0.0);
        private readonly TranslateTransform3D _upperBodyBreathTranslate = new(0, 0, 0);

        private readonly AxisAngleRotation3D _rightArmPitchRotation = new(new Vector3D(1, 0, 0), 0.0);
        private readonly AxisAngleRotation3D _rightArmRollRotation = new(new Vector3D(0, 0, 1), 0.0);

        private readonly AxisAngleRotation3D _leftArmPitchRotation = new(new Vector3D(1, 0, 0), 0.0);
        private readonly AxisAngleRotation3D _leftArmRollRotation = new(new Vector3D(0, 0, 1), 0.0);

        private double _baseYaw = -14.0;
        private double _basePitch = 4.0;
        private double _currentYaw = -14.0;
        private double _currentPitch = 4.0;
        private double _velocityYaw;
        private double _velocityPitch;
        private bool _hasInitializedAngles;
        private bool _isDragging;
        private bool _isReturningToBase;
        private Point _lastMousePos;

        private bool _isRenderingHooked;
        private long _lastFrameTimestamp;
        private double _animTime;
        private uint _rngState = 0x9E3779B9u;

        private double _headCurrentYaw;
        private double _headCurrentPitch;
        private double _headCurrentRoll;
        private double _headTargetYaw;
        private double _headTargetPitch;
        private double _headTargetRoll;
        private double _nextGazeTimer;

        public PlayerRigController(FrameworkElement host)
        {
            _host = host;
            _baseYaw = GetBaseYaw(host);
            _basePitch = GetBasePitch(host);
            _currentYaw = _baseYaw;
            _currentPitch = _basePitch;
            _charYawRotation.Angle = _currentYaw;
            _charPitchRotation.Angle = _currentPitch;

            _characterRootTransform.Children.Add(new RotateTransform3D(_charYawRotation));
            _characterRootTransform.Children.Add(new RotateTransform3D(_charPitchRotation));

            UpdatePhaseSeed(GetPhaseSeed(host));

            _host.Loaded += OnHostLoaded;
            _host.Unloaded += OnHostUnloaded;
            _host.IsVisibleChanged += OnHostIsVisibleChanged;
            _host.MouseLeftButtonDown += OnMouseLeftButtonDown;
            _host.MouseMove += OnMouseMove;
            _host.MouseLeftButtonUp += OnMouseLeftButtonUp;
            _host.LostMouseCapture += OnLostMouseCapture;
        }

        public void UpdatePhaseSeed(string? seed)
        {
            uint hash = 2166136261u;
            if (!string.IsNullOrEmpty(seed))
            {
                foreach (char c in seed)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
            }
            else
            {
                hash ^= (uint)_host.GetHashCode();
            }

            _rngState = hash == 0 ? 0x9E3779B9u : hash;
            _animTime = (NextRandom01() * 12.0);
            _nextGazeTimer = 0.35 + NextRandom01() * 0.9;
        }

        public void UpdateBaseAngles(double baseYaw, double basePitch)
        {
            double prevBaseYaw = _baseYaw;
            _baseYaw = baseYaw;
            _basePitch = basePitch;

            if (!_hasInitializedAngles)
            {
                _currentYaw = baseYaw;
                _currentPitch = basePitch;
                _charYawRotation.Angle = _currentYaw;
                _charPitchRotation.Angle = _currentPitch;
                _hasInitializedAngles = true;
            }
            else if (!_isDragging && Math.Abs( prevBaseYaw - baseYaw) > 0.1)
            {
                _isReturningToBase = true;
            }
        }

        public void SetSourceModel(Model3DGroup? model)
        {
            _sourceModel = model;
            RebuildArticulatedScene();
            UpdateRenderingSubscription();
        }

        public void SetExactAngles(double yaw, double pitch)
        {
            _isDragging = false;
            _isReturningToBase = false;
            _velocityYaw = 0;
            _velocityPitch = 0;
            _currentYaw = NormalizeAngle(yaw);
            _currentPitch = Math.Clamp(pitch, -20.0, 20.0);
            _charYawRotation.Angle = _currentYaw;
            _charPitchRotation.Angle = _currentPitch;
            _hasInitializedAngles = true;
        }

        public void TriggerReturnToBase()
        {
            _isDragging = false;
            _velocityYaw = 0;
            _velocityPitch = 0;
            _isReturningToBase = true;
            UpdateRenderingSubscription();
        }

        private void OnHostLoaded(object sender, RoutedEventArgs e)
        {
            RebuildArticulatedScene();
            UpdateRenderingSubscription();
        }

        private void OnHostUnloaded(object sender, RoutedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                _host.ReleaseMouseCapture();
            }
            UnhookRendering();
        }

        private void OnHostIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateRenderingSubscription();
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                _isDragging = false;
                _velocityYaw = 0;
                _velocityPitch = 0;
                _isReturningToBase = true;
                _host.ReleaseMouseCapture();
                UpdateRenderingSubscription();
                e.Handled = true;
                return;
            }

            _isDragging = true;
            _isReturningToBase = false;
            _velocityYaw = 0;
            _velocityPitch = 0;
            _lastMousePos = e.GetPosition(_host);
            _host.CaptureMouse();
            UpdateRenderingSubscription();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _isDragging = false;
                _host.ReleaseMouseCapture();
                return;
            }

            Point pos = e.GetPosition(_host);
            double dx = pos.X - _lastMousePos.X;
            double dy = pos.Y - _lastMousePos.Y;
            _lastMousePos = pos;

            double deltaYaw = dx * 0.65;
            double deltaPitch = dy * 0.35;

            _currentYaw = NormalizeAngle(_currentYaw + deltaYaw);
            _currentPitch = Math.Clamp(_currentPitch + deltaPitch, -20.0, 20.0);

            _velocityYaw = deltaYaw;
            _velocityPitch = deltaPitch;

            _charYawRotation.Angle = _currentYaw;
            _charPitchRotation.Angle = _currentPitch;
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            _host.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            _isDragging = false;
        }

        private void RebuildArticulatedScene()
        {
            _viewport ??= FindViewport(_host);
            if (_viewport == null) return;

            if (_modelVisual == null)
            {
                foreach (var child in _viewport.Children)
                {
                    if (child is ModelVisual3D mv)
                    {
                        _modelVisual = mv;
                        break;
                    }
                }

                if (_modelVisual == null)
                {
                    _modelVisual = new ModelVisual3D();
                    _viewport.Children.Add(_modelVisual);
                }
            }

            _modelVisual.Transform = _characterRootTransform;

            if (_sourceModel == null)
            {
                _modelVisual.Content = null;
                return;
            }

            bool isSlim = SkinModel3DBuilder.GetIsSlimModel(_sourceModel);
            double rightShoulderX = isSlim ? -5.5 : -6.0;
            double leftShoulderX = isSlim ? 5.5 : 6.0;

            var upperBodyTransform = new Transform3DGroup();
            upperBodyTransform.Children.Add(new RotateTransform3D(_upperBodyPitchRotation, new Point3D(0, -4, 0)));
            upperBodyTransform.Children.Add(new RotateTransform3D(_upperBodyYawRotation, new Point3D(0, -4, 0)));
            upperBodyTransform.Children.Add(_upperBodyBreathTranslate);

            var headTransform = new Transform3DGroup();
            headTransform.Children.Add(new RotateTransform3D(_headRollRotation, new Point3D(0, 8, 0)));
            headTransform.Children.Add(new RotateTransform3D(_headPitchRotation, new Point3D(0, 8, 0)));
            headTransform.Children.Add(new RotateTransform3D(_headYawRotation, new Point3D(0, 8, 0)));
            headTransform.Children.Add(upperBodyTransform);

            var rightArmTransform = new Transform3DGroup();
            rightArmTransform.Children.Add(new RotateTransform3D(_rightArmRollRotation, new Point3D(rightShoulderX, 6, 0)));
            rightArmTransform.Children.Add(new RotateTransform3D(_rightArmPitchRotation, new Point3D(rightShoulderX, 6, 0)));
            rightArmTransform.Children.Add(upperBodyTransform);

            var leftArmTransform = new Transform3DGroup();
            leftArmTransform.Children.Add(new RotateTransform3D(_leftArmRollRotation, new Point3D(leftShoulderX, 6, 0)));
            leftArmTransform.Children.Add(new RotateTransform3D(_leftArmPitchRotation, new Point3D(leftShoulderX, 6, 0)));
            leftArmTransform.Children.Add(upperBodyTransform);

            var liveRoot = new Model3DGroup();
            var liveBaseGroup = new Model3DGroup();
            var liveOverlayGroup = new Model3DGroup();

            int modelGroupIndex = 0;
            foreach (var child in _sourceModel.Children)
            {
                if (child is Light light)
                {
                    liveRoot.Children.Add(light);
                }
                else if (child is Model3DGroup subGroup)
                {
                    var targetContainer = modelGroupIndex == 0 ? liveBaseGroup : liveOverlayGroup;
                    modelGroupIndex++;

                    foreach (var partModel in subGroup.Children)
                    {
                        PlayerLimbPart limbPart = SkinModel3DBuilder.GetLimbPart(partModel);
                        Transform3D? limbTransform = limbPart switch
                        {
                            PlayerLimbPart.Head => headTransform,
                            PlayerLimbPart.Body => upperBodyTransform,
                            PlayerLimbPart.RightArm => rightArmTransform,
                            PlayerLimbPart.LeftArm => leftArmTransform,
                            _ => null
                        };

                        if (limbTransform != null)
                        {
                            var wrapper = new Model3DGroup
                            {
                                Transform = limbTransform
                            };
                            wrapper.Children.Add(partModel);
                            targetContainer.Children.Add(wrapper);
                        }
                        else
                        {
                            targetContainer.Children.Add(partModel);
                        }
                    }
                }
            }

            liveRoot.Children.Add(liveBaseGroup);
            liveRoot.Children.Add(liveOverlayGroup);
            _modelVisual.Content = liveRoot;
        }

        private void UpdateRenderingSubscription()
        {
            bool shouldHook = _host.IsLoaded && _host.IsVisible && _sourceModel != null;
            if (shouldHook && !_isRenderingHooked)
            {
                _lastFrameTimestamp = Stopwatch.GetTimestamp();
                CompositionTarget.Rendering += OnCompositionTargetRendering;
                _isRenderingHooked = true;
            }
            else if (!shouldHook && _isRenderingHooked)
            {
                UnhookRendering();
            }
        }

        private void UnhookRendering()
        {
            if (_isRenderingHooked)
            {
                CompositionTarget.Rendering -= OnCompositionTargetRendering;
                _isRenderingHooked = false;
            }
        }

        private void OnCompositionTargetRendering(object? sender, EventArgs e)
        {
            if (!_host.IsLoaded || !_host.IsVisible || _sourceModel == null)
            {
                UnhookRendering();
                return;
            }

            long now = Stopwatch.GetTimestamp();
            double dt = (double)(now - _lastFrameTimestamp) / Stopwatch.Frequency;
            _lastFrameTimestamp = now;
            if (dt <= 0.0 || dt > 0.1) dt = 1.0 / 60.0;

            double frameScale = dt * 60.0;
            _animTime += dt;

            // 1. Вращение всего персонажа мышью + инерция + возврат по двойному клику
            if (!_isDragging)
            {
                if (_isReturningToBase)
                {
                    double diffYaw = ShortestAngleDelta(_currentYaw, _baseYaw);
                    double diffPitch = _basePitch - _currentPitch;

                    double blend = 1.0 - Math.Pow(1.0 - 0.16, frameScale);
                    _currentYaw = NormalizeAngle(_currentYaw + diffYaw * blend);
                    _currentPitch += diffPitch * blend;

                    if (Math.Abs(diffYaw) < 0.08 && Math.Abs(diffPitch) < 0.08)
                    {
                        _currentYaw = _baseYaw;
                        _currentPitch = _basePitch;
                        _isReturningToBase = false;
                    }

                    _charYawRotation.Angle = _currentYaw;
                    _charPitchRotation.Angle = _currentPitch;
                }
                else if (Math.Abs(_velocityYaw) > 0.01 || Math.Abs(_velocityPitch) > 0.01)
                {
                    _currentYaw = NormalizeAngle(_currentYaw + _velocityYaw * frameScale);
                    _currentPitch = Math.Clamp(_currentPitch + _velocityPitch * frameScale, -20.0, 20.0);

                    _velocityYaw *= Math.Pow(0.90, frameScale);
                    _velocityPitch *= Math.Pow(0.88, frameScale);

                    if (Math.Abs(_velocityYaw) <= 0.01) _velocityYaw = 0;
                    if (Math.Abs(_velocityPitch) <= 0.01) _velocityPitch = 0;

                    _charYawRotation.Angle = _currentYaw;
                    _charPitchRotation.Angle = _currentPitch;
                }
            }

            // 2. Живой взгляд головой по сторонам (как в Minecraft: плавный поворот взгляда + пауза + микро-дыхание)
            _nextGazeTimer -= dt;
            if (_nextGazeTimer <= 0.0)
            {
                PickNextGazeTarget();
            }

            double headSmoothing = 1.0 - Math.Exp(-3.4 * dt);
            _headCurrentYaw += (_headTargetYaw - _headCurrentYaw) * headSmoothing;
            _headCurrentPitch += (_headTargetPitch - _headCurrentPitch) * headSmoothing;
            _headCurrentRoll += (_headTargetRoll - _headCurrentRoll) * headSmoothing;

            double microYaw = Math.Sin(_animTime * 1.15) * 1.4;
            double microPitch = Math.Sin(_animTime * 1.75 + 0.8) * 1.1;

            double finalHeadYaw = _headCurrentYaw + microYaw;
            double finalHeadPitch = _headCurrentPitch + microPitch;
            double finalHeadRoll = _headCurrentRoll + Math.Sin(_animTime * 0.9) * 0.6;

            _headYawRotation.Angle = finalHeadYaw;
            _headPitchRotation.Angle = finalHeadPitch;
            _headRollRotation.Angle = finalHeadRoll;

            // 3. Корпус слегка доворачивается вслед за взглядом головы + живое дыхание грудной клетки
            double breathWave = (Math.Sin(_animTime * 1.75) + 1.0) * 0.5; // 0..1
            _upperBodyYawRotation.Angle = finalHeadYaw * 0.16;
            _upperBodyPitchRotation.Angle = (breathWave - 0.5) * 1.1;
            _upperBodyBreathTranslate.OffsetY = breathWave * 0.12;

            // 4. Классическое живое покачивание рук Minecraft в такт дыханию и повороту корпуса
            double armOutward = 1.8 + breathWave * 2.4;
            double armForwardRight = Math.Sin(_animTime * 1.35) * 3.2 - finalHeadYaw * 0.08;
            double armForwardLeft = -Math.Sin(_animTime * 1.35 + 0.4) * 3.2 + finalHeadYaw * 0.08;

            _rightArmRollRotation.Angle = -armOutward;
            _rightArmPitchRotation.Angle = armForwardRight;

            _leftArmRollRotation.Angle = armOutward;
            _leftArmPitchRotation.Angle = armForwardLeft;
        }

        private void PickNextGazeTarget()
        {
            double roll = NextRandom01();
            if (roll < 0.28)
            {
                // Спокойный взгляд почти прямо перед собой
                _headTargetYaw = (NextRandom01() - 0.5) * 7.0;
                _headTargetPitch = (NextRandom01() - 0.5) * 4.0;
                _headTargetRoll = 0.0;
                _nextGazeTimer = 1.6 + NextRandom01() * 1.8;
            }
            else if (roll < 0.64)
            {
                // Плавный взгляд влево или вправо
                double side = NextRandom01() < 0.5 ? -1.0 : 1.0;
                _headTargetYaw = side * (10.0 + NextRandom01() * 13.0);
                _headTargetPitch = -4.0 + NextRandom01() * 9.0;
                _headTargetRoll = side * (0.8 + NextRandom01() * 1.6);
                _nextGazeTimer = 1.4 + NextRandom01() * 1.6;
            }
            else
            {
                // Любопытный осмотр по сторонам / чуть вверх или вниз
                _headTargetYaw = (NextRandom01() - 0.5) * 36.0;
                _headTargetPitch = -7.0 + NextRandom01() * 14.0;
                _headTargetRoll = (_headTargetYaw / 22.0) * 1.8;
                _nextGazeTimer = 1.1 + NextRandom01() * 1.4;
            }
        }

        private double NextRandom01()
        {
            uint x = _rngState;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _rngState = x;
            return (x & 0x00FFFFFFu) / (double)0x01000000u;
        }

        private static double NormalizeAngle(double angle)
        {
            angle %= 360.0;
            if (angle > 180.0) angle -= 360.0;
            if (angle < -180.0) angle += 360.0;
            return angle;
        }

        private static double ShortestAngleDelta(double from, double to)
        {
            double delta = (to - from) % 360.0;
            if (delta > 180.0) delta -= 360.0;
            if (delta < -180.0) delta += 360.0;
            return delta;
        }

        private static Viewport3D? FindViewport(DependencyObject root)
        {
            if (root is Viewport3D vp) return vp;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindViewport(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }
    }
}
