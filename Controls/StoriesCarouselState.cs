using System.Collections.Generic;
using System.Windows.Controls;

namespace AuraLauncher.Controls;

/// <summary>
/// Единый класс состояния карусели историй: список слайдов, активный индекс, токен перехода и состояние паузы.
/// </summary>
public sealed class StoriesCarouselState
{
    public IReadOnlyList<UserControl> Slides { get; }
    public int ActiveIndex { get; set; }
    public long TransitionToken { get; set; }
    public bool IsPaused { get; set; }

    public StoriesCarouselState(IReadOnlyList<UserControl> slides)
    {
        Slides = slides;
        ActiveIndex = 0;
        TransitionToken = 0;
        IsPaused = false;
    }
}
