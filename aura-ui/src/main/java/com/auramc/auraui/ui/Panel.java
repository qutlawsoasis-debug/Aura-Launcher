package com.auramc.auraui.ui;

import com.auramc.auraui.render.AuraRenderer;

public class Panel extends Component {
    public float radius = Theme.RADIUS_LG;
    public int backgroundColor = Theme.PANEL_GLASS;
    public int borderColor = Theme.PANEL_BORDER;
    public boolean withHighlight = true;

    @Override
    public void render(float mouseX, float mouseY, float delta) {
        if (!visible) return;

        // Shadow
        AuraRenderer.shadow(x, y, width, height, radius, 28.0f, 0.0f, Theme.rgba(0, 0, 0, 0.35f));

        // Glass background
        AuraRenderer.roundRect(x, y, width, height, radius, backgroundColor);

        // Subtle top highlight (sheen)
        if (withHighlight && height > 20) {
            AuraRenderer.gradient(x + 1, y + 1, width - 2, 24.0f, radius - 1,
                    Theme.rgba(255, 255, 255, 0.08f), Theme.rgba(255, 255, 255, 0.0f));
        }

        // Outline
        if (borderColor != 0) {
            AuraRenderer.outline(x, y, width, height, radius, 1.0f, borderColor);
        }
    }
}
