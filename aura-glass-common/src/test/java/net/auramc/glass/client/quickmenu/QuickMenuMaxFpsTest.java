package net.auramc.glass.client.quickmenu;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import net.auramc.glass.common.style.GlassQuality;
import net.auramc.glass.common.style.GlassStyles;

public final class QuickMenuMaxFpsTest {
    private static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();

    public static void main(String[] args) {
        System.out.println("=== Starting QuickMenuMaxFps Unit Tests ===");
        testSavedStateDefaults();
        testConfigSerialization();
        testToggleAndRollback();
        testIdempotence();
        testModSafety();
        System.out.println("All QuickMenuMaxFps tests PASSED successfully!");
    }

    private static void testSavedStateDefaults() {
        QuickMenuMaxFps.SavedState state = new QuickMenuMaxFps.SavedState();
        check(state.viewDistance == 12, "Default viewDistance is 12");
        check(state.simulationDistance == 10, "Default simulationDistance is 10");
        check("ALL".equals(state.particles), "Default particles is ALL");
        check("FANCY".equals(state.cloudRenderMode), "Default cloudRenderMode is FANCY");
        check(state.entityShadows, "Default entityShadows is true");
        check(Math.abs(state.entityDistanceScaling - 1.0) < 0.001, "Default entityDistanceScaling is 1.0");
        check("FANCY".equals(state.graphicsMode), "Default graphicsMode is FANCY");
        check(state.ao, "Default ao is true");
        check(state.mipmapLevels == 4, "Default mipmapLevels is 4");
        check(state.biomeBlendRadius == 2, "Default biomeBlendRadius is 2");
        check(state.enableVsync, "Default enableVsync is true");
        check("HIGH".equals(state.glassQuality), "Default glassQuality is HIGH");
        System.out.println("[PASS] 1. SavedState default values match requirements");
    }

    private static void testConfigSerialization() {
        QuickMenuConfig config = new QuickMenuConfig();
        config.maxFps = true;
        config.maxFpsSavedState = new QuickMenuMaxFps.SavedState();
        config.maxFpsSavedState.viewDistance = 16;
        config.maxFpsSavedState.glassQuality = "MEDIUM";
        config.maxFpsSavedState.dhChunkDistance = 256;

        String json = GSON.toJson(config);
        QuickMenuConfig restored = GSON.fromJson(json, QuickMenuConfig.class);

        check(restored.maxFps, "Restored maxFps is true");
        check(restored.maxFpsSavedState != null, "Restored maxFpsSavedState not null");
        check(restored.maxFpsSavedState.viewDistance == 16, "Restored viewDistance matches");
        check("MEDIUM".equals(restored.maxFpsSavedState.glassQuality), "Restored glassQuality matches");
        check(restored.maxFpsSavedState.dhChunkDistance == 256, "Restored dhChunkDistance matches");
        System.out.println("[PASS] 2. Config JSON serialization and deserialization");
    }

    private static void testToggleAndRollback() {
        var config = QuickMenuConfig.get();
        config.maxFps = false;
        config.maxFpsSavedState = null;
        GlassStyles.setQuality(GlassQuality.HIGH);

        // 1. Toggle ON
        QuickMenuMaxFps.toggle();
        check(QuickMenuMaxFps.isEnabled(), "maxFps enabled after toggle");
        check(config.maxFpsSavedState != null, "savedState recorded");
        check("HIGH".equals(config.maxFpsSavedState.glassQuality), "Recorded previous glass quality was HIGH");
        check(GlassStyles.quality() == GlassQuality.LOW, "Glass quality switched to LOW");

        // 2. Toggle OFF (Rollback)
        QuickMenuMaxFps.toggle();
        check(!QuickMenuMaxFps.isEnabled(), "maxFps disabled after second toggle");
        check(config.maxFpsSavedState == null, "savedState cleared after rollback");
        check(GlassStyles.quality() == GlassQuality.HIGH, "Glass quality restored to HIGH");
        System.out.println("[PASS] 3. Toggle ON and full rollback");
    }

    private static void testIdempotence() {
        var config = QuickMenuConfig.get();
        config.maxFps = false;
        config.maxFpsSavedState = null;

        QuickMenuMaxFps.setEnabled(false);
        check(!QuickMenuMaxFps.isEnabled(), "setEnabled(false) when false does nothing");

        QuickMenuMaxFps.setEnabled(true);
        check(QuickMenuMaxFps.isEnabled(), "setEnabled(true) enables preset");

        QuickMenuMaxFps.setEnabled(true);
        check(QuickMenuMaxFps.isEnabled(), "setEnabled(true) when true does not duplicate or reset state");

        QuickMenuMaxFps.setEnabled(false);
        check(!QuickMenuMaxFps.isEnabled(), "setEnabled(false) disables and restores");
        System.out.println("[PASS] 4. SetEnabled idempotence");
    }

    private static void testModSafety() {
        check(!QuickMenuMaxFps.isModLoaded("non_existent_dummy_mod"), "Unknown mod returns false safely");
        System.out.println("[PASS] 5. Mod presence check safety");
    }

    private static void check(boolean condition, String message) {
        if (!condition) {
            throw new AssertionError("Assertion failed: " + message);
        }
    }
}
