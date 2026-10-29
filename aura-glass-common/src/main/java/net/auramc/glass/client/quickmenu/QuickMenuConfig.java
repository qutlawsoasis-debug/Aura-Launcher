package net.auramc.glass.client.quickmenu;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import net.fabricmc.loader.api.FabricLoader;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;

public final class QuickMenuConfig {
    private static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();
    private static QuickMenuConfig value = new QuickMenuConfig();
    private static Path file;
    public int version = 1;
    public boolean zoomEnabled = true, fullbright, customCrosshair = true, blockOutline, lowFire, lowShield, visualTime, clearWeather;
    public double zoomFactor = 3, brightness = -1, crosshairSize = 5, outlineWidth = 2, timeOfDay = 6000;
    public int crosshairStyle = 1, crosshairColor = 0xFFFFFF, outlineColor = 0x64D2FF, particles = -1;
    public double centerX = .5, bottomY = -1;
    public boolean hitParticles, damageNumbers, targetHud, projectilePrediction, hitTint, handTransform, cooldownHud;
    public int hitParticleStyle, hitParticleColor = 0x64D2FF, hitParticleCount = 8, hitTintColor = 0xFF7070, swingAnimation;
    public double handOffsetX, handOffsetY, handOffsetZ, handScale = 1;
    public boolean karaoke = true, karaokePhysics = true, karaokeOnlyStanding = false;
    public double karaokeSize = 1.0, karaokeDistance = 5, musicVolume = .5;
    public int karaokeDensity = 1, lyricsOffsetMs = 0, karaokeMaxWords = 4;
    public boolean maxFps;
    public QuickMenuMaxFps.SavedState maxFpsSavedState;

    public static QuickMenuConfig get() { return value; }
    public static void setConfig(QuickMenuConfig config) { if (config != null) value = config; }
    public static Path getConfigFile() { return file; }
    public static void setConfigFile(Path path) { file = path; }
    public static void initialize() {
        file = FabricLoader.getInstance().getConfigDir().resolve("aura-glass/quickmenu.json");
        if (Files.isRegularFile(file)) {
            try {
                QuickMenuConfig read = GSON.fromJson(Files.readString(file, StandardCharsets.UTF_8), QuickMenuConfig.class);
                if (read == null || read.version != 1) throw new IllegalArgumentException("Unsupported quickmenu version");
                read.validate(); value = read;
            } catch (Exception error) {
                System.err.println("[AuraQuickMenu] Config rejected; defaults active: " + error.getMessage());
                return;
            }
        } else save();
    }
    public static boolean save() {
        if (file == null) return false;
        try {
            value.validate(); Files.createDirectories(file.getParent());
            Path temporary = file.resolveSibling("quickmenu.json.tmp");
            Files.writeString(temporary, GSON.toJson(value) + "\n", StandardCharsets.UTF_8);
            try { Files.move(temporary, file, StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING); }
            catch (AtomicMoveNotSupportedException ignored) { Files.move(temporary, file, StandardCopyOption.REPLACE_EXISTING); }
            return true;
        } catch (Exception error) {
            System.err.println("[AuraQuickMenu] Config save failed: " + error.getMessage());
            return false;
        }
    }
    private void validate() {
        zoomFactor = bounded(zoomFactor, 1, 16, 3); brightness = bounded(brightness, -1, 1, -1);
        crosshairSize = bounded(crosshairSize, 2, 16, 5); outlineWidth = bounded(outlineWidth, 1, 5, 2);
        timeOfDay = bounded(timeOfDay, 0, 23999, 6000); centerX = bounded(centerX, 0, 1, .5);
        bottomY = bounded(bottomY, -1, 1, -1); crosshairStyle = Math.max(0, Math.min(3, crosshairStyle));
        handOffsetX = bounded(handOffsetX,-1,1,0); handOffsetY=bounded(handOffsetY,-1,1,0); handOffsetZ=bounded(handOffsetZ,-1,1,0); handScale=bounded(handScale,.5,1.5,1);
        hitParticleCount=Math.max(1,Math.min(24,hitParticleCount));hitParticleStyle=Math.max(0,Math.min(2,hitParticleStyle));swingAnimation=Math.max(0,Math.min(2,swingAnimation));
        hitParticleColor&=0xFFFFFF;hitTintColor&=0xFFFFFF;
        karaokeSize=bounded(karaokeSize,0.75,1.50,1.0);karaokeDistance=bounded(karaokeDistance,3,7,5);musicVolume=bounded(musicVolume,0,1,.5);
        karaokeDensity=Math.max(0,Math.min(2,karaokeDensity));lyricsOffsetMs=Math.max(-500,Math.min(500,lyricsOffsetMs));
        karaokeMaxWords=Math.max(1,Math.min(6,karaokeMaxWords));
        particles = Math.max(-1, Math.min(2, particles)); crosshairColor &= 0xFFFFFF; outlineColor &= 0xFFFFFF;
    }
    private static double bounded(double v, double min, double max, double fallback) {
        return Double.isFinite(v) ? Math.max(min, Math.min(max, v)) : fallback;
    }
}
