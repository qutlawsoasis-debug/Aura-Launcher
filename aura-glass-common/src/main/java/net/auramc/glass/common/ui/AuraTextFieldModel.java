package net.auramc.glass.common.ui;

import net.auramc.glass.common.adapter.AuraPlatform;
import net.auramc.glass.common.adapter.IAuraRenderBackend;
import net.auramc.glass.common.theme.AuraTokens;

/**
 * Платформонезависимая модель поля ввода текста (TextField).
 * Поддерживает каретку, выделение, плейсхолдер, буфер обмена,
 * горизонтальную прокрутку (горизонтальный скролл при длинном тексте),
 * позиционирование каретки по клику на основе метрик символов,
 * и сохранение состояния при reflow.
 */
public class AuraTextFieldModel {
    private int x;
    private int y;
    private int width;
    private int height;
    private String text = "";
    private String placeholder = "Поиск...";
    private int cursorPosition = 0;
    private int selectionStart = 0;
    private int selectionEnd = 0;
    private int scrollOffset = 0; // Горизонтальное смещение для длинного текста в пикселях
    private boolean focused = false;
    private boolean active = true;
    private int maxCharacters = 128;

    public AuraTextFieldModel(int x, int y, int width, int height) {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    public boolean isMouseOver(double mouseX, double mouseY) {
        return active && mouseX >= x && mouseX < x + width && mouseY >= y && mouseY < y + height;
    }

    /**
     * Обработка клика мыши с позиционированием каретки по метрикам символов.
     */
    public boolean onMouseClicked(double mouseX, double mouseY, int button, IAuraRenderBackend backend) {
        if (!active) return false;
        if (button == 0) {
            boolean wasOver = isMouseOver(mouseX, mouseY);
            if (wasOver) {
                this.focused = true;
                int textStartX = x + 8;
                int clickRelativeX = (int) (mouseX - textStartX) + scrollOffset;

                // Находим ближайшую позицию символа по метрикам
                this.cursorPosition = findCursorPosition(clickRelativeX, backend);
                this.selectionStart = this.cursorPosition;
                this.selectionEnd = this.cursorPosition;
                ensureCursorVisible(backend);
                return true;
            }
        }
        return false;
    }

    private int findCursorPosition(int relativeX, IAuraRenderBackend backend) {
        if (relativeX <= 0 || text.isEmpty()) return 0;
        if (backend == null) return Math.min(relativeX / 6, text.length());

        int closestPos = 0;
        int minDiff = Integer.MAX_VALUE;

        for (int i = 0; i <= text.length(); i++) {
            int w = backend.getTextWidth(text.substring(0, i));
            int diff = Math.abs(w - relativeX);
            if (diff < minDiff) {
                minDiff = diff;
                closestPos = i;
            }
        }
        return closestPos;
    }

    public boolean onCharTyped(char chr, int modifiers, IAuraRenderBackend backend) {
        if (!active || !focused) return false;
        if (Character.isISOControl(chr)) return false;

        deleteSelectedText();
        if (text.length() < maxCharacters) {
            text = text.substring(0, cursorPosition) + chr + text.substring(cursorPosition);
            cursorPosition++;
            clearSelection();
            ensureCursorVisible(backend);
            return true;
        }
        return false;
    }

    public boolean onKeyPressed(int keyCode, int scanCode, int modifiers, IAuraRenderBackend backend) {
        if (!active || !focused) return false;

        boolean isCtrlDown = (modifiers & 2) != 0; // GLFW_MOD_CONTROL
        boolean isShiftDown = (modifiers & 1) != 0; // GLFW_MOD_SHIFT

        // 259 = GLFW_KEY_BACKSPACE
        if (keyCode == 259) {
            if (hasSelection()) {
                deleteSelectedText();
            } else if (cursorPosition > 0) {
                text = text.substring(0, cursorPosition - 1) + text.substring(cursorPosition);
                cursorPosition--;
                clearSelection();
            }
            ensureCursorVisible(backend);
            return true;
        }

        // 261 = GLFW_KEY_DELETE
        if (keyCode == 261) {
            if (hasSelection()) {
                deleteSelectedText();
            } else if (cursorPosition < text.length()) {
                text = text.substring(0, cursorPosition) + text.substring(cursorPosition + 1);
                clearSelection();
            }
            ensureCursorVisible(backend);
            return true;
        }

        // 263 = GLFW_KEY_LEFT
        if (keyCode == 263) {
            if (cursorPosition > 0) {
                cursorPosition--;
                if (!isShiftDown) clearSelection();
                else selectionEnd = cursorPosition;
            }
            ensureCursorVisible(backend);
            return true;
        }

        // 262 = GLFW_KEY_RIGHT
        if (keyCode == 262) {
            if (cursorPosition < text.length()) {
                cursorPosition++;
                if (!isShiftDown) clearSelection();
                else selectionEnd = cursorPosition;
            }
            ensureCursorVisible(backend);
            return true;
        }

        // 268 = GLFW_KEY_HOME
        if (keyCode == 268) {
            cursorPosition = 0;
            if (!isShiftDown) clearSelection();
            else selectionEnd = cursorPosition;
            ensureCursorVisible(backend);
            return true;
        }

        // 269 = GLFW_KEY_END
        if (keyCode == 269) {
            cursorPosition = text.length();
            if (!isShiftDown) clearSelection();
            else selectionEnd = cursorPosition;
            ensureCursorVisible(backend);
            return true;
        }

        // Ctrl + A (Select All)
        if (isCtrlDown && keyCode == 65) {
            selectionStart = 0;
            selectionEnd = text.length();
            cursorPosition = text.length();
            ensureCursorVisible(backend);
            return true;
        }

        // Ctrl + C (Copy)
        if (isCtrlDown && keyCode == 67) {
            String selected = getSelectedText();
            if (!selected.isEmpty()) {
                AuraPlatform.get().setClipboardString(selected);
            }
            return true;
        }

        // Ctrl + V (Paste)
        if (isCtrlDown && keyCode == 86) {
            String clip = AuraPlatform.get().getClipboardString();
            if (clip != null && !clip.isEmpty()) {
                deleteSelectedText();
                int allowed = maxCharacters - text.length();
                if (allowed > 0) {
                    if (clip.length() > allowed) clip = clip.substring(0, allowed);
                    text = text.substring(0, cursorPosition) + clip + text.substring(cursorPosition);
                    cursorPosition += clip.length();
                    clearSelection();
                }
            }
            ensureCursorVisible(backend);
            return true;
        }

        // Ctrl + X (Cut)
        if (isCtrlDown && keyCode == 88) {
            String selected = getSelectedText();
            if (!selected.isEmpty()) {
                AuraPlatform.get().setClipboardString(selected);
                deleteSelectedText();
            }
            ensureCursorVisible(backend);
            return true;
        }

        return false;
    }

    public void ensureCursorVisible(IAuraRenderBackend backend) {
        int visibleWidth = Math.max(10, width - 16);
        int cursorPixelX = backend != null ? backend.getTextWidth(text.substring(0, Math.min(cursorPosition, text.length()))) : (cursorPosition * 6);

        if (cursorPixelX - scrollOffset > visibleWidth) {
            scrollOffset = cursorPixelX - visibleWidth;
        } else if (cursorPixelX - scrollOffset < 0) {
            scrollOffset = Math.max(0, cursorPixelX);
        }
    }

    public boolean hasSelection() {
        return selectionStart != selectionEnd;
    }

    public int getSelectionMin() {
        return Math.min(selectionStart, selectionEnd);
    }

    public int getSelectionMax() {
        return Math.max(selectionStart, selectionEnd);
    }

    public String getSelectedText() {
        if (!hasSelection()) return "";
        return text.substring(getSelectionMin(), getSelectionMax());
    }

    private void deleteSelectedText() {
        if (!hasSelection()) return;
        int min = getSelectionMin();
        int max = getSelectionMax();
        text = text.substring(0, min) + text.substring(max);
        cursorPosition = min;
        clearSelection();
    }

    public void clearSelection() {
        selectionStart = cursorPosition;
        selectionEnd = cursorPosition;
    }

    public boolean isCursorVisible(long systemTimeMs) {
        return focused && (systemTimeMs / 500) % 2 == 0;
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

    public String getText() { return text; }
    public void setText(String text) {
        this.text = text != null ? text : "";
        this.cursorPosition = Math.min(cursorPosition, this.text.length());
        clearSelection();
    }

    public String getPlaceholder() { return placeholder; }
    public void setPlaceholder(String placeholder) { this.placeholder = placeholder != null ? placeholder : ""; }

    public int getCursorPosition() { return cursorPosition; }

    /**
     * Устанавливает позицию курсора с валидацией границ строки.
     */
    public void setCursorPosition(int pos) {
        this.cursorPosition = Math.max(0, Math.min(pos, text.length()));
        if (!hasSelection()) {
            this.selectionStart = this.cursorPosition;
            this.selectionEnd = this.cursorPosition;
        }
    }

    /**
     * Возвращает начальный якорь выделения (selectionStart).
     * Совместимость с версионными адаптерами.
     */
    public int getSelectionAnchor() {
        return selectionStart;
    }

    /**
     * Устанавливает начальный якорь выделения (selectionStart).
     * Совместимость с версионными адаптерами.
     */
    public void setSelectionAnchor(int anchor) {
        this.selectionStart = Math.max(0, Math.min(anchor, text.length()));
    }

    /**
     * Устанавливает диапазон выделения.
     */
    public void setSelection(int start, int end) {
        this.selectionStart = Math.max(0, Math.min(start, text.length()));
        this.selectionEnd = Math.max(0, Math.min(end, text.length()));
        this.cursorPosition = this.selectionEnd;
    }

    public int getScrollOffset() { return scrollOffset; }

    public boolean isFocused() { return focused; }
    public void setFocused(boolean focused) {
        this.focused = active && focused;
        if (!this.focused) clearSelection();
    }

    public boolean isActive() { return active; }
    public void setActive(boolean active) { this.active = active; if (!active) focused = false; }

    public int getRadius() { return AuraTokens.RADIUS_TEXT_FIELD; }
}
