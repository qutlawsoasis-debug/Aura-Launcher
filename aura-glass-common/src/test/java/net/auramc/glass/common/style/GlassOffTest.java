package net.auramc.glass.common.style;

import com.google.gson.Gson;
import com.google.gson.JsonParser;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.charset.StandardCharsets;

public final class GlassOffTest {
    public static void main(String[] args)throws Exception {
        Gson gson=new Gson();
        check(gson.fromJson("\"full\"",GlassQuality.class)==GlassQuality.HIGH,"full alias");
        check(gson.fromJson("\"FULL\"",GlassQuality.class)==GlassQuality.HIGH,"FULL alias");
        check(gson.fromJson("\"off\"",GlassQuality.class)==GlassQuality.OFF,"off alias");
        check(!GlassQuality.OFF.needsBackdrop()&&!GlassQuality.LOW.needsBackdrop(),"flat modes never capture");
        check(GlassQuality.HIGH.needsBackdrop()&&GlassQuality.MEDIUM.needsBackdrop(),"optical modes capture");
        check(GlassQuality.OFF.shaderLevel()==0&&GlassQuality.LOW.shaderLevel()==0,"Off uses existing flat branch");
        GlassStyles.initialize(Path.of(args[0]));
        GlassStyle original=GlassStyles.get(GlassPreset.PANEL);
        GlassStyle off=GlassStyles.material(original,GlassQuality.OFF);
        check(off.tintA>=.94f&&Math.abs(off.tintR-28/255f)<.0001f,"dense configured panel fill");
        check(off.frost==0&&off.refraction==0&&off.dispersion==0&&off.light==0&&off.gloss==0,"Off optical effects disabled");
        check(original.tintA==GlassStyles.get(GlassPreset.PANEL).tintA,"preset not mutated");
        check(off.radius==original.radius&&off.scale==original.scale,"layout unchanged");
        GlassStyle filled=GlassStyles.material(GlassStyles.get(GlassPreset.BUTTON_FILLED),GlassQuality.OFF);
        check(filled.tintR==1&&filled.tintA>=.94f,"primary fill remains distinct");
        GlassStyle selected=GlassStyles.material(GlassStyles.selected(),GlassQuality.OFF);
        check(selected.isSelectedText()&&selected.isThemeTint()&&Math.abs(selected.tintB-1)<.0001f,"selected accent retained");
        GlassStyles.setQuality(GlassQuality.OFF);check(GlassStyles.save()&&GlassStyles.reloadFromFile(),"Off persists");
        check(GlassStyles.quality()==GlassQuality.OFF,"Off reloads");
        var legacy=JsonParser.parseString(Files.readString(GlassStyles.file())).getAsJsonObject();legacy.remove("offFill");
        Files.writeString(GlassStyles.file(),legacy.toString(),StandardCharsets.UTF_8);
        check(GlassStyles.reloadFromFile()&&GlassStyles.material(original).tintA>=.94f,"old v4 file receives dense fill default");
        var bad=JsonParser.parseString(Files.readString(GlassStyles.file())).getAsJsonObject();
        var fill=new com.google.gson.JsonObject();fill.addProperty("alpha",.5);bad.add("offFill",fill);
        Files.writeString(GlassStyles.file(),bad.toString(),StandardCharsets.UTF_8);
        check(!GlassStyles.reloadFromFile(),"transparent Off configuration rejected");
        System.out.println("Off: aliases, capture policy, flat material, preserved presets, migration and persistence PASS");
    }
    private static void check(boolean value,String reason){if(!value)throw new AssertionError(reason);}
}
