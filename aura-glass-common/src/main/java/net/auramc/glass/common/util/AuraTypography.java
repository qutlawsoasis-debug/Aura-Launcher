package net.auramc.glass.common.util;

/**
 * Типографическая абстракция UI Aura.
 * На Этапе 1 предоставляет логические параметры и метрики шрифта Onest TTF.
 * На Этапе 2 послужит мостом к нативному MSDF-шейдеру и атласу без модификации UI компонентов.
 */
public final class AuraTypography {
    private AuraTypography() {}

    /**
     * Стандартная высота строки шрифта Onest в логических единицах GUI.
     */
    public static final int LINE_HEIGHT = 9;

    /**
     * Стандартный базовый размер шрифта (11pt).
     */
    public static final float SIZE_DEFAULT = 11.0f;

    /**
     * Оптимальный размер шрифта для элементов HUD (13pt, ТЗ §3).
     */
    public static final float SIZE_HUD = 13.0f;

    /**
     * Оптимальный размер шрифта для элементов меню и списков (17pt, ТЗ §3).
     */
    public static final float SIZE_MENU = 17.0f;

    /**
     * Оптимальный размер шрифта для заголовков разделов (38pt, ТЗ §3).
     */
    public static final float SIZE_TITLE = 38.0f;

    /**
     * Расчет вертикального центрирования текста в прямоугольной области заданной высоты.
     */
    public static int getCenteredY(int boxY, int boxHeight) {
        return getCenteredY(boxY, boxHeight, SIZE_DEFAULT);
    }

    public static int getCenteredY(int boxY, int boxHeight, float fontSize) {
        // Onest hhea/OS2: ascender .970 em, cap height .707 em; MSDF uses the line-box top.
        return Math.round(boxY + boxHeight * .5f - fontSize * .6165f);
    }

    /**
     * Расчет вертикального центрирования с учетом высоты шрифта.
     */
    public static int getCenteredY(int boxY, int boxHeight, int textHeight) {
        return boxY + Math.max(0, (boxHeight - textHeight) / 2);
    }

    /**
     * Проверяет, задействован ли MSDF-рендер (на Этапе 1 честно возвращает false).
     */
    public static boolean isMsdfActive() {
        return net.auramc.glass.common.render.AuraRenderer.isReady();
    }
}
