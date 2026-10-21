package com.auramc.auraui;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/** Общие константы мода. Точки входа не нужны: всё подключается миксинами. */
public final class AuraUi {
    public static final Logger LOGGER = LoggerFactory.getLogger("aura-ui");

    /** systemCyan — акцент «Острова» (#64D2FF). */
    public static final float ACCENT_R = 100 / 255f;
    public static final float ACCENT_G = 210 / 255f;
    public static final float ACCENT_B = 255 / 255f;

    private AuraUi() {
    }
}
