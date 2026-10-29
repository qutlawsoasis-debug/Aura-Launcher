package net.auramc.glass.client.quickmenu.lyrics;

import com.google.gson.JsonParser;
import com.mojang.blaze3d.systems.RenderSystem;
import net.auramc.glass.client.quickmenu.QuickMenuConfig;
import net.auramc.glass.client.quickmenu.music.MusicRuntime;
import net.fabricmc.fabric.api.client.rendering.v1.WorldRenderEvents;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientLifecycleEvents;
import net.fabricmc.fabric.api.event.player.AttackEntityCallback;
import net.minecraft.client.MinecraftClient;
import net.minecraft.util.ActionResult;
import net.minecraft.util.hit.HitResult;
import net.minecraft.util.math.*;
import net.minecraft.world.RaycastContext;
import org.joml.Matrix4f;
import java.net.*;
import java.net.http.*;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.concurrent.CompletableFuture;

public final class KaraokeRuntime {
    private static final HttpClient HTTP=HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(8)).followRedirects(HttpClient.Redirect.NEVER).build();
    private static final KaraokeEngine ENGINE=new KaraokeEngine(MusicRuntime::positionMs,new KaraokeEngine.CollisionWorld(){
        public boolean blocked(KaraokeEngine.Aabb a){
            var c=MinecraftClient.getInstance();if(c.world==null)return true;
            Box box=new Box(a.minX(),a.minY(),a.minZ(),a.maxX(),a.maxY(),a.maxZ());
            return !c.world.isSpaceEmpty(c.player,box)||!c.world.getOtherEntities(c.player,box,e->e.isAlive()).isEmpty();
        }
        public boolean visible(KaraokeEngine.Vec3 from,KaraokeEngine.Vec3 to){
            var c=MinecraftClient.getInstance();if(c.world==null||c.player==null)return false;
            return c.world.raycast(new RaycastContext(new Vec3d(from.x(),from.y(),from.z()),new Vec3d(to.x(),to.y(),to.z()),RaycastContext.ShapeType.COLLIDER,RaycastContext.FluidHandling.NONE,c.player)).getType()==HitResult.Type.MISS;
        }
    },0x41555241);
    private static String trackId="",status="Текст не загружен";
    private static long lastCombat,lastSample,sampleDuration,totalSamples;
    private static float previousYaw;
    private static double smoothedSpeed,smoothedYaw;
    private static int previousHurt;
    private static boolean initialized;
    private KaraokeRuntime(){}
    public static String status(){return status;}
    public static long frameNanos(){return sampleDuration;}
    public static long samples(){return totalSamples;}
    public static KaraokeEngine engine(){return ENGINE;}
    public static void initialize(){
        if(initialized)return;initialized=true;
        AttackEntityCallback.EVENT.register((player,world,hand,entity,hit)->{if(world.isClient&&player==MinecraftClient.getInstance().player)lastCombat=System.nanoTime();return ActionResult.PASS;});
        WorldRenderEvents.AFTER_ENTITIES.register(context->{
            var c=MinecraftClient.getInstance();var cfg=QuickMenuConfig.get();
            if(!MusicRuntime.playing()||!cfg.karaoke||c.world==null||c.player==null){ENGINE.clear();return;}
            long began=System.nanoTime();
            var current=MusicRuntime.current();
            if(current!=null&&!current.id().equals(trackId)){trackId=current.id();ENGINE.setTrack(LyricsTrack.parse(""));load(current.author(),current.title(),Math.max(current.durationMs(),MusicRuntime.durationMs())/1000.0,trackId);}
            long now=System.nanoTime();
            if(c.player.hurtTime>previousHurt)lastCombat=now;previousHurt=c.player.hurtTime;
            if(lastSample==0||now-lastSample>=100_000_000L){
                double seconds=lastSample==0?.1:(now-lastSample)/1e9;
                smoothedYaw=smoothedYaw*.4+Math.abs(MathHelper.wrapDegrees(c.player.getYaw()-previousYaw))/seconds*.6;
                smoothedSpeed=smoothedSpeed*.4+c.player.getVelocity().horizontalLength()*.6;
                previousYaw=c.player.getYaw();lastSample=now;
            }
            ENGINE.configure(0,cfg.lyricsOffsetMs);
            ENGINE.configureSpatial(cfg.karaokeDistance,cfg.karaokeDensity,cfg.karaokePhysics);
            ENGINE.update(now,new KaraokeEngine.ListenerState(c.player.getX(),c.player.getEyeY(),c.player.getZ(),c.player.getYaw(),smoothedSpeed,smoothedYaw,c.player.isSprinting(),!c.player.isOnGround()&&c.player.getVelocity().y>.05,c.player.getAbilities().flying,c.player.getVelocity().y<-.1,c.currentScreen!=null,c.player.isSubmergedInWater(),c.player.isInLava(),c.player.getHealth(),lastCombat,true));
            var bodies=ENGINE.bodies();if(bodies.isEmpty()){sampleDuration=System.nanoTime()-began;return;}
            WorldLyricsRenderer.initialize();WorldLyricsRenderer.begin();var camera=context.camera();var position=camera.getPos();
            for(var body:bodies){float light=Math.max(.75f,c.world.getLightLevel(BlockPos.ofFloored(body.position().x(),body.position().y(),body.position().z()))/15f);WorldLyricsRenderer.word(body,position.x,position.y,position.z,(float)cfg.karaokeSize,light,1);}
            Matrix4f view=context.matrixStack()==null?RenderSystem.getModelViewMatrix():context.matrixStack().peek().getPositionMatrix();
            WorldLyricsRenderer.draw(new Matrix4f(RenderSystem.getProjectionMatrix()).mul(view));sampleDuration=System.nanoTime()-began;totalSamples++;
        });
        ClientLifecycleEvents.CLIENT_STOPPING.register(client->WorldLyricsRenderer.close());
    }
    public static void load(String artist,String title,double duration,String expectedId){
        if(artist.isBlank()||title.isBlank()||duration<=0){status="Для текста нужны исполнитель, название и длительность";return;}
        status="Загрузка текста";
        String url="https://lobby-api.vercel.app/api/lyrics?artist="+encode(artist)+"&title="+encode(title)+"&duration="+duration;
        CompletableFuture.runAsync(()->{
            try{
                var response=HTTP.send(HttpRequest.newBuilder(URI.create(url)).timeout(Duration.ofSeconds(15)).GET().build(),HttpResponse.BodyHandlers.ofString());
                if(response.statusCode()!=200)throw new IllegalStateException("HTTP "+response.statusCode());
                if(response.body().length()>262144)throw new IllegalStateException("Ответ текста слишком большой");
                var doc=JsonParser.parseString(response.body()).getAsJsonObject();String lrc=doc.has("syncedLyrics")?doc.get("syncedLyrics").getAsString():"";
                var parsed=LyricsTrack.parse(lrc);
                MinecraftClient.getInstance().execute(()->{if(trackId.equals(expectedId)){ENGINE.setTrack(parsed);status=parsed.words().isEmpty()?"Синхронизированного текста нет":"Текст LRCLIB";}});
            }catch(Exception error){MinecraftClient.getInstance().execute(()->{if(trackId.equals(expectedId))status="Текст недоступен: "+error.getMessage();});}
        });
    }
    private static String encode(String text){return URLEncoder.encode(text,StandardCharsets.UTF_8);}
}