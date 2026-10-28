package net.auramc.glass.common.adapter;

/**
 * Графический бэкенд для отрисовки компонентов UI Aura.
 * Изолирует общую модель UI от версионных различий DrawContext / RenderSystem.
 * Включает методы отрисовки скруглений (radius) и замера текста для Этапа 1.
 */
public interface IAuraRenderBackend {
    default void fillRoundedRect(float x, float y, float width, float height, float radius, int colorArgb) {
        fillRoundedRect(Math.round(x), Math.round(y), Math.round(width), Math.round(height), Math.round(radius), colorArgb);
    }

    default void drawText(String text, float x, float y, float fontSize, int colorArgb, boolean shadow) {
        drawText(text, Math.round(x), Math.round(y), fontSize, colorArgb, shadow);
    }

    default void shadow(int x, int y, int w, int h, int radius, int color) {}

    /**
     * Заливка прямоугольника заданным ARGB цветом (без скругления).
     */
    void fillRect(int x1, int y1, int x2, int y2, int colorArgb);

    /**
     * Заливка скруглённого прямоугольника заданным радиусом и ARGB цветом.
     */
    void fillRoundedRect(int x, int y, int width, int height, int radius, int colorArgb);

    /**
     * Отрисовка прямоугольной контурной рамки толщиной 1px.
     */
    void drawBorder(int x, int y, int width, int height, int colorArgb);

    /**
     * Отрисовка скруглённой контурной рамки толщиной 1px.
     */
    void drawRoundedBorder(int x, int y, int width, int height, int radius, int colorArgb);

    /**
     * Отрисовка строки текста с указанием размера и опциональной тенью.
     * @param fontSize размер шрифта в pt (например, AuraTypography.SIZE_HUD или SIZE_MENU)
     */
    void drawText(String text, int x, int y, float fontSize, int colorArgb, boolean shadow);

    /**
     * Отрисовка строки текста стандартного размера (11pt) с опциональной тенью.
     */
    default void drawText(String text, int x, int y, int colorArgb, boolean shadow) {
        drawText(text, x, y, 11.0f, colorArgb, shadow);
    }

    /**
     * Отрисовка строки текста с центрированием по горизонтали.
     */
    void drawCenteredText(String text, int centerX, int y, float fontSize, int colorArgb, boolean shadow);

    /**
     * Отрисовка строки текста стандартного размера (11pt) с центрированием по горизонтали.
     */
    default void drawCenteredText(String text, int centerX, int y, int colorArgb, boolean shadow) {
        drawCenteredText(text, centerX, y, 11.0f, colorArgb, shadow);
    }

    /**
     * Возвращает ширину строки в логических пикселях GUI при заданном размере шрифта.
     */
    int getTextWidth(String text, float fontSize);

    /**
     * Возвращает ширину строки в логических пикселях GUI (стандартный размер).
     */
    default int getTextWidth(String text) {
        return getTextWidth(text, 11.0f);
    }

    /**
     * Возвращает высоту строки шрифта в логических пикселях GUI.
     */
    int getTextHeight(float fontSize);

    /**
     * Включает аппаратное отсечение области (scissor).
     */
    void enableScissor(int x, int y, int width, int height);

    /**
     * Отключает аппаратное отсечение области (scissor).
     */
    void disableScissor();
}
