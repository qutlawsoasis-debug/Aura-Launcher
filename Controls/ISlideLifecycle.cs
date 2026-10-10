namespace AuraLauncher.Controls;

/// <summary>
/// Интерфейс жизненного цикла слайда карусели для запуска и остановки локальных эффектов (зум, прогресс).
/// </summary>
public interface ISlideLifecycle
{
    void OnEntered();
    void OnExited();
}
