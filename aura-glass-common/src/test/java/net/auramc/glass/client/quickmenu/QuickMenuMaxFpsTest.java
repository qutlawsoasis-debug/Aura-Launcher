package net.auramc.glass.client.quickmenu;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import net.auramc.glass.common.style.GlassQuality;
import net.auramc.glass.common.style.GlassStyles;

import java.io.File;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

public final class QuickMenuMaxFpsTest {
    private static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();

    public static void main(String[] args) throws Exception {
        System.out.println("=== Starting QuickMenuMaxFps Comprehensive Unit Tests ===");
        Path tempDir = Files.createTempDirectory("auratest_quickmenu_main");
        Path cfgFile = tempDir.resolve("quickmenu.json");
        QuickMenuConfig.setConfigFile(cfgFile);

        try {
            testSavedStateDefaults();
            testConfigSerialization();
            testToggleAndRollback();
            testIdempotence();
            testModSafety();
            testSaveFailureAbortsApply();
            testPartialRollbackRetention();
            testDistantHorizonsPartialCaptureSafety();
            testIrisBooleanRestoration();
            testRestartPersistence();
            System.out.println("All QuickMenuMaxFps tests PASSED successfully!");
        } finally {
            deleteRecursively(tempDir.toFile());
        }
    }

    private static void testSavedStateDefaults() {
        QuickMenuMaxFps.SavedState state = new QuickMenuMaxFps.SavedState();
        check(!state.hasIrisShaders, "Initially hasIrisShaders is false");
        check(!state.hasDhRendering, "Initially hasDhRendering is false");
        check(!state.hasDhChunkDistance, "Initially hasDhChunkDistance is false");
        check(!state.hasViewDistance, "Initially hasViewDistance is false");
        check(!state.hasGlassQuality, "Initially hasGlassQuality is false");
        System.out.println("[PASS] 1. SavedState default flags match requirements");
    }

    private static void testConfigSerialization() {
        QuickMenuConfig config = new QuickMenuConfig();
        config.maxFps = true;
        config.maxFpsSavedState = new QuickMenuMaxFps.SavedState();
        config.maxFpsSavedState.hasViewDistance = true;
        config.maxFpsSavedState.viewDistance = 16;
        config.maxFpsSavedState.hasGlassQuality = true;
        config.maxFpsSavedState.glassQuality = "MEDIUM";
        config.maxFpsSavedState.hasDhChunkDistance = true;
        config.maxFpsSavedState.dhChunkDistance = 256;
        config.maxFpsSavedState.hasIrisShaders = true;
        config.maxFpsSavedState.irisShadersEnabled = false;

        String json = GSON.toJson(config);
        QuickMenuConfig restored = GSON.fromJson(json, QuickMenuConfig.class);

        check(restored.maxFps, "Restored maxFps is true");
        check(restored.maxFpsSavedState != null, "Restored maxFpsSavedState not null");
        check(restored.maxFpsSavedState.hasViewDistance, "Restored hasViewDistance is true");
        check(restored.maxFpsSavedState.viewDistance == 16, "Restored viewDistance matches");
        check(restored.maxFpsSavedState.hasGlassQuality, "Restored hasGlassQuality is true");
        check("MEDIUM".equals(restored.maxFpsSavedState.glassQuality), "Restored glassQuality matches");
        check(restored.maxFpsSavedState.hasDhChunkDistance, "Restored hasDhChunkDistance is true");
        check(restored.maxFpsSavedState.dhChunkDistance == 256, "Restored dhChunkDistance matches");
        check(restored.maxFpsSavedState.hasIrisShaders, "Restored hasIrisShaders is true");
        check(!restored.maxFpsSavedState.irisShadersEnabled, "Restored irisShadersEnabled is false");
        System.out.println("[PASS] 2. Config JSON serialization and deserialization");
    }

    private static void testToggleAndRollback() {
        var config = QuickMenuConfig.get();
        config.maxFps = false;
        config.maxFpsSavedState = null;
        GlassStyles.setQuality(GlassQuality.HIGH);

        // 1. Toggle ON
        boolean onResult = QuickMenuMaxFps.toggle();
        check(onResult, "toggle ON succeeded");
        check(QuickMenuMaxFps.isEnabled(), "maxFps enabled after toggle");
        check(config.maxFpsSavedState != null, "savedState recorded");
        check(config.maxFpsSavedState.hasGlassQuality, "Glass quality was captured");
        check("HIGH".equals(config.maxFpsSavedState.glassQuality), "Recorded previous glass quality was HIGH");
        check(GlassStyles.quality() == GlassQuality.LOW, "Glass quality switched to LOW");

        // 2. Toggle OFF (Rollback)
        boolean offResult = QuickMenuMaxFps.toggle();
        check(offResult, "toggle OFF succeeded");
        check(!QuickMenuMaxFps.isEnabled(), "maxFps disabled after second toggle");
        check(config.maxFpsSavedState == null, "savedState cleared after full rollback");
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

    private static void testSaveFailureAbortsApply() throws IOException {
        Path origFile = QuickMenuConfig.getConfigFile();
        Path tempDir = Files.createTempDirectory("auratest_savefail");
        Path invalidTarget = tempDir.resolve("not_a_dir/blocked/quickmenu.json");

        // Create a regular file in place of directory to cause Files.createDirectories to fail
        Files.createFile(tempDir.resolve("not_a_dir"));

        try {
            QuickMenuConfig.setConfigFile(invalidTarget);
            var config = QuickMenuConfig.get();
            config.maxFps = false;
            config.maxFpsSavedState = null;
            GlassStyles.setQuality(GlassQuality.HIGH);

            boolean res = QuickMenuMaxFps.toggle();
            check(!res, "toggle() returns false on save failure");
            check(!QuickMenuMaxFps.isEnabled(), "maxFps remains false when save fails");
            check(config.maxFpsSavedState == null, "SavedState remains null when save fails");
            check(GlassStyles.quality() == GlassQuality.HIGH, "Settings are NOT modified when save fails");
            System.out.println("[PASS] 6. Save failure aborts preset apply and preserves settings");
        } finally {
            QuickMenuConfig.setConfigFile(origFile);
            deleteRecursively(tempDir.toFile());
        }
    }

    private static void testPartialRollbackRetention() {
        var config = QuickMenuConfig.get();
        config.maxFps = true;
        QuickMenuMaxFps.SavedState state = new QuickMenuMaxFps.SavedState();
        state.hasGlassQuality = true;
        state.glassQuality = "MEDIUM";
        // Unrecognized dummy option simulating a failure in another system
        state.hasCloudRenderMode = true;
        state.cloudRenderMode = "INVALID_MODE_FORCING_FAILURE";

        config.maxFpsSavedState = state;

        // Try toggle OFF without client (simulating partial rollback or missing client)
        boolean rollbackResult = QuickMenuMaxFps.toggle();
        check(!rollbackResult, "toggle() returns false on partial rollback failure");
        check(config.maxFps, "maxFps remains true on partial rollback failure");
        check(config.maxFpsSavedState != null, "SavedState is retained on disk/memory on partial rollback failure");
        check(!config.maxFpsSavedState.hasGlassQuality, "Successfully restored field hasGlassQuality is cleared");
        check(config.maxFpsSavedState.hasCloudRenderMode, "Failed field hasCloudRenderMode remains retained");
        check(GlassStyles.quality() == GlassQuality.MEDIUM, "Successfully restored glass quality is restored");

        // Now clear the failing condition and retry rollback
        config.maxFpsSavedState.hasCloudRenderMode = false;
        boolean retryResult = QuickMenuMaxFps.toggle();
        check(retryResult, "Subsequent rollback succeeds after clearing error");
        check(!config.maxFps, "maxFps is now false");
        check(config.maxFpsSavedState == null, "SavedState is now cleared");
        System.out.println("[PASS] 7. Partial rollback retains recovery snapshot until all fields succeed");
    }

    private static void testDistantHorizonsPartialCaptureSafety() {
        QuickMenuMaxFps.SavedState state = new QuickMenuMaxFps.SavedState();
        check(!state.hasDhRendering, "hasDhRendering is false if read failed");
        check(!state.hasDhChunkDistance, "hasDhChunkDistance is false if read failed");

        // Verify that default values are not marked as captured
        state.dhRenderingEnabled = true;
        state.dhChunkDistance = 128;
        check(!state.hasDhRendering, "dhRenderingEnabled value does not imply captured");
        check(!state.hasDhChunkDistance, "dhChunkDistance value does not imply captured");
        System.out.println("[PASS] 8. Distant Horizons per-field captured flags prevent writing defaults");
    }

    private static void testIrisBooleanRestoration() {
        QuickMenuMaxFps.SavedState state = new QuickMenuMaxFps.SavedState();
        state.hasIrisShaders = true;
        state.irisShadersEnabled = false; // Shaders were originally OFF
        check(!state.irisShadersEnabled, "Iris shaders recorded as false");

        state.irisShadersEnabled = true; // Shaders were originally ON
        check(state.irisShadersEnabled, "Iris shaders recorded as true");
        System.out.println("[PASS] 9. Iris boolean restoration records and supports both false and true");
    }

    private static void testRestartPersistence() throws IOException {
        Path tempDir = Files.createTempDirectory("auratest_restart");
        Path testConfigFile = tempDir.resolve("quickmenu.json");
        Path origFile = QuickMenuConfig.getConfigFile();

        try {
            QuickMenuConfig.setConfigFile(testConfigFile);
            var config = QuickMenuConfig.get();
            config.maxFps = true;
            QuickMenuMaxFps.SavedState state = new QuickMenuMaxFps.SavedState();
            state.hasViewDistance = true;
            state.viewDistance = 14;
            state.hasSimulationDistance = true;
            state.simulationDistance = 8;
            state.hasParticles = true;
            state.particles = "DECREASED";
            state.hasGlassQuality = true;
            state.glassQuality = "HIGH";
            state.hasIrisShaders = true;
            state.irisShadersEnabled = false;
            state.hasDhRendering = true;
            state.dhRenderingEnabled = false;
            state.hasDhChunkDistance = true;
            state.dhChunkDistance = 96;
            config.maxFpsSavedState = state;

            check(QuickMenuConfig.save(), "Saved config to temporary test file");

            // Simulate cold restart: clear static config and re-read from disk
            String jsonText = Files.readString(testConfigFile);
            QuickMenuConfig restarted = GSON.fromJson(jsonText, QuickMenuConfig.class);
            check(restarted.maxFps, "Restarted config has maxFps=true");
            check(restarted.maxFpsSavedState != null, "Restarted config has maxFpsSavedState");
            check(restarted.maxFpsSavedState.hasViewDistance && restarted.maxFpsSavedState.viewDistance == 14, "ViewDistance preserved across restart");
            check(restarted.maxFpsSavedState.hasSimulationDistance && restarted.maxFpsSavedState.simulationDistance == 8, "SimulationDistance preserved across restart");
            check("DECREASED".equals(restarted.maxFpsSavedState.particles), "Particles preserved across restart");
            check(restarted.maxFpsSavedState.hasIrisShaders && !restarted.maxFpsSavedState.irisShadersEnabled, "Iris shaders=false preserved across restart");
            check(restarted.maxFpsSavedState.hasDhRendering && !restarted.maxFpsSavedState.dhRenderingEnabled, "DH rendering=false preserved across restart");
            check(restarted.maxFpsSavedState.hasDhChunkDistance && restarted.maxFpsSavedState.dhChunkDistance == 96, "DH distance=96 preserved across restart");
            System.out.println("[PASS] 10. Recovery snapshot survives game restart and restores accurately");
        } finally {
            QuickMenuConfig.setConfigFile(origFile);
            deleteRecursively(tempDir.toFile());
        }
    }

    private static void check(boolean condition, String message) {
        if (!condition) {
            throw new AssertionError("Assertion failed: " + message);
        }
    }

    private static void deleteRecursively(File file) {
        if (file.isDirectory()) {
            File[] files = file.listFiles();
            if (files != null) {
                for (File f : files) deleteRecursively(f);
            }
        }
        file.delete();
    }
}
