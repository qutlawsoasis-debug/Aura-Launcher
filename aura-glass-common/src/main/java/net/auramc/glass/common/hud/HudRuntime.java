package net.auramc.glass.common.hud;

import com.mojang.blaze3d.systems.RenderSystem;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.keybinding.v1.KeyBindingHelper;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.ChatScreen;
import net.minecraft.client.gui.screen.ingame.HandledScreen;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.client.util.InputUtil;
import net.auramc.glass.common.render.AuraRenderer;
import net.auramc.glass.common.style.GlassQuality;
import net.auramc.glass.common.style.GlassStyles;
import java.time.LocalTime;
import java.time.format.DateTimeFormatter;

public final class HudRuntime {
    public static final MiniPlayer MINI_PLAYER=new MiniPlayer();
    private static final DateTimeFormatter CLOCK=DateTimeFormatter.ofPattern("HH:mm");
    private static final String[] DIGITS={"0","1","2","3","4","5","6","7","8","9"};
    private static final float[] DIGIT_WIDTH=new float[10];
    private static final HudToasts.Toast[] ACTIVE=new HudToasts.Toast[3];
    private static HudConfig config;
    private static HudElement status,coords,toasts,player;
    private static KeyBinding previous,play,next;
    private static long nextUpdate;
    private static String fps="",time="",position="",biome="";
    private static float cell,statusWidth,coordsWidth;
    private static boolean metricsReady;
    private static int oldFps=-1,oldX,oldY,oldZ,oldDirection=-1;
    private static long oldMinute=Long.MIN_VALUE;
    private static Object oldWorld;
    private static boolean oldBiome;
    private static java.util.function.BooleanSupplier debugVisible=()->false;
    public static boolean lastRendered;
    public static int lastDrawCalls;
    private HudRuntime(){}

    public static void initialize(java.util.function.BooleanSupplier debugSupplier){
        debugVisible=debugSupplier;
        config=new HudConfig(FabricLoader.getInstance().getConfigDir());
        status=config.registry().get(HudRegistry.STATUS);coords=config.registry().get(HudRegistry.COORDS);
        toasts=config.registry().get(HudRegistry.TOASTS);player=config.registry().get(HudRegistry.PLAYER);
        previous=key("key.aura.hud.previous");play=key("key.aura.hud.play");next=key("key.aura.hud.next");
        ClientTickEvents.END_CLIENT_TICK.register(HudRuntime::tick);
    }
    private static KeyBinding key(String name){return KeyBindingHelper.registerKeyBinding(new KeyBinding(name,InputUtil.Type.KEYSYM,-1,"category.aura"));}
    public static HudConfig config(){return config;}
    public static boolean musicAvailable(){return MINI_PLAYER.available();}
    private static void tick(MinecraftClient client){
        if(client.world==null||client.player==null)return;
        while(previous.wasPressed())if(client.currentScreen==null)MINI_PLAYER.previous();
        while(play.wasPressed())if(client.currentScreen==null)MINI_PLAYER.togglePlayback();
        while(next.wasPressed())if(client.currentScreen==null)MINI_PLAYER.next();
        if(!config.enabled||!AuraRenderer.isReady())return;
        long now=System.nanoTime();if(now<nextUpdate)return;nextUpdate=now+250_000_000L;
        if(!metricsReady){
            for(int i=0;i<10;i++){DIGIT_WIDTH[i]=AuraRenderer.textWidth(DIGITS[i],13);cell=Math.max(cell,DIGIT_WIDTH[i]);}
            metricsReady=true;
        }
        int currentFps=client.getCurrentFps();
        if(currentFps!=oldFps){oldFps=currentFps;fps=Integer.toString(currentFps);statusWidth=106+Math.max(4,fps.length())*cell+5*cell;}
        long minute=System.currentTimeMillis()/60000;
        if(minute!=oldMinute){oldMinute=minute;time=LocalTime.now().format(CLOCK);}
        int x=client.player.getBlockX(),y=client.player.getBlockY(),z=client.player.getBlockZ(),direction=client.player.getHorizontalFacing().getId();
        if(oldWorld!=client.world||oldX!=x||oldY!=y||oldZ!=z||oldDirection!=direction||oldBiome!=config.biome){
            oldWorld=client.world;oldX=x;oldY=y;oldZ=z;oldDirection=direction;oldBiome=config.biome;
            position="XYZ "+x+" "+y+" "+z+" · "+client.player.getHorizontalFacing().asString().toUpperCase(java.util.Locale.ROOT);
            biome=config.biome?client.world.getBiome(client.player.getBlockPos()).getKey().map(k->k.getValue().toString()).orElse("—"):"";
            coordsWidth=Math.min(310,Math.max(AuraRenderer.textWidth(position,13),AuraRenderer.textWidth(biome,11))+24);
        }
        for(int i=0;i<ACTIVE.length;i++){
            HudToasts.Toast toast=ACTIVE[i];if(toast!=null&&now-toast.started>=toast.duration)ACTIVE[i]=null;
            if(ACTIVE[i]==null){toast=HudToasts.poll();if(toast!=null){toast.started=now;toast.width=Math.min(300,AuraRenderer.textWidth(toast.text,13)+40);var fitted=net.auramc.glass.common.ui.GlassTextFit.fit(toast.text,13,toast.width-36,value->AuraRenderer.textWidth(value,1));toast.display=fitted.text();toast.fontSize=fitted.size();ACTIVE[i]=toast;}}
        }
        try{MINI_PLAYER.update();}catch(RuntimeException error){MINI_PLAYER.source(null);System.err.println("[AuraGlass] MiniPlayer source disabled: "+error.getMessage());}
    }

    public static boolean eligible(MinecraftClient client){
        return config!=null&&config.enabled&&client.world!=null&&client.player!=null&&!client.options.hudHidden
                &&!debugVisible.getAsBoolean()
                &&(client.currentScreen==null||client.currentScreen instanceof ChatScreen||client.currentScreen instanceof HandledScreen<?>);
    }
    public static void render(DrawContext context){
        lastRendered=false;lastDrawCalls=0;
        MinecraftClient client=MinecraftClient.getInstance();
        if(!eligible(client)||!metricsReady||!AuraRenderer.isReady())return;
        context.draw();
        if(!AuraRenderer.begin(context.getMatrices().peek().getPositionMatrix(),RenderSystem.getProjectionMatrix(),RenderSystem.getModelViewMatrix()))return;
        int width=client.getWindow().getScaledWidth(),height=client.getWindow().getScaledHeight();
        if(status.visible){
            float s=status.scale,x=status.x(width,statusWidth),y=status.y(height,36);
            panel(x,y,statusWidth*s,36*s,18*s,1);
            AuraRenderer.rect(x+10*s,y+16*s,4*s,4*s,2*s,0,0xFF64D2FF,0);
            text("Aura",x+21*s,y+10*s,13*s,1);
            text("·",x+51*s,y+10*s,13*s,.78f);
            float fx=x+61*s;
            digits(fps,fx+(Math.max(4,fps.length())-fps.length())*cell*s,y+10*s,s);
            text("FPS",fx+Math.max(4,fps.length())*cell*s+5*s,y+10*s,11*s,1);
            text("·",fx+Math.max(4,fps.length())*cell*s+24*s,y+10*s,13*s,.78f);
            digits(time,fx+Math.max(4,fps.length())*cell*s+31*s,y+10*s,s);
        }
        if(coords.visible){
            float s=coords.scale,h=config.biome?43:28,x=coords.x(width,coordsWidth),y=coords.y(height,h);
            panel(x,y,coordsWidth*s,h*s,14*s,1);
            fitted(position,x+12*s,y+6*s,13*s,(coordsWidth-24)*s,1);
            if(config.biome)fitted(biome,x+12*s,y+23*s,11*s,(coordsWidth-24)*s,.78f);
        }
        if(toasts.visible){
            long now=System.nanoTime();
            float s=toasts.scale,x,y=toasts.y(height,3*36);
            for(int i=0;i<ACTIVE.length;i++){
                HudToasts.Toast toast=ACTIVE[i];if(toast==null)continue;
                float alpha=toast.opacity(now);x=toasts.x(width,toast.width);
                float yy=y+i*36*s+(1-alpha)*4*s;
                panel(x,yy,toast.width*s,28*s,14*s,alpha);
                AuraRenderer.rect(x+10*s,yy+11*s,6*s,6*s,toast.icon==HudToasts.Icon.INFO?3*s:1*s,0,rgba(0xFF64D2FF,alpha),0);
                text(toast.display,x+26*s,yy+6*s,toast.fontSize*s,alpha);
                text(toast.icon==HudToasts.Icon.INFO?"i":toast.icon==HudToasts.Icon.SUCCESS?"+":"!",x+10*s,yy+6*s,11*s,alpha);
            }
        }
        if(player.visible&&MINI_PLAYER.available()){
            float s=player.scale,x=player.x(width,220),y=player.y(height,49);
            panel(x,y,220*s,49*s,14*s,1);
            fitted(MINI_PLAYER.track().title(),x+12*s,y+6*s,13*s,196*s,1);
            fitted(MINI_PLAYER.track().artist()==null?"":MINI_PLAYER.track().artist(),x+12*s,y+25*s,11*s,196*s,.78f);
        }
        AuraRenderer.flush();lastDrawCalls=AuraRenderer.batchDrawCalls();lastRendered=lastDrawCalls>0;
    }
    private static void panel(float x,float y,float w,float h,float radius,float opacity){
        int color=switch(GlassStyles.quality()){
            case OFF->0xF01C1E24;
            case LOW->0xD11C1E24;
            default->0x8C1C1E24;
        };
        AuraRenderer.rect(x,y,w,h,radius,1,rgba(color,opacity),rgba(0x1AFFFFFF,opacity));
        AuraRenderer.rect(x+radius,y+1,Math.max(0,w-2*radius),1,0,0,rgba(0x66FFFFFF,opacity),0);
    }
    private static int rgba(int color,float opacity){return ((int)((color>>>24)*opacity)<<24)|(color&0xFFFFFF);}
    private static void text(String label,float x,float y,float size,float opacity){
        AuraRenderer.text(label,x+.5f,y+.5f,size,rgba(0x99000000,opacity));
        AuraRenderer.text(label,x,y,size,rgba(0xFFFFFFFF,opacity));
    }
    private static void fitted(String label,float x,float y,float size,float width,float opacity){
        float measured=AuraRenderer.textWidth(label,size);if(measured>width)size*=width/measured;
        text(label,x,y,size,opacity);
    }
    private static void digits(String label,float x,float y,float scale){
        for(int i=0;i<label.length();i++){
            char c=label.charAt(i);
            if(c>='0'&&c<='9')text(DIGITS[c-'0'],x+(cell-DIGIT_WIDTH[c-'0'])*scale*.5f,y,13*scale,1);
            else text(":",x,y,13*scale,1);
            x+=cell*scale;
        }
    }
}
