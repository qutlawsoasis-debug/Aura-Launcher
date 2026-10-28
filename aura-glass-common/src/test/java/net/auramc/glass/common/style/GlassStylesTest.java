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
        for (GlassPreset preset : GlassPreset.values()) {
            GlassStyle style = GlassStyles.get(preset);
            check(style != null, "all presets present");
            float refraction = style.refraction;
            style.refraction = 99;
            check(GlassStyles.get(preset).refraction == refraction, "get returns independent copy");
        }
        check(GlassStyles.resolve(GlassPreset.PILL_THICK, GlassState.HOVER, 1).scale == 1.03f, "hover scale modifier");
        check(GlassStyles.resolve(GlassPreset.PILL_THICK, GlassState.PRESS, 1).scale == .97f, "press scale modifier");
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
        var legacy = com.google.gson.JsonParser.parseString(Files.readString(GlassStyles.file())).getAsJsonObject();
        legacy.addProperty("version",1);
        var presets = legacy.getAsJsonObject("presets");
        for (GlassPreset preset : GlassPreset.values()) presets.getAsJsonObject(preset.name()).addProperty("lightDirY",.83f);
        var panel = presets.getAsJsonObject("PANEL");
        panel.addProperty("refraction",3); panel.addProperty("bevel",10); panel.addProperty("tintA",.12f); panel.addProperty("frost",1.37f);
        Files.writeString(GlassStyles.file(),legacy.toString(),StandardCharsets.UTF_8);
        check(GlassStyles.reloadFromFile(),"v1 file reloads and migrates");
        check(GlassStyles.get(GlassPreset.PANEL).bevel==12,"old default bevel updated");
        check(GlassStyles.get(GlassPreset.PANEL).refraction==.25f,"old default refraction updated");
        check(GlassStyles.get(GlassPreset.PANEL).frost==1.37f,"custom frost preserved during migration");
        check(GlassStyles.get(GlassPreset.PANEL).lightDirY<0,"default light migrated to GUI up");
        GlassStyle unsaved=GlassStyles.get(GlassPreset.PANEL);unsaved.frost=.1f;GlassStyles.update(GlassPreset.PANEL,unsaved);
        check(GlassStyles.reloadFromFile() && GlassStyles.get(GlassPreset.PANEL).frost==1.37f,"Reset restores file values");
        var version2 = com.google.gson.JsonParser.parseString(Files.readString(GlassStyles.file())).getAsJsonObject();
        version2.addProperty("version",2);
        for (var entry : version2.getAsJsonObject("presets").entrySet()) {
            entry.getValue().getAsJsonObject().remove("profile");
            entry.getValue().getAsJsonObject().remove("cornerSoften");
        }
        version2.getAsJsonObject("presets").getAsJsonObject("PANEL").addProperty("refraction",.42f);
        Files.writeString(GlassStyles.file(),version2.toString(),StandardCharsets.UTF_8);
        check(GlassStyles.reloadFromFile(),"version2 upgrades");
        check(GlassStyles.get(GlassPreset.PANEL).refraction==.42f,"v2 refraction customization retained");
        check(GlassStyles.get(GlassPreset.PANEL).profile==2.5f,"v2 receives finite profile default");
        check(GlassStyles.get(GlassPreset.PANEL).cornerSoften==.6f,"v2 receives corner softening default");
        check(com.google.gson.JsonParser.parseString(Files.readString(GlassStyles.file())).getAsJsonObject().get("version").getAsInt()==4,"v4 saved to JSON");
        GlassStyle bounds=new GlassStyle();bounds.profile=0;bounds.cornerSoften=2;bounds.validate();
        check(bounds.profile==1.5f && bounds.cornerSoften==1,"new parameter lower/upper limits");
        bounds.profile=99;bounds.cornerSoften=-2;bounds.validate();
        check(bounds.profile==4 && bounds.cornerSoften==0,"new parameter opposite limits");
        var version3 = com.google.gson.JsonParser.parseString(Files.readString(GlassStyles.file())).getAsJsonObject();
        for (var entry : version3.getAsJsonObject("presets").entrySet()) entry.getValue().getAsJsonObject().remove("specFalloff");
        Files.writeString(GlassStyles.file(),version3.toString(),StandardCharsets.UTF_8);
        check(GlassStyles.reloadFromFile(),"existing v3 without specFalloff reloads");
        check(GlassStyles.get(GlassPreset.PANEL).specFalloff==3,"missing falloff receives default3");
        bounds.specFalloff=0;bounds.validate();check(bounds.specFalloff==1,"falloff lower bound");
        bounds.specFalloff=99;bounds.validate();check(bounds.specFalloff==6,"falloff upper bound");
        GlassStyle falloff=GlassStyles.get(GlassPreset.PANEL);falloff.specFalloff=4.25f;GlassStyles.update(GlassPreset.PANEL,falloff);
        check(GlassStyles.save() && GlassStyles.reloadFromFile(),"falloff save/reload");
        check(GlassStyles.get(GlassPreset.PANEL).specFalloff==4.25f,"custom falloff retained");
        var old3=com.google.gson.JsonParser.parseString(Files.readString(GlassStyles.file())).getAsJsonObject();
        var darkBefore=old3.getAsJsonObject("presets").get("SELECTED_DARK").deepCopy();
        old3.addProperty("version",3);old3.remove("selectedPreset");old3.remove("theme");
        old3.getAsJsonObject("presets").remove("SELECTED_GLASS");old3.getAsJsonObject("presets").remove("SELECTED_TINT");
        Files.writeString(GlassStyles.file(),old3.toString(),StandardCharsets.UTF_8);
        check(GlassStyles.reloadFromFile(),"v3 selected migration");
        var migrated=com.google.gson.JsonParser.parseString(Files.readString(GlassStyles.file())).getAsJsonObject();
        check(migrated.getAsJsonObject("presets").get("SELECTED_DARK").equals(darkBefore),"SELECTED_DARK unchanged");
        check(migrated.get("selectedPreset").getAsString().equals("SELECTED_TINT"),"default selected mapping is tint");
        check(GlassStyles.selected().isSelectedText() && GlassStyles.selected().isThemeTint(),"selected metadata survives copy");
        check(Math.abs(GlassStyles.material(GlassStyles.selected()).tintR-64/255f)<.0001f,"theme accent applied only to material");
        check(GlassStyles.get(GlassPreset.SELECTED_TINT).tintR==1,"saved tint multiplier unchanged");
        migrated.addProperty("selectedPreset","SELECTED_GLASS");
        Files.writeString(GlassStyles.file(),migrated.toString(),StandardCharsets.UTF_8);check(GlassStyles.reloadFromFile(),"one-line glass switch");
        check(GlassStyles.selected().tintA==.30f && !GlassStyles.selected().isThemeTint(),"glass mapping used");
        migrated.addProperty("selectedPreset","SELECTED_DARK");
        Files.writeString(GlassStyles.file(),migrated.toString(),StandardCharsets.UTF_8);check(GlassStyles.reloadFromFile(),"one-line dark switch");
        check(GlassStyles.selected().tintA==GlassStyles.get(GlassPreset.SELECTED_DARK).tintA,"dark mapping used");
        migrated.addProperty("selectedPreset","SELECTED_TINT");migrated.getAsJsonObject("theme").addProperty("accent","#AA2211");
        Files.writeString(GlassStyles.file(),migrated.toString(),StandardCharsets.UTF_8);check(GlassStyles.reloadFromFile(),"theme accent reload");
        check(Math.abs(GlassStyles.material(GlassStyles.selected()).tintB-17/255f)<.0001f,"theme accent hot reload");
        System.out.println("Glass styles: defaults, copies, states, atomic save and reload checks passed");
    }

    private static void check(boolean ok, String message) {
        if (!ok) throw new AssertionError(message);
    }
}
