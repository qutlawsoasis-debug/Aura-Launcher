package com.auramc.auraui.ui;

import com.auramc.auraui.render.AuraRenderer;
import org.lwjgl.glfw.GLFW;
import org.lwjgl.nanovg.NanoVG;

public class TextField extends Component {
    public String text = "";
    public String placeholder = "";
    public boolean focused = false;
    public java.util.function.Consumer<String> onChange;

    public TextField(String placeholder) {
        this.placeholder = placeholder;
        this.width = 200.0f;
        this.height = 36.0f;
    }

    @Override
    public void render(float mouseX, float mouseY, float delta) {
        if (!visible) return;

        int bg = focused ? Theme.PANEL_GLASS_HOVER : Theme.PANEL_GLASS;
        int border = focused ? Theme.ACCENT : Theme.PANEL_BORDER;

        AuraRenderer.roundRect(x, y, width, height, Theme.RADIUS_MD, bg);
        AuraRenderer.outline(x, y, width, height, Theme.RADIUS_MD, 1.0f, border);

        float fontSize = 14.0f;
        float textY = y + height * 0.5f + fontSize * 0.35f;
        float textX = x + 12.0f;

        if (text == null || text.isEmpty()) {
            AuraRenderer.text(placeholder, textX, textY, fontSize, Theme.TEXT_DIM, NanoVG.NVG_ALIGN_LEFT);
        } else {
            AuraRenderer.text(text, textX, textY, fontSize, Theme.TEXT_PRIMARY, NanoVG.NVG_ALIGN_LEFT);
        }

        // Blinking cursor
        if (focused && (System.currentTimeMillis() % 1000 < 500)) {
            float tw = text == null || text.isEmpty() ? 0 : AuraRenderer.textWidth(text, fontSize);
            AuraRenderer.rect(textX + tw + 2.0f, y + 8.0f, 1.5f, height - 16.0f, Theme.ACCENT);
        }
    }

    @Override
    public boolean mouseClicked(double mouseX, double mouseY, int button) {
        if (visible && enabled && button == 0) {
            focused = isHovered((float) mouseX, (float) mouseY);
            return focused;
        }
        return false;
    }

    @Override
    public boolean charTyped(char chr, int modifiers) {
        if (focused && enabled && !Character.isISOControl(chr)) {
            text += chr;
            if (onChange != null) onChange.accept(text);
            return true;
        }
        return false;
    }

    @Override
    public boolean keyPressed(int keyCode, int scanCode, int modifiers) {
        if (focused && enabled) {
            if (keyCode == GLFW.GLFW_KEY_BACKSPACE && !text.isEmpty()) {
                text = text.substring(0, text.length() - 1);
                if (onChange != null) onChange.accept(text);
                return true;
            }
            if (keyCode == GLFW.GLFW_KEY_ENTER || keyCode == GLFW.GLFW_KEY_ESCAPE) {
                focused = false;
                return true;
            }
        }
        return false;
    }
}
