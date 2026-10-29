package net.auramc.glass.common.style;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.nio.file.AtomicMoveNotSupportedException;
import java.util.EnumMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;

public final class GlassStyles {
    private static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();
    private static final Set<String> LOGGED = new HashSet<>();
    private static Document document = defaults();
    private static Path path;
    private static long modified = Long.MIN_VALUE, nextPoll;
    private static final GlassStyle[] FRAME_STYLES=new GlassStyle[8192];
    private static int frameCursor;
    static{for(int i=0;i<FRAME_STYLES.length;i++)FRAME_STYLES[i]=new GlassStyle();}

    private GlassStyles() {}

    public static void initialize(Path configDir) {
        path = configDir.resolve("aura-glass/styles.json");
        if (!Files.exists(path)) save();
        nextPoll = 0;
        poll();
    }

    public static void poll() {
        long now = System.nanoTime();
        if (path == null || now < nextPoll) return;
        nextPoll = now + 250_000_000L;
        try {
            long stamp = Files.getLastModifiedTime(path).toMillis();
            if (stamp == modified) return;
            modified = stamp;
            Document next = GSON.fromJson(Files.readString(path, StandardCharsets.UTF_8), Document.class);
            validate(next);
            boolean upgraded = upgrade(next);
            document = next;
            if (upgraded) save();
            log("Styles loaded: version=" + next.version + " quality=" + next.quality + " selectedPreset=" + next.selectedPreset);
        } catch (Exception error) { log("Styles reload rejected: " + error.getMessage()); }
    }

    public static boolean regular(){return !"LIQUID".equals(document.material);}
    private static Map<GlassPreset,GlassStyle> activePresets(){
        return regular()?document.presets:document.profiles.get("LIQUID");
    }
    public static GlassStyle get(GlassPreset preset) { return tagged(preset,activePresets().get(preset)); }
    public static void beginFrame(){frameCursor=0;}
    private static GlassStyle frameCopy(GlassStyle source){GlassStyle target=FRAME_STYLES[frameCursor++%FRAME_STYLES.length];source.copyInto(target);return target;}
    // Frame styles are borrowed scratch values. Persisted edits must use copy().
    public static GlassStyle frame(GlassPreset preset){
        GlassStyle value=frameCopy(activePresets().get(preset));
        return preset.name().startsWith("SELECTED_")?value.selectedText(preset==GlassPreset.SELECTED_TINT):value;
    }
    public static GlassStyle frameSelected(){return frame(document.selectedPreset);}
    private static GlassStyle tagged(GlassPreset preset,GlassStyle value){
        GlassStyle copy=value.copy();
        return preset.name().startsWith("SELECTED_")?copy.selectedText(preset==GlassPreset.SELECTED_TINT):copy;
    }
    public static GlassStyle selected(){return get(document.selectedPreset);}
    public static GlassStyle material(GlassStyle value){ return material(value,document.quality); }
    public static GlassStyle material(GlassStyle value,GlassQuality quality){
        GlassStyle copy=value.copy();
        return applyMaterial(copy,quality);
    }
    public static void materialInto(GlassStyle source,GlassQuality quality,GlassStyle target){source.copyInto(target);applyMaterial(target,quality);}
    public static GlassStyle frameMaterial(GlassStyle source){return applyMaterial(frameCopy(source),document.quality);}
    private static GlassStyle applyMaterial(GlassStyle copy,GlassQuality quality){
        if(!regular() && copy.isThemeTint()){int rgb=document.theme.rgb;copy.tintR*=((rgb>>16)&255)/255f;copy.tintG*=((rgb>>8)&255)/255f;copy.tintB*=(rgb&255)/255f;}
        if (quality == GlassQuality.LOW) {
            copy.tintA = Math.max(copy.tintA, .12f);
            if(regular()&&copy.renderPath()==1){copy.tintR=.12f;copy.tintG=.13f;copy.tintB=.16f;copy.tintA=Math.max(copy.tintA,.90f);}
        }
        if(quality==GlassQuality.OFF){
            FlatFill fill=document.offFill;
            if(!copy.isSelectedText() && copy.tintA<.5f){copy.tintR=fill.red;copy.tintG=fill.green;copy.tintB=fill.blue;}
            copy.tintA=Math.max(copy.tintA,fill.alpha);
            copy.refraction=copy.dispersion=copy.frost=copy.light=copy.gloss=copy.depth=copy.innerGlow=copy.gradient=copy.noise=copy.wobble=0;
            copy.rimOuter*=.25f;
        }
        return copy;
    }
    public static GlassQuality quality() { return document.quality; }
    public static void setQuality(GlassQuality quality) { document.quality = quality == null ? GlassQuality.HIGH : quality; }
    public static void update(GlassPreset preset, GlassStyle style) {
        GlassStyle copy = style.copy(); copy.validate(); activePresets().put(preset, copy);
    }

    public static GlassStyle resolve(GlassPreset preset, GlassState state, float amount) {
        return resolve(get(preset), state, amount);
    }

    public static GlassStyle resolve(GlassStyle base, GlassState state, float amount) {
        GlassStyle result = base.copy();
        return applyState(result,state,amount);
    }
    public static GlassStyle frameResolve(GlassPreset preset,GlassState state,float amount){return frameResolve(frame(preset),state,amount);}
    public static GlassStyle frameResolve(GlassStyle base,GlassState state,float amount){return applyState(frameCopy(base),state,amount);}
    private static GlassStyle applyState(GlassStyle result,GlassState state,float amount){
        Modifier modifier = document.states.get(state);
        float t = Float.isFinite(amount) ? Math.max(0, Math.min(1, amount)) : 0;
        result.scale *= 1 + (modifier.scale - 1) * t;
        result.rimOuter *= 1 + (modifier.rim - 1) * t;
        result.gloss *= 1 + (modifier.gloss - 1) * t;
        result.tintA = Math.max(0, Math.min(1, result.tintA + modifier.tintAlpha * t));
        result.saturation *= 1 + (modifier.saturation - 1) * t;
        result.light *= 1 + (modifier.light - 1) * t;
        return result;
    }

    public static boolean save() {
        if (path == null) return false;
        try {
            validate(document);
            Files.createDirectories(path.getParent());
            Path temporary = path.resolveSibling("styles.json.tmp");
            Files.writeString(temporary, GSON.toJson(document) + "\n", StandardCharsets.UTF_8);
            try { Files.move(temporary, path, StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING); }
            catch (AtomicMoveNotSupportedException unsupported) { Files.move(temporary, path, StandardCopyOption.REPLACE_EXISTING); }
            modified = Files.getLastModifiedTime(path).toMillis();
            return true;
        } catch (Exception error) { log("Styles save failed: " + error.getMessage()); return false; }
    }

    public static Path file() { return path; }
    public static void setFile(Path p) { path = p; }

    private static void validate(Document candidate) {
        if (candidate == null || candidate.quality == null || candidate.presets == null || candidate.states == null)
            throw new IllegalArgumentException("quality, presets and states are required");
        if(candidate.version>5)throw new IllegalArgumentException("unsupported styles version");
        if(candidate.version==5 && (!"REGULAR".equals(candidate.material)&&!"LIQUID".equals(candidate.material)))throw new IllegalArgumentException("material must be REGULAR or LIQUID");
        if(candidate.version==5){
            if(candidate.profiles==null || candidate.profiles.get("LIQUID")==null)throw new IllegalArgumentException("profiles.LIQUID is required");
            for(GlassPreset preset:GlassPreset.values()){
                GlassStyle liquid=candidate.profiles.get("LIQUID").get(preset);
                if(liquid==null)throw new IllegalArgumentException("Missing LIQUID preset "+preset);
                liquid.validate();
            }
        }
        if(candidate.version<4){
            Document fresh=defaults();
            for(GlassPreset preset:new GlassPreset[]{GlassPreset.SELECTED_GLASS,GlassPreset.SELECTED_TINT})candidate.presets.putIfAbsent(preset,fresh.presets.get(preset).copy());
        }
        if(candidate.selectedPreset!=GlassPreset.SELECTED_DARK && candidate.selectedPreset!=GlassPreset.SELECTED_GLASS && candidate.selectedPreset!=GlassPreset.SELECTED_TINT)throw new IllegalArgumentException("selectedPreset must name a selected preset");
        if(candidate.theme==null || candidate.theme.accent==null || !candidate.theme.accent.matches("#[0-9a-fA-F]{6}"))throw new IllegalArgumentException("theme.accent must be #RRGGBB");
        candidate.theme.rgb=Integer.parseInt(candidate.theme.accent.substring(1),16);
        if(candidate.offFill==null)throw new IllegalArgumentException("offFill is required");
        candidate.offFill.validate();
        for (GlassPreset preset : GlassPreset.values()) {
            GlassStyle style = candidate.presets.get(preset);
            if (style == null) throw new IllegalArgumentException("Missing preset " + preset);
            style.validate();
        }
        for (GlassState state : GlassState.values()) {
            Modifier modifier = candidate.states.get(state);
            if (modifier == null) throw new IllegalArgumentException("Missing state " + state);
            for (var field : Modifier.class.getFields()) {
                try {
                    float value = field.getFloat(modifier);
                    if (!Float.isFinite(value) || value < -1 || value > 4)
                        throw new IllegalArgumentException("Invalid state modifier " + state + "." + field.getName());
                } catch (IllegalAccessException impossible) { throw new IllegalStateException(impossible); }
            }
        }
    }

    private static Document legacyDefaults() {
        Document doc = new Document(); doc.version = 1;
        for (GlassPreset preset : GlassPreset.values()) doc.presets.put(preset, new GlassStyle());
        for (GlassStyle style : doc.presets.values()) style.lightDirY = .83f;
        GlassStyle panel = doc.presets.get(GlassPreset.PANEL);
        panel.refraction = 3; panel.bevel = 10; panel.frost = 2.7f; panel.tintA = .12f;
        GlassStyle pill = doc.presets.get(GlassPreset.PILL_THICK);
        pill.refraction = 18; pill.bevel = -1; pill.radius = -1; pill.frost = .8f; pill.depth = .28f; pill.gloss = .45f;
        GlassStyle filled = doc.presets.get(GlassPreset.BUTTON_FILLED);
        filled.tintA = .7f; filled.bevel = -1; filled.radius = -1; filled.gradient = .25f; filled.innerGlow = .3f;
        GlassStyle dark = doc.presets.get(GlassPreset.SELECTED_DARK);
        dark.tintR = .04f; dark.tintG = .06f; dark.tintB = .09f; dark.tintA = .6f;
        dark.refraction = .2f; dark.rimOuter = .7f; dark.light = .6f;
        for (GlassState state : GlassState.values()) doc.states.put(state, new Modifier());
        Modifier hover = doc.states.get(GlassState.HOVER); hover.scale = 1.03f; hover.rim = 1.4f; hover.gloss = 1.3f;
        Modifier press = doc.states.get(GlassState.PRESS); press.scale = .97f; press.rim = 1.15f; press.gloss = .8f;
        Modifier disabled = doc.states.get(GlassState.DISABLED); disabled.saturation = .2f; disabled.light = .4f; disabled.gloss = .3f;
        Modifier focused = doc.states.get(GlassState.FOCUSED); focused.rim = 1.7f; focused.light = 1.4f;
        return doc;
    }

    private static Document defaults() {
        try(var input=GlassStyles.class.getResourceAsStream("/assets/aura-glass/styles.json")){
            if(input!=null)return GSON.fromJson(new java.io.InputStreamReader(input,StandardCharsets.UTF_8),Document.class);
        }catch(Exception error){log("Default styles unavailable: "+error.getMessage());}
        return legacyDefaults();
    }

    private static boolean upgrade(Document next) {
        if(next.version>=5)return false;
        Document fresh=defaults();
        next.profiles=new java.util.LinkedHashMap<>();
        next.profiles.put("LIQUID",next.presets);
        next.profiles.put("REGULAR",fresh.presets);
        next.presets=fresh.presets;next.states=fresh.states;next.material="REGULAR";next.version=5;
        log("Styles upgraded to version 5 REGULAR; quality retained, previous presets available as LIQUID");
        return true;
    }

    public static Map<GlassPreset, GlassStyle> filePresets() throws java.io.IOException {
        Document file = GSON.fromJson(Files.readString(path, StandardCharsets.UTF_8), Document.class);
        validate(file); upgrade(file);
        Map<GlassPreset, GlassStyle> result = new EnumMap<>(GlassPreset.class);
        Map<GlassPreset,GlassStyle> source="LIQUID".equals(file.material)?file.profiles.get("LIQUID"):file.presets;
        for (GlassPreset preset : GlassPreset.values()) result.put(preset, tagged(preset,source.get(preset)));
        return result;
    }

    public static boolean reloadFromFile() {
        try {
            Document next = GSON.fromJson(Files.readString(path, StandardCharsets.UTF_8), Document.class);
            validate(next); boolean upgraded = upgrade(next); document = next;
            modified = Files.getLastModifiedTime(path).toMillis();
            if (upgraded) save();
            return true;
        } catch (Exception error) { log("Styles reset rejected: " + error.getMessage()); return false; }
    }

    private static void log(String reason) { if (LOGGED.add(reason)) System.err.println("[AuraGlass] " + reason); }
    private static final class Document {
        int version = 5;
        String material="REGULAR";
        Map<String,Map<GlassPreset,GlassStyle>> profiles=new java.util.LinkedHashMap<>();
        GlassPreset selectedPreset=GlassPreset.SELECTED_TINT;
        Theme theme=new Theme();
        FlatFill offFill=new FlatFill();
        GlassQuality quality = GlassQuality.HIGH;
        Map<GlassPreset, GlassStyle> presets = new EnumMap<>(GlassPreset.class);
        Map<GlassState, Modifier> states = new EnumMap<>(GlassState.class);
    }
    private static final class FlatFill {
        float red=28/255f,green=30/255f,blue=36/255f,alpha=.94f;
        void validate(){
            if(!Float.isFinite(red)||!Float.isFinite(green)||!Float.isFinite(blue)||!Float.isFinite(alpha)
                    ||red<0||red>1||green<0||green>1||blue<0||blue>1||alpha<.94f||alpha>1)
                throw new IllegalArgumentException("offFill RGB must be0..1 and alpha0.94..1");
        }
    }
    private static final class Theme {
        String accent="#40C4FF";
        transient int rgb=0x40C4FF;
    }
    public static final class Modifier {
        public float scale = 1, rim = 1, gloss = 1, tintAlpha = 0, saturation = 1, light = 1;
    }
}
