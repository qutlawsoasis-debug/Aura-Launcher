package com.auramc.auraui.ui;

import com.auramc.auraui.render.AuraRenderer;
import org.lwjgl.nanovg.NanoVG;

public class Button extends Component {
    public enum Type {
        REGULAR, PRIMARY, DANGER
    }

    public String text = "";
    public Type type = Type.REGULAR;
    public float radius = Theme.RADIUS_PILL;
    public Runnable onClick;

    private float hoverProgress = 0.0f;
    private boolean pressed = false;

    public Button(String text, Type type, Runnable onClick) {
        this.text = text;
        this.type = type;
        this.onClick = onClick;
    }

    @Override
    public void render(float mouseX, float mouseY, float delta) {
        if (!visible) return;

        boolean hovered = isHovered(mouseX, mouseY) && enabled;
        float target = hovered ? 1.0f : 0.0f;
        hoverProgress = Anim.lerp(hoverProgress, target, delta * 12.0f);

        float translateY = (hovered ? -1.0f : 0.0f) + (pressed ? 1.0f : 0.0f);
        float ry = y + translateY;

        int bg;
        int border;
        int textColor = Theme.TEXT_PRIMARY;

        if (type == Type.PRIMARY) {
            bg = Anim.lerpColor(Theme.rgba(100, 210, 255, 0.20f), Theme.rgba(100, 210, 255, 0.32f), hoverProgress);
            border = Anim.lerpColor(Theme.rgba(100, 210, 255, 0.45f), Theme.ACCENT, hoverProgress);
            if (hoverProgress > 0.01f) {
                AuraRenderer.shadow(x, ry, width, height, radius, 18.0f * hoverProgress, 0.0f,
                        Theme.rgba(100, 210, 255, 0.35f * hoverProgress));
            }
        } else if (type == Type.DANGER) {
            bg = Anim.lerpColor(Theme.PANEL_GLASS, Theme.PANEL_GLASS_HOVER, hoverProgress);
            border = Anim.lerpColor(Theme.PANEL_BORDER, Theme.rgba(255, 95, 87, 0.6f), hoverProgress);
            textColor = Anim.lerpColor(Theme.TEXT_PRIMARY, 0xFFFF8F87, hoverProgress);
            if (hoverProgress > 0.01f) {
                AuraRenderer.shadow(x, ry, width, height, radius, 14.0f * hoverProgress, 0.0f,
                        Theme.rgba(255, 95, 87, 0.25f * hoverProgress));
            }
        } else {
            bg = Anim.lerpColor(Theme.PANEL_GLASS, Theme.PANEL_GLASS_HOVER, hoverProgress);
            border = Anim.lerpColor(Theme.PANEL_BORDER, Theme.rgba(100, 210, 255, 0.5f), hoverProgress);
            if (hoverProgress > 0.01f) {
                AuraRenderer.shadow(x, ry, width, height, radius, 16.0f * hoverProgress, 0.0f,
                        Theme.rgba(100, 210, 255, 0.20f * hoverProgress));
            }
        }

        AuraRenderer.roundRect(x, ry, width, height, radius, bg);
        AuraRenderer.outline(x, ry, width, height, radius, 1.0f, border);

        // Sheen
        AuraRenderer.gradient(x + 1, ry + 1, width - 2, height * 0.5f, radius - 1,
                Theme.rgba(255, 255, 255, 0.06f), Theme.rgba(255, 255, 255, 0.0f));

        // Text
        float fontSize = 15.0f;
        AuraRenderer.text(text, x + width * 0.5f, ry + height * 0.5f + fontSize * 0.35f,
                fontSize, textColor, NanoVG.NVG_ALIGN_CENTER);
    }

    @Override
    public boolean mouseClicked(double mouseX, double mouseY, int button) {
        if (visible && enabled && isHovered((float) mouseX, (float) mouseY) && button == 0) {
            pressed = true;
            return true;
        }
        return false;
    }

    @Override
    public boolean mouseReleased(double mouseX, double mouseY, int button) {
        if (pressed && button == 0) {
            pressed = false;
            if (visible && enabled && isHovered((float) mouseX, (float) mouseY)) {
                if (onClick != null) onClick.run();
                return true;
            }
        }
        return false;
    }
}
