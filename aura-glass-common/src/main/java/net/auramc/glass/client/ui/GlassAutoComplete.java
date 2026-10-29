package net.auramc.glass.client.ui;

import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.GlassPreset;
import net.auramc.glass.common.style.GlassStyles;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.ClickableWidget;
import net.minecraft.text.Text;

import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.util.List;

public final class GlassAutoComplete {
    private static boolean initialized;
    private static Class<?> autoCompleteClass;
    private static Field editBoxField;
    private static Field suggestionsField;
    private static Field offsetField;
    private static Field selectedField;
    private static Field heightField;
    private static Field maxField;
    private static Field lastMousePosField;
    private static Method updateHoveringMethod;
    private static Method displayMethod;
    private static Method vectorSetMethod;

    private GlassAutoComplete() {}

    private static void init() {
        if (initialized) return;
        initialized = true;
        try {
            autoCompleteClass = Class.forName("com.blamejared.searchables.api.autcomplete.AutoComplete");
            editBoxField = autoCompleteClass.getDeclaredField("editBox");
            editBoxField.setAccessible(true);
            suggestionsField = autoCompleteClass.getDeclaredField("suggestions");
            suggestionsField.setAccessible(true);
            offsetField = autoCompleteClass.getDeclaredField("displayOffset");
            offsetField.setAccessible(true);
            selectedField = autoCompleteClass.getDeclaredField("selectedIndex");
            selectedField.setAccessible(true);
            heightField = autoCompleteClass.getDeclaredField("suggestionHeight");
            heightField.setAccessible(true);
            maxField = autoCompleteClass.getDeclaredField("maxSuggestions");
            maxField.setAccessible(true);
            lastMousePosField = autoCompleteClass.getDeclaredField("lastMousePosition");
            lastMousePosField.setAccessible(true);
            updateHoveringMethod = autoCompleteClass.getDeclaredMethod("updateHoveringState", double.class, double.class);
            updateHoveringMethod.setAccessible(true);

            Class<?> suggClass = Class.forName("com.blamejared.searchables.api.autcomplete.CompletionSuggestion");
            displayMethod = suggClass.getDeclaredMethod("display");
            displayMethod.setAccessible(true);

            try {
                Class<?> vecClass = Class.forName("org.joml.Vector2d");
                vectorSetMethod = vecClass.getDeclaredMethod("set", double.class, double.class);
                vectorSetMethod.setAccessible(true);
            } catch (Throwable ignored) {}
        } catch (Throwable ignored) {
            autoCompleteClass = null;
        }
    }

    public static boolean isAutoComplete(ClickableWidget widget) {
        if (!initialized) init();
        return autoCompleteClass != null && autoCompleteClass.isInstance(widget);
    }

    public static int getRowY(int widgetY, int suggestionHeight, int index) {
        return widgetY + index * suggestionHeight;
    }

    public static int getRowHeight(int suggestionHeight) {
        return suggestionHeight;
    }

    public static int hitTestRow(int widgetY, int suggestionHeight, int shown, double mouseY) {
        if (shown <= 0 || suggestionHeight <= 0) return -1;
        int currentY = widgetY;
        for (int i = 0; i < shown; i++) {
            int nextY = currentY + suggestionHeight;
            if (mouseY >= currentY && mouseY < nextY) {
                return i;
            }
            currentY = nextY;
        }
        return -1;
    }

    public static void updateLastMousePosition(Object widget, double mx, double my) {
        if (lastMousePosField != null && vectorSetMethod != null && widget != null) {
            try {
                Object vector = lastMousePosField.get(widget);
                if (vector != null) {
                    vectorSetMethod.invoke(vector, mx, my);
                }
            } catch (Throwable ignored) {}
        }
    }

    public static boolean shouldRender(boolean widgetVisible, boolean editBoxVisible, boolean editBoxFocused, int suggestionCount) {
        return widgetVisible && editBoxVisible && editBoxFocused && suggestionCount > 0;
    }

    public static boolean render(ClickableWidget widget, DrawContext context, int mx, int my, float delta) {
        // Guard 1: widget must be visible and be AutoComplete instance
        if (widget == null || !widget.visible || !isAutoComplete(widget)) return false;

        try {
            ClickableWidget editBox = (ClickableWidget) editBoxField.get(widget);
            // Guard 2: editBox must be visible and focused to display autocomplete popup
            if (editBox == null || !editBox.visible || !editBox.isFocused()) return true;

            List<?> suggestions = (List<?>) suggestionsField.get(widget);
            // Guard 3: suggestions must be non-empty
            if (suggestions == null || suggestions.isEmpty()) return true;

            int displayOffset = offsetField.getInt(widget);
            int selectedIndex = selectedField.getInt(widget);
            int suggestionHeight = heightField.getInt(widget);
            int maxSuggestions = maxField.getInt(widget);

            // Native hover check (only updates selectedIndex if mouse moved relative to lastMousePosition)
            try {
                updateHoveringMethod.invoke(widget, (double) mx, (double) my);
                selectedIndex = selectedField.getInt(widget);
            } catch (Throwable ignored) {}

            int shown = Math.min(maxSuggestions, suggestions.size() - displayOffset);
            if (shown <= 0) return true;

            int x = widget.getX();
            int y = widget.getY();
            int w = widget.getWidth();
            int totalH = shown * suggestionHeight;

            // 1. Apple Liquid Glass surface: padding only in background, keeping native row hitbox geometry
            int pad = 2;
            var style = GlassStyles.frame(GlassPreset.PANEL);
            style.asSurface();
            style.radius = 8;
            style.bevel = 2.5f;
            style.refraction = 0.25f;
            style.dispersion = 0.005f;
            style.frost = 1.5f;

            // Z-order: floating popover on top of the list content
            GlassRenderer.drawSurface(widget, x - pad, y - pad, w + 2 * pad, totalH + 2 * pad, style, 1);
            context.draw();
            GlassRenderer.flush();

            // 2. Inner flat fills and legible high-contrast text with EXACT native row geometry
            var client = MinecraftClient.getInstance();
            for (int i = 0; i < shown; i++) {
                int suggIdx = displayOffset + i;
                if (suggIdx >= suggestions.size()) break;

                Object sugg = suggestions.get(suggIdx);
                Text text = (Text) displayMethod.invoke(sugg);

                int rowX = x;
                int rowY = getRowY(y, suggestionHeight, i);
                int rowW = w;
                int rowH = getRowHeight(suggestionHeight);

                boolean selected = (selectedIndex != -1 && selectedIndex == i);
                if (selected) {
                    // Apple flat fill: rgba(118, 118, 128, 0.36) with subtle 1px border
                    context.fill(rowX + 1, rowY, rowX + rowW - 1, rowY + rowH, 0x5C767680);
                    // 1px border top & bottom
                    context.fill(rowX + 1, rowY, rowX + rowW - 1, rowY + 1, 0x30FFFFFF);
                    context.fill(rowX + 1, rowY + rowH - 1, rowX + rowW - 1, rowY + rowH, 0x30FFFFFF);
                    context.fill(rowX + 1, rowY, rowX + 2, rowY + rowH, 0x30FFFFFF);
                    context.fill(rowX + rowW - 2, rowY, rowX + rowW - 1, rowY + rowH, 0x30FFFFFF);
                }

                // Crisp, high-contrast readable text (contrast >= 4.5:1)
                int textColor = selected ? 0xFFFFFFFF : 0xFFE0E0E0;
                context.drawText(client.textRenderer, text, rowX + 4, rowY + (rowH - 8) / 2, textColor, false);
            }

            // 3. Preserve native lastMousePosition semantics so keyboard navigation without mouse movement is preserved
            updateLastMousePosition(widget, (double) mx, (double) my);

            return true;
        } catch (Throwable t) {
            return false;
        }
    }
}
