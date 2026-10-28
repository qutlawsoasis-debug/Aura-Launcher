package net.auramc.glass.common.ui;

import net.auramc.glass.common.adapter.AuraPlatform;
import net.auramc.glass.common.theme.AuraTokens;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * Чистая платформонезависимая модель виртуализированного списка (Virtual List).
 * Поддерживает честные пустые состояния (empty state), скроллинг, выбор элементов и навигацию стрелками.
 */
public class AuraVirtualListModel<T> {
    public static final int CONTENT_INSET = 4;
    private int x;
    private int y;
    private int width;
    private int height;
    private int itemHeight = 24;
    private final List<T> items = new ArrayList<>();
    private int selectedIndex = -1;
    private double scrollOffset = 0.0;
    private double targetScrollOffset;
    private long lastUpdateNanos = -1L;
    private boolean motionEnabled = true;
    private boolean focused = false;
    private String emptyText = "Нет элементов";

    public AuraVirtualListModel(int x, int y, int width, int height) {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    public void setItems(List<T> newItems) {
        this.items.clear();
        if (newItems != null) {
            this.items.addAll(newItems);
        }
        clampScroll();
        if (selectedIndex >= items.size()) {
            selectedIndex = items.isEmpty() ? -1 : items.size() - 1;
        }
    }

    public List<T> getItems() {
        return Collections.unmodifiableList(items);
    }

    public int getItemCount() {
        return items.size();
    }

    public boolean isEmpty() {
        return items.isEmpty();
    }

    public double getMaxScroll() {
        int totalContentHeight = items.size() * itemHeight;
        return Math.max(0.0, totalContentHeight - getViewportHeight());
    }

    private void clampScroll() {
        double max = getMaxScroll();
        if (scrollOffset < 0.0) scrollOffset = 0.0;
        if (scrollOffset > max) scrollOffset = max;
        targetScrollOffset = Math.max(0.0, Math.min(targetScrollOffset, max));
    }

    public boolean onMouseScrolled(double mouseX, double mouseY, double amount) {
        if (isMouseOver(mouseX, mouseY)) {
            // amount > 0: скролл вверх, amount < 0: скролл вниз
            if (!Double.isFinite(amount)) return false;
            this.targetScrollOffset -= amount * (itemHeight * 1.5);
            clampScroll();
            if (!motionEnabled) scrollOffset = targetScrollOffset;
            return true;
        }
        return false;
    }

    public void update(long nowNanos) {
        if (lastUpdateNanos < 0L) {
            lastUpdateNanos = nowNanos;
            return;
        }
        double elapsed = Math.max(0.0, (nowNanos - lastUpdateNanos) / 1_000_000_000.0);
        lastUpdateNanos = nowNanos;
        if (!motionEnabled) {
            scrollOffset = targetScrollOffset;
        } else {
            double blend = -Math.expm1(-20.0 * elapsed);
            scrollOffset += (targetScrollOffset - scrollOffset) * blend;
            if (Math.abs(targetScrollOffset - scrollOffset) < 0.001) scrollOffset = targetScrollOffset;
        }
        clampScroll();
    }

    public void setMotionEnabled(boolean enabled) {
        motionEnabled = enabled;
        if (!enabled) scrollOffset = targetScrollOffset;
    }

    public boolean isMouseOver(double mouseX, double mouseY) {
        return mouseX >= x && mouseX < x + width && mouseY >= y && mouseY < y + height;
    }

    public boolean onMouseClicked(double mouseX, double mouseY, int button) {
        if (button == 0 && isMouseOver(mouseX, mouseY)) {
            this.focused = true;
            if (mouseY < getViewportY() || mouseY >= getViewportY() + getViewportHeight()
                    || mouseX < x + CONTENT_INSET || mouseX >= x + width - 8) return false;
            double relativeY = (mouseY - getViewportY()) + scrollOffset;
            int clickedIndex = (int) (relativeY / itemHeight);
            if (clickedIndex >= 0 && clickedIndex < items.size()) {
                this.selectedIndex = clickedIndex;
                AuraPlatform.get().playClickSound();
                return true;
            }
        }
        return false;
    }

    public boolean onKeyPressed(int keyCode, int scanCode, int modifiers) {
        if (!focused || items.isEmpty()) return false;

        // 265 = GLFW_KEY_UP
        if (keyCode == 265) {
            if (selectedIndex > 0) {
                selectedIndex--;
                ensureVisible(selectedIndex);
                AuraPlatform.get().playClickSound();
                return true;
            }
        }

        // 264 = GLFW_KEY_DOWN
        if (keyCode == 264) {
            if (selectedIndex < items.size() - 1) {
                selectedIndex++;
                ensureVisible(selectedIndex);
                AuraPlatform.get().playClickSound();
                return true;
            }
        }

        return false;
    }

    private void ensureVisible(int index) {
        int itemTop = index * itemHeight;
        int itemBottom = itemTop + itemHeight;

        if (itemTop < scrollOffset) {
            scrollOffset = itemTop;
        } else if (itemBottom > scrollOffset + getViewportHeight()) {
            scrollOffset = itemBottom - getViewportHeight();
        }
        targetScrollOffset = scrollOffset;
        clampScroll();
    }

    /**
     * Возвращает начальный индекс для виртуализированной отрисовки.
     */
    public int getFirstVisibleIndex() {
        int first = (int) (scrollOffset / itemHeight);
        return Math.max(0, Math.min(first, items.size()));
    }

    /**
     * Возвращает конечный индекс (исключающий) для виртуализированной отрисовки.
     */
    public int getLastVisibleIndex() {
        int count = (int) Math.ceil((double) getViewportHeight() / itemHeight) + 1;
        return Math.min(items.size(), getFirstVisibleIndex() + count);
    }

    // --- Геттеры и сеттеры ---
    public int getX() { return x; }
    public void setX(int x) { this.x = x; }

    public int getY() { return y; }
    public void setY(int y) { this.y = y; }

    public int getWidth() { return width; }
    public void setWidth(int width) { this.width = width; }

    public int getHeight() { return height; }
    public void setHeight(int height) { this.height = height; clampScroll(); }
    public int getViewportY() { return y + CONTENT_INSET; }
    public int getViewportHeight() { return Math.max(0, height - CONTENT_INSET * 2); }
    public double getRowPosition(int index) { return getViewportY() + index * (double) itemHeight - scrollOffset; }
    public int getRowY(int index) { return (int) Math.floor(getRowPosition(index)); }

    public int getItemHeight() { return itemHeight; }
    public int getSelectedIndex() { return selectedIndex; }
    public void setSelectedIndex(int index) { this.selectedIndex = index; }

    public double getScrollOffset() { return scrollOffset; }
    public double getTargetScrollOffset() { return targetScrollOffset; }
    public boolean isFocused() { return focused; }
    public void setFocused(boolean focused) { this.focused = focused; }

    public String getEmptyText() { return emptyText; }
    public void setEmptyText(String text) { this.emptyText = text != null ? text : ""; }

    public int getRadius() { return AuraTokens.RADIUS_CARD; }
}
