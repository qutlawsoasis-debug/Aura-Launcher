package net.auramc.glass.client.ui;

/**
 * Aura Client UI Theme: Apple-inspired visionOS-style translucent glass language.
 */
public final class AuraUiTheme {
    private AuraUiTheme() {}

    // Translucent glass backgrounds
    public static final int COLOR_BG_VOID = 0xFF0A0D14;
    public static final int COLOR_BG_PANEL = 0x44101520;
    public static final int COLOR_BG_GRADIENT_TOP = 0x660A0E17;
    public static final int COLOR_BG_GRADIENT_BOTTOM = 0x8806080F;

    // Translucent glass widgets (no opaque matte black)
    public static final int WIDGET_BG_NORMAL = 0x33121824;
    public static final int WIDGET_BG_HOVER = 0x551E2838;
    public static final int WIDGET_BG_DISABLED = 0x1A0D121A;

    public static final int WIDGET_BORDER_NORMAL = 0x30FFFFFF;
    public static final int WIDGET_BORDER_HOVER = 0x8040C4FF;
    public static final int WIDGET_BORDER_DISABLED = 0x15FFFFFF;

    // Typography
    public static final int TEXT_WHITE = 0xFFFFFFFF;
    public static final int TEXT_NORMAL = 0xFFF0F4F8;
    public static final int TEXT_MUTED = 0xFF9CA3AF;
    public static final int TEXT_DISABLED = 0x559CA3AF;

    // Accents & progress
    public static final int ACCENT_CYAN = 0xFF40C4FF;
    public static final int ACCENT_CYAN_MUTED = 0x6640C4FF;
    public static final int PROGRESS_TRACK = 0x44FFFFFF;
}
