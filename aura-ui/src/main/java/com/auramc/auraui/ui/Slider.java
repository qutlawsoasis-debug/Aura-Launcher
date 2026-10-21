package com.auramc.auraui.ui;

import com.auramc.auraui.render.AuraRenderer;

public class Slider extends Component {
    public float value = 0.5f; // 0.0 .. 1.0
    public java.util.function.Consumer<Float> onChange;
    private boolean dragging = false;

    public Slider(float value, java.util.function.Consumer<Float> onChange) {
        this.value = Math.max(0.0f, Math.min(1.0f, value));
        this.onChange = onChange;
        this.width = 160.0f;
        this.height = 18.0f;
    }

    @Override
    public void render(float mouseX, float mouseY, float delta) {
        if (!visible) return;

        if (dragging) {
            float rel = (mouseX - x) / width;
            value = Math.max(0.0f, Math.min(1.0f, rel));
            if (onChange != null) onChange.accept(value);
        }

        float trackH = 6.0f;
        float trackY = y + (height - trackH) * 0.5f;

        // Base track
        AuraRenderer.roundRect(x, trackY, width, trackH, trackH * 0.5f, Theme.FILL_2);

        // Filled track
        float fillW = width * value;
        if (fillW > trackH) {
            AuraRenderer.roundRect(x, trackY, fillW, trackH, trackH * 0.5f, Theme.ACCENT);
        }

        // Thumb
        float thumbRadius = 7.0f;
        float thumbX = x + fillW;
        float thumbY = y + height * 0.5f;
        AuraRenderer.shadow(thumbX - thumbRadius, thumbY - thumbRadius, thumbRadius * 2, thumbRadius * 2,
                thumbRadius, 4.0f, 0.0f, Theme.rgba(0, 0, 0, 0.3f));
        AuraRenderer.roundRect(thumbX - thumbRadius, thumbY - thumbRadius, thumbRadius * 2, thumbRadius * 2,
                thumbRadius, 0xFFFFFFFF);
    }

    @Override
    public boolean mouseClicked(double mouseX, double mouseY, int button) {
        if (visible && enabled && isHovered((float) mouseX, (float) mouseY) && button == 0) {
            dragging = true;
            float rel = ((float) mouseX - x) / width;
            value = Math.max(0.0f, Math.min(1.0f, rel));
            if (onChange != null) onChange.accept(value);
            return true;
        }
        return false;
    }

    @Override
    public boolean mouseReleased(double mouseX, double mouseY, int button) {
        if (dragging && button == 0) {
            dragging = false;
            return true;
        }
        return false;
    }
}
