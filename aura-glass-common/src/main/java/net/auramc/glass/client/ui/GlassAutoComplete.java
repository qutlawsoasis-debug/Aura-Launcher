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
    private static Method updateHoveringMethod;
    private static Method displayMethod;

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
            updateHoveringMethod = autoCompleteClass.getDeclaredMethod("updateHoveringState", double.class, double.class);
            updateHoveringMethod.setAccessible(true);

            Class<?> suggClass = Class.forName("com.blamejared.searchables.api.autcomplete.CompletionSuggestion");
            displayMethod = suggClass.getDeclaredMethod("display");
            displayMethod.setAccessible(true);
        } catch (Throwable ignored) {
            autoCompleteClass = null;
        }
    }

    public static boolean isAutoComplete(ClickableWidget widget) {
        if (!initialized) init();
        return autoCompleteClass != null && autoCompleteClass.isInstance(widget);
    }

    public static boolean render(ClickableWidget widget, DrawContext context, int mx, int my, float delta) {
        if (!isAutoComplete(widget)) return false;
        try {
            ClickableWidget editBox = (ClickableWidget) editBoxField.get(widget);
            if (editBox == null || !editBox.isFocused()) return true;

            List<?> suggestions = (List<?>) suggestionsField.get(widget);
            if (suggestions == null || suggestions.isEmpty()) return true;

            int displayOffset = offsetField.getInt(widget);
            int selectedIndex = selectedField.getInt(widget);
            int suggestionHeight = heightField.getInt(widget);
            int maxSuggestions = maxField.getInt(widget);

            try {
                updateHoveringMethod.invoke(widget, (double) mx, (double) my);
                selectedIndex = selectedField.getInt(widget);
            } catch (Throwable ignored) {}

            int shown = Math.min(maxSuggestions, suggestions.size() - displayOffset);
            if (shown <= 0) return true;

            int x = widget.getX();
            int y = widget.getY();
            int w = widget.getWidth();
            int totalH = shown * (suggestionHeight + 2) + 6;

            // 1. One Apple Liquid Glass surface covering the entire dropdown popover
            var style = GlassStyles.frame(GlassPreset.PANEL);
            style.asSurface();
            style.radius = 8;
            style.bevel = 2.5f;
            style.refraction = 0.25f;
            style.dispersion = 0.005f;
            style.frost = 1.5f;

            // Z-order: floating popover on top of the list content
            GlassRenderer.drawSurface(widget, x - 2, y - 2, w + 4, totalH, style, 1);
            context.draw();
            GlassRenderer.flush();

            // 2. Inner flat fills and legible high-contrast text
            var client = MinecraftClient.getInstance();
            for (int i = 0; i < shown; i++) {
                int suggIdx = displayOffset + i;
                if (suggIdx >= suggestions.size()) break;

                Object sugg = suggestions.get(suggIdx);
                Text text = (Text) displayMethod.invoke(sugg);

                int rowX = x + 2;
                int rowY = y + 1 + i * (suggestionHeight + 2);
                int rowW = w - 4;
                int rowH = suggestionHeight + 1;

                boolean selected = (selectedIndex != -1 && selectedIndex == i);
                if (selected) {
                    // Apple flat fill: rgba(118, 118, 128, 0.36) with subtle 1px border
                    context.fill(rowX, rowY, rowX + rowW, rowY + rowH, 0x5C767680);
                    // 1px border top & bottom
                    context.fill(rowX, rowY, rowX + rowW, rowY + 1, 0x30FFFFFF);
                    context.fill(rowX, rowY + rowH - 1, rowX + rowW, rowY + rowH, 0x30FFFFFF);
                    context.fill(rowX, rowY, rowX + 1, rowY + rowH, 0x30FFFFFF);
                    context.fill(rowX + rowW - 1, rowY, rowX + rowW, rowY + rowH, 0x30FFFFFF);
                }

                // Crisp, high-contrast readable text (contrast >= 4.5:1)
                int textColor = selected ? 0xFFFFFFFF : 0xFFE0E0E0;
                context.drawText(client.textRenderer, text, rowX + 6, rowY + (rowH - 8) / 2, textColor, false);
            }
            return true;
        } catch (Throwable t) {
            return false;
        }
    }
}
