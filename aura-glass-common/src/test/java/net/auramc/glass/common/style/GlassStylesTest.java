package net.auramc.glass.common.style;

import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.charset.StandardCharsets;

public final class GlassStylesTest {
    public static void main(String[] args) throws Exception {
        Path directory = Path.of(args[0]);
        Files.createDirectories(directory);
        GlassStyles.initialize(directory);
        check(Files.isRegularFile(GlassStyles.file()), "default JSON created");
        check(GlassStyles.quality() == GlassQuality.HIGH, "default quality High");
        GlassStyle transparent = GlassStyles.get(GlassPreset.PANEL);
        transparent.tintA = 0;
        GlassStyle low = GlassStyles.material(transparent, GlassQuality.LOW);
        check(low.tintA >= .12f, "Low retains visible fill without backdrop or configured tint");
        check(transparent.tintA == 0, "Low fallback does not overwrite configured preset");
        check(GlassStyles.material(transparent, GlassQuality.HIGH).tintA == 0, "High custom tint retained");
        GlassStyle outer=GlassStyles.get(GlassPreset.PANEL).asSurface();
        GlassStyle flatOuter=GlassStyles.material(outer,GlassQuality.LOW);
        check(flatOuter.tintA>=.90f && flatOuter.tintB<=.16f,"Low outer material protects white text on bright backdrops");
        GlassStyles.beginFrame();
        for(GlassPreset preset:GlassPreset.values()){
            GlassStyle source=GlassStyles.get(preset),copy=new GlassStyle();source.copyInto(copy);
            for(var field:GlassStyle.class.getFields())check(field.getFloat(source)==field.getFloat(copy),"complete copy: "+field.getName());
            GlassStyle material=new GlassStyle();GlassStyles.materialInto(source,GlassQuality.OFF,material);
            check(material.tintA>=.94f && material.frost==0 && source.frost==GlassStyles.get(preset).frost,"borrowed material independent");
        }
        for (GlassPreset preset : GlassPreset.values()) {
            GlassStyle style = GlassStyles.get(preset);
            check(style != null, "all presets present");
            float refraction = style.refraction;
            style.refraction = 99;
            check(GlassStyles.get(preset).refraction == refraction, "get returns independent copy");
        }
        check(GlassStyles.resolve(GlassPreset.PILL_THICK, GlassState.HOVER, 1).scale == 1f, "Regular hover keeps grouped controls aligned");
        check(GlassStyles.resolve(GlassPreset.PILL_THICK, GlassState.PRESS, 1).scale == 1f, "Regular press keeps grouped controls aligned");
        GlassStyle edited = GlassStyles.get(GlassPreset.PANEL);
        edited.frost = 1.25f;
        GlassStyles.update(GlassPreset.PANEL, edited);
        check(GlassStyles.save(), "edited preset saves");
        String accepted = Files.readString(GlassStyles.file());
        check(accepted.contains("1.25"), "file contains changed parameter");
        Files.writeString(GlassStyles.file(), "{invalid-json", StandardCharsets.UTF_8);
        Thread.sleep(260);
        GlassStyles.poll();
        check(GlassStyles.get(GlassPreset.PANEL).frost == 1.25f, "bad reload retains accepted document");
        Files.writeString(GlassStyles.file(), accepted.replace("1.25", "2.25"), StandardCharsets.UTF_8);
        Thread.sleep(260);
        GlassStyles.poll();
        check(GlassStyles.get(GlassPreset.PANEL).frost == 2.25f, "valid JSON hot reloads");
        edited.refraction = Float.NaN;
        boolean rejected = false;
        try { GlassStyles.update(GlassPreset.PANEL, edited); }
        catch (IllegalArgumentException expected) { rejected = true; }
        check(rejected, "NaN rejected");
        var saved=com.google.gson.JsonParser.parseString(accepted).getAsJsonObject();
        for(GlassQuality quality:GlassQuality.values()){
            var legacy=saved.deepCopy();legacy.addProperty("version",4);legacy.addProperty("quality",quality.name());
            legacy.remove("material");legacy.remove("profiles");
            legacy.getAsJsonObject("presets").getAsJsonObject("PANEL").addProperty("refraction",.73f);
            Files.writeString(GlassStyles.file(),legacy.toString(),StandardCharsets.UTF_8);
            check(GlassStyles.reloadFromFile(),"v4 migration: "+quality);
            check(GlassStyles.quality()==quality,"migration retains quality: "+quality);
            check(GlassStyles.regular(),"migration selects Regular");
            check(GlassStyles.get(GlassPreset.PANEL).bevel<=3,"Regular limits refraction to edge");
            var migrated=com.google.gson.JsonParser.parseString(Files.readString(GlassStyles.file())).getAsJsonObject();
            check(migrated.get("version").getAsInt()==5,"v5 saved atomically");
            check(migrated.getAsJsonObject("profiles").getAsJsonObject("LIQUID").getAsJsonObject("PANEL").get("refraction").getAsFloat()==.73f,"old custom lens retained");
            migrated.addProperty("material","LIQUID");
            Files.writeString(GlassStyles.file(),migrated.toString(),StandardCharsets.UTF_8);
            check(GlassStyles.reloadFromFile()&&!GlassStyles.regular(),"one-line LIQUID switch");
            check(GlassStyles.get(GlassPreset.PANEL).refraction==.73f,"LIQUID uses saved custom lens");
            check(GlassStyles.filePresets().get(GlassPreset.PANEL).refraction==.73f,"gallery comparison uses active profile");
            migrated.addProperty("material","REGULAR");
            migrated.getAsJsonObject("presets").getAsJsonObject("PANEL").addProperty("frost",2.1f);
            Files.writeString(GlassStyles.file(),migrated.toString(),StandardCharsets.UTF_8);
            check(GlassStyles.reloadFromFile()&&GlassStyles.get(GlassPreset.PANEL).frost==2.1f,"v5 custom Regular values not reset");
        }
        System.out.println("Glass styles: defaults, copies, states, atomic save and reload checks passed");
    }

    private static void check(boolean ok, String message) {
        if (!ok) throw new AssertionError(message);
    }
}
