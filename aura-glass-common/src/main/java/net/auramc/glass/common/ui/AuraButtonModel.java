package net.auramc.glass.common.ui;

import net.auramc.glass.common.adapter.AuraPlatform;
import net.auramc.glass.common.theme.AuraTokens;

/**
 * Чистая платформонезависимая модель кнопки Aura UI.
 * Управляет хитбоксами, состояниями (normal, hover, pressed, focused, disabled) и кликами.
 */
public class AuraButtonModel {
    private int x;
    private int y;
    private int width;
    private int height;
    private String label;
    private boolean active = true;
    private boolean focused = false;
    private boolean pressed = false;
    private Runnable onPress;

    public AuraButtonModel(int x, int y, int width, int height, String label, Runnable onPress) {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
        this.label = label != null ? label : "";
        this.onPress = onPress;
    }

    public boolean isMouseOver(double mouseX, double mouseY) {
        return active && mouseX >= x && mouseX < x + width && mouseY >= y && mouseY < y + height;
    }

    public AuraWidgetState resolveState(boolean isHovered) {
        if (!active) return AuraWidgetState.DISABLED;
        if (pressed) return AuraWidgetState.PRESSED;
        if (focused) return AuraWidgetState.FOCUSED;
        if (isHovered) return AuraWidgetState.HOVER;
        return AuraWidgetState.NORMAL;
    }

    public boolean onMouseClicked(double mouseX, double mouseY, int button) {
        if (button == 0 && isMouseOver(mouseX, mouseY)) {
            this.pressed = true;
            this.focused = true;
            return true;
        }
        return false;
    }

    public boolean onMouseReleased(double mouseX, double mouseY, int button) {
        if (button == 0 && pressed) {
            pressed = false;
            if (isMouseOver(mouseX, mouseY)) {
                AuraPlatform.get().playClickSound();
                if (onPress != null) {
                    onPress.run();
                }
                return true;
            }
        }
        return false;
    }

    public void cancelPress() {
        pressed = false;
    }

    public boolean onKeyPressed(int keyCode, int scanCode, int modifiers) {
        if (!active || !focused) return false;
        // 257 = GLFW_KEY_ENTER, 32 = GLFW_KEY_SPACE
        if (keyCode == 257 || keyCode == 32) {
            AuraPlatform.get().playClickSound();
            if (onPress != null) {
                onPress.run();
            }
            return true;
        }
        return false;
    }

    // --- Геттеры и сеттеры ---
    public int getX() { return x; }
    public void setX(int x) { this.x = x; }

    public int getY() { return y; }
    public void setY(int y) { this.y = y; }

    public int getWidth() { return width; }
    public void setWidth(int width) { this.width = width; }

    public int getHeight() { return height; }
    public void setHeight(int height) { this.height = height; }

    public String getLabel() { return label; }
    public void setLabel(String label) { this.label = label != null ? label : ""; }

    public boolean isActive() { return active; }
    public void setActive(boolean active) { this.active = active; if (!active) focused = false; }

    public boolean isFocused() { return focused; }
    public void setFocused(boolean focused) { this.focused = active && focused; }

    public int getRadius() { return AuraTokens.RADIUS_BUTTON; }
}
