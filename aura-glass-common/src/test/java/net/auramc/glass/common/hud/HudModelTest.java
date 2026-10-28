package net.auramc.glass.common.hud;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
public final class HudModelTest {
    public static void main(String[]args)throws Exception {
        Path folder=Path.of(args[0]);Files.createDirectories(folder);
        HudConfig config=new HudConfig(folder);check(Files.exists(config.file()),"created");
        check(!config.enabled,"HUD master defaults off");
        for(HudElement element:config.registry().layouts().values())check(!element.visible,"every HUD element defaults off");
        HudElement e=config.registry().get(HudRegistry.STATUS);e.offsetX=.125f;e.offsetY=.05f;e.scale=1.75f;e.visible=true;config.biome=true;config.enabled=true;
        check(config.save(),"saved");HudConfig restored=new HudConfig(folder);HudElement copy=restored.registry().get(HudRegistry.STATUS);
        check(copy.offsetX==.125f&&copy.scale==1.75f&&copy.visible&&restored.enabled&&restored.biome,"round trip");
        float before=copy.x(1000,100),after=copy.x(500,100);check(before!=after&&copy.offsetX==.125f&&copy.scale==1.75f,"resize does not mutate layout");
        for(HudElement.Anchor anchor:HudElement.Anchor.values()){e.anchor=anchor;check(Float.isFinite(e.x(1280,100))&&Float.isFinite(e.y(720,30)),"anchor");}
        e.scale=9;e.validate();check(e.scale==2,"scale upper");e.scale=.1f;e.validate();check(e.scale==.5f,"scale lower");
        String malformed="{broken";Files.writeString(config.file(),malformed,StandardCharsets.UTF_8);HudConfig recovered=new HudConfig(folder);
        check(!recovered.enabled&&!recovered.biome&&!recovered.registry().get(HudRegistry.STATUS).visible,"recovery defaults");
        check(Files.readString(folder.resolve("aura-hud.json.bak"),StandardCharsets.UTF_8).equals(malformed),"backup preserves corrupt file");
        var json=com.google.gson.JsonParser.parseString(Files.readString(recovered.file())).getAsJsonObject();json.addProperty("version",0);Files.writeString(recovered.file(),json.toString(),StandardCharsets.UTF_8);new HudConfig(folder);
        check(com.google.gson.JsonParser.parseString(Files.readString(recovered.file())).getAsJsonObject().get("version").getAsInt()==1,"migration");
        MiniPlayer player=new MiniPlayer();player.update();check(!player.available(),"no source hidden");
        HudToasts.Toast[] visible=new HudToasts.Toast[3];int count=0;for(int i=0;i<20;i++)if(HudToasts.show("Notification "+i,HudToasts.Icon.INFO,1000))count++;
        check(count==16,"bounded queue");for(int i=0;i<3;i++)visible[i]=HudToasts.poll();check(visible[2]!=null,"three active");
        var toast=visible[0];toast.started=1_000_000_000L;check(toast.opacity(toast.started)==0&&toast.opacity(toast.started+250_000_000L)==1&&toast.opacity(toast.started+toast.duration)==0,"transforms/opacity envelope");
        System.out.println("HUD: layout,9anchors,resize,atomic save,recovery,backup,migration,source-less player and bounded toasts PASS");
    }
    private static void check(boolean value,String reason){if(!value)throw new AssertionError(reason);}
}
