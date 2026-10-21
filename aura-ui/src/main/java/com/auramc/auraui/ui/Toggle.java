package com.auramc.auraui.ui;

import com.auramc.auraui.render.AuraRenderer;

public class Toggle extends Component {
    public boolean value = false;
    public java.util.function.Consumer<Boolean> onChange;
    private float knobAnim = 0.0f;

    public Toggle(boolean value, java.util.function.Consumer<Boolean> onChange) {
        this.value = value;
        this.onChange = onChange;
        this.knobAnim = value ? 1.0f : 0.0f;
        this.width = 44.0f;
        this.height = 24.0f;
    }

    @Override
    public void render(float mouseX, float mouseY, float delta) {
        if (!visible) return;

        float target = value ? 1.0f : 0.0f;
        knobAnim = Anim.lerp(knobAnim, target, delta * 14.0f);

        int bg = Anim.lerpColor(Theme.FILL_2, Theme.ACCENT, knobAnim);
        AuraRenderer.roundRect(x, y, width, height, Theme.RADIUS_PILL, bg);
        AuraRenderer.outline(x, y, width, height, Theme.RADIUS_PILL, 1.0f, Theme.LINE);

        // Knob
        float knobSize = height - 4.0f;
        float knobX = x + 2.0f + knobAnim * (width - knobSize - 4.0f);
        float knobY = y + 2.0f;

        AuraRenderer.shadow(knobX, knobY, knobSize, knobSize, knobSize * 0.5f, 4.0f, 0.0f, Theme.rgba(0, 0, 0, 0.25f));
        AuraRenderer.roundRect(knobX, knobY, knobSize, knobSize, knobSize * 0.5f, 0xFFFFFFFF);
    }

    @Override
    public boolean mouseClicked(double mouseX, double mouseY, int button) {
        if (visible && enabled && isHovered((float) mouseX, (float) mouseY) && button == 0) {
            value = !value;
            if (onChange != null) onChange.accept(value);
            return true;
        }
        return false;
    }
}
