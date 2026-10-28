package net.auramc.glass.common.adapter;

/**
 * Минимальный кроссверсионный адаптер платформы для Этапа 1.
 * Изолирует версионные различия между Minecraft 1.20.1 и 1.21.1.
 */
public interface AuraPlatformAdapter {

    /**
     * Возвращает честный текущий FPS клиента игры.
     */
    int getCurrentFps();

    /**
     * Воспроизводит стандартный звук клика по кнопке.
     */
    void playClickSound();

    /**
     * Помещает текст в буфер обмена операционной системы.
     */
    void setClipboardString(String text);

    /**
     * Считывает текст из буфера обмена операционной системы.
     */
    String getClipboardString();

    /**
     * Возвращает текущий коэффициент масштабирования GUI (GUI Scale).
     */
    double getGuiScale();

    /**
     * Возвращает ширину экрана в логических единицах GUI.
     */
    int getScaledWidth();

    /**
     * Возвращает высоту экрана в логических единицах GUI.
     */
    int getScaledHeight();

    /**
     * Возвращает текущее системное время в миллисекундах.
     */
    default long getSystemTimeMillis() {
        return System.currentTimeMillis();
    }
}
