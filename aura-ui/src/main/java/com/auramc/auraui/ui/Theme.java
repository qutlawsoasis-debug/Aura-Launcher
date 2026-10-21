package com.auramc.auraui.ui;

public final class Theme {
    private Theme() {}

    // Fonts
    public static final String FONT_ONEST = "Onest";

    // Palette
    public static final int ACCENT = 0xFF64D2FF;            // #64D2FF
    public static final int ACCENT_HOVER = 0xFF8EDEFF;      // #8EDEFF
    public static final int ACCENT_ACTIVE = 0xFF50BCE8;     // #50BCE8
    public static final int ACCENT_INK = 0xFF00283A;        // #00283A
    public static final int ACCENT_SOFT = rgba(100, 210, 255, 0.18f);

    public static final int DANGER = 0xFFFF8A8A;            // #FF8A8A
    public static final int DANGER_HOVER = 0xFFFF6B6B;
    public static final int SUCCESS = 0xFF34C759;           // #34C759

    // Inks / Text
    public static final int TEXT_PRIMARY = 0xFFFFFFFF;
    public static final int TEXT_SECONDARY = rgba(255, 255, 255, 0.76f);
    public static final int TEXT_TERTIARY = rgba(255, 255, 255, 0.56f);
    public static final int TEXT_DIM = rgba(255, 255, 255, 0.45f);

    // Apple Liquid Glass fills
    public static final int FILL_1 = rgba(118, 118, 128, 0.24f);
    public static final int FILL_2 = rgba(118, 118, 128, 0.36f);
    public static final int FILL_3 = rgba(118, 118, 128, 0.50f);

    // Panel & Buttons
    public static final int PANEL_GLASS = rgba(18, 22, 32, 0.72f);
    public static final int PANEL_GLASS_HOVER = rgba(28, 34, 48, 0.85f);
    public static final int PANEL_BORDER = rgba(255, 255, 255, 0.14f);
    public static final int PANEL_BORDER_HOVER = rgba(100, 210, 255, 0.50f);

    public static final int LINE = rgba(255, 255, 255, 0.22f);
    public static final int HAIR = rgba(255, 255, 255, 0.10f);

    // Radii
    public static final float RADIUS_SM = 8.0f;
    public static final float RADIUS_MD = 14.0f;
    public static final float RADIUS_LG = 18.0f;
    public static final float RADIUS_PILL = 999.0f;

    public static int rgba(int r, int g, int b, float a) {
        int ia = Math.min(255, Math.max(0, (int) (a * 255f)));
        return (ia << 24) | ((r & 0xFF) << 16) | ((g & 0xFF) << 8) | (b & 0xFF);
    }

    public static int withAlpha(int color, float a) {
        int ia = Math.min(255, Math.max(0, (int) (a * 255f)));
        return (ia << 24) | (color & 0x00FFFFFF);
    }
}
