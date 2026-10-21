package com.auramc.auraui.ui;

import com.auramc.auraui.render.AuraRenderer;
import org.lwjgl.nanovg.NanoVG;

public class Tooltip extends Component {
    public String text = "";

    public Tooltip(String text) {
        this.text = text;
    }

    @Override
    public void render(float mouseX, float mouseY, float delta) {
        if (!visible || text == null || text.isEmpty()) return;

        float fontSize = 13.0f;
        float tw = AuraRenderer.textWidth(text, fontSize);
        width = tw + 20.0f;
        height = 28.0f;

        x = mouseX + 12.0f;
        y = mouseY + 12.0f;

        AuraRenderer.shadow(x, y, width, height, Theme.RADIUS_SM, 12.0f, 0.0f, Theme.rgba(0, 0, 0, 0.4f));
        AuraRenderer.roundRect(x, y, width, height, Theme.RADIUS_SM, Theme.PANEL_GLASS_HOVER);
        AuraRenderer.outline(x, y, width, height, Theme.RADIUS_SM, 1.0f, Theme.PANEL_BORDER);

        AuraRenderer.text(text, x + width * 0.5f, y + height * 0.5f + fontSize * 0.35f,
                fontSize, Theme.TEXT_PRIMARY, NanoVG.NVG_ALIGN_CENTER);
    }
}
