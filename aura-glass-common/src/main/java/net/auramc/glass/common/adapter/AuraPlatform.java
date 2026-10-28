package net.auramc.glass.common.adapter;

/**
 * Синглтон-реестр платформенного адаптера.
 * Инициализируется версионными модулями при старте клиента (ClientModInitializer).
 */
public final class AuraPlatform {
    private static AuraPlatformAdapter adapter;

    private AuraPlatform() {}

    public static synchronized void init(AuraPlatformAdapter impl) {
        adapter = impl;
    }

    public static synchronized AuraPlatformAdapter get() {
        if (adapter == null) {
            // Фолбэк для изолированных unit-тестов
            return DefaultTestAdapter.INSTANCE;
        }
        return adapter;
    }

    /**
     * Минимальный тестовый адаптер, если игра ещё не инициализирована.
     */
    private static final class DefaultTestAdapter implements AuraPlatformAdapter {
        private static final DefaultTestAdapter INSTANCE = new DefaultTestAdapter();
        private String clipboard = "";

        @Override public int getCurrentFps() { return 60; }
        @Override public void playClickSound() {}
        @Override public void setClipboardString(String text) { this.clipboard = text != null ? text : ""; }
        @Override public String getClipboardString() { return this.clipboard; }
        @Override public double getGuiScale() { return 2.0; }
        @Override public int getScaledWidth() { return 960; }
        @Override public int getScaledHeight() { return 540; }
    }
}
