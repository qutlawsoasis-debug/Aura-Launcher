package net.auramc.glass.client.ui;

public final class GlassActiveField {
    private static Object activeField;
    private static boolean empty;

    private GlassActiveField() {}

    public static void set(Object field, boolean isEmpty) {
        activeField = field;
        empty = isEmpty;
    }

    public static void clear() {
        activeField = null;
        empty = false;
    }

    public static boolean isPlaceholder() {
        return activeField != null && empty;
    }

    public static boolean isActive() {
        return activeField != null;
    }
}
