package net.auramc.glass.common.hud;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.Map;

public final class HudConfig {
    private static final Gson GSON=new GsonBuilder().setPrettyPrinting().create();
    private final Path file;
    private HudRegistry registry=new HudRegistry();
    public boolean enabled=true,biome;
    public String loadProblem;
    public HudConfig(Path configDirectory){file=configDirectory.resolve("aura-hud.json");load();}
    public HudRegistry registry(){return registry;}
    public Path file(){return file;}
    private void load(){
        if(!Files.exists(file)){save();return;}
        try{
            if(Files.size(file)>1_048_576)throw new IllegalArgumentException("HUD config exceeds1MiB");
            Document doc=GSON.fromJson(Files.readString(file,StandardCharsets.UTF_8),Document.class);
            if(doc==null||doc.version<0||doc.version>1||doc.elements==null||doc.elements.size()>256)throw new IllegalArgumentException("unsupported HUD schema");
            HudRegistry next=new HudRegistry();next.apply(doc.elements);registry=next;enabled=doc.enabled;biome=doc.biome;
            if(doc.version==0)save();
        }catch(Exception error){
            loadProblem=error.getClass().getSimpleName()+": "+error.getMessage();
            registry=new HudRegistry();enabled=true;biome=false;
            try{Files.copy(file,file.resolveSibling("aura-hud.json.bak"),StandardCopyOption.REPLACE_EXISTING);save();}
            catch(Exception backupError){loadProblem+="; backup: "+backupError.getMessage();}
            System.err.println("[AuraGlass] HUD config recovered: "+loadProblem);
        }
    }
    public boolean save(){
        try{
            for(HudElement element:registry.layouts().values())element.validate();
            Document doc=new Document();doc.elements=registry.layouts();doc.enabled=enabled;doc.biome=biome;
            Files.createDirectories(file.getParent());Path tmp=file.resolveSibling("aura-hud.json.tmp");
            Files.writeString(tmp,GSON.toJson(doc)+"\n",StandardCharsets.UTF_8);
            try{Files.move(tmp,file,StandardCopyOption.ATOMIC_MOVE,StandardCopyOption.REPLACE_EXISTING);}
            catch(AtomicMoveNotSupportedException ignored){Files.move(tmp,file,StandardCopyOption.REPLACE_EXISTING);}
            return true;
        }catch(Exception error){System.err.println("[AuraGlass] HUD config save failed: "+error.getMessage());return false;}
    }
    private static final class Document {int version=1;boolean enabled=true,biome;Map<String,HudElement> elements;}
}
