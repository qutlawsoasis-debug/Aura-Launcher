using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AuraLauncher.Controls;

public partial class MusicDiskWidget : UserControl
{
    private const double ArmRestAngle = -24; // тонарм отведён (пауза)
    private readonly AnimationClock _spinClock;

    public MusicDiskWidget()
    {
        InitializeComponent();

        // Один бесконечный клок: Pause/Resume сохраняют угол, пластинка не «прыгает»
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(6)) { RepeatBehavior = RepeatBehavior.Forever };
        _spinClock = spin.CreateClock();
        SpinRotate.ApplyAnimationClock(RotateTransform.AngleProperty, _spinClock);
        _spinClock.Controller?.Pause();

        Loaded += (_, _) => ApplyPlayState(animateArm: false);
    }

    #region Dependency properties

    public static readonly DependencyProperty TitleProperty = Reg(nameof(Title), typeof(string), "");
    public static readonly DependencyProperty ArtistProperty = Reg(nameof(Artist), typeof(string), "");
    public static readonly DependencyProperty CoverProperty = Reg(nameof(Cover), typeof(ImageSource), null);
    public static readonly DependencyProperty PositionProperty = Reg(nameof(Position), typeof(TimeSpan), TimeSpan.Zero);
    public static readonly DependencyProperty DurationProperty = Reg(nameof(Duration), typeof(TimeSpan), TimeSpan.Zero);
    public static readonly DependencyProperty PlayPauseCommandProperty = Reg(nameof(PlayPauseCommand), typeof(ICommand), null);
    public static readonly DependencyProperty PreviousCommandProperty = Reg(nameof(PreviousCommand), typeof(ICommand), null);
    public static readonly DependencyProperty NextCommandProperty = Reg(nameof(NextCommand), typeof(ICommand), null);

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying), typeof(bool), typeof(MusicDiskWidget),
        new PropertyMetadata(false, (d, _) => ((MusicDiskWidget)d).ApplyPlayState(animateArm: true)));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Artist { get => (string)GetValue(ArtistProperty); set => SetValue(ArtistProperty, value); }
    public ImageSource? Cover { get => (ImageSource?)GetValue(CoverProperty); set => SetValue(CoverProperty, value); }
    public TimeSpan Position { get => (TimeSpan)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public TimeSpan Duration { get => (TimeSpan)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    public ICommand? PlayPauseCommand { get => (ICommand?)GetValue(PlayPauseCommandProperty); set => SetValue(PlayPauseCommandProperty, value); }
    public ICommand? PreviousCommand { get => (ICommand?)GetValue(PreviousCommandProperty); set => SetValue(PreviousCommandProperty, value); }
    public ICommand? NextCommand { get => (ICommand?)GetValue(NextCommandProperty); set => SetValue(NextCommandProperty, value); }

    private static DependencyProperty Reg(string name, Type type, object? def) =>
        DependencyProperty.Register(name, type, typeof(MusicDiskWidget), new PropertyMetadata(def));

    #endregion

    private void ApplyPlayState(bool animateArm)
    {
        if (!IsLoaded) return;

        if (IsPlaying) _spinClock.Controller?.Resume();
        else _spinClock.Controller?.Pause();

        var target = IsPlaying ? 0 : ArmRestAngle;
        if (!animateArm)
        {
            ArmRotate.Angle = target;
            return;
        }

        ArmRotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(900))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
    }
}
