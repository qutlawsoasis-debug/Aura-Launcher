package net.auramc.glass.client.quickmenu;

import com.mojang.blaze3d.systems.RenderSystem;
import net.auramc.glass.common.render.AuraRenderer;
import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.GlassPreset;
import net.auramc.glass.common.style.GlassStyles;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.text.Text;
import net.auramc.glass.client.quickmenu.music.MusicRuntime;
import net.auramc.glass.client.quickmenu.music.MusicTrack;
import org.lwjgl.glfw.GLFW;
import java.time.LocalTime;
import java.time.format.DateTimeFormatter;
import java.util.*;
import java.util.function.*;

public final class QuickMenuScreen extends Screen {
    private static final String[] SECTIONS = {"Визуал", "Мир", "Музыка"};
    private static final DateTimeFormatter CLOCK = DateTimeFormatter.ofPattern("HH:mm");
    private static final int ACCENT = 0xFF64D2FF, ACCENT_TEXT = 0xFF00283A;
    private static final int[] COLORS = {0xFFFFFF, 0x64D2FF, 0x5FD39A, 0xFFCC66, 0xFF8A7A};
    private final QuickMenuMotion motion = new QuickMenuMotion();
    private final List<Control> controls = new ArrayList<>();
    private int section, focused, scroll;
    private boolean dragging, menuKeyHeld = true, searching;
    private String searchText = "";
    private int musicTab;
    private double dragX, dragY;
    private long lastHeaderClick, renderedFrames;
    private Control pressed;
    private float textOpacity = 1;
    private float clipLeft, clipTop, clipRight, clipBottom;
    private int mouseX, mouseY;
    private record Control(float x, float y, float w, float h, String label, Runnable action, DoubleConsumer slide, boolean navigation, double sliderValue) {
        Control(float x,float y,float w,float h,String label,Runnable action,DoubleConsumer slide) { this(x,y,w,h,label,action,slide,false,0); }
        boolean contains(double px, double py) { return px >= x && px <= x+w && py >= y && py <= y+h; }
    }
    public QuickMenuScreen() { super(Text.literal("Aura")); }
    @Override protected void init() {
        var config = QuickMenuConfig.get();
        if (renderedFrames == 0) motion.setOpen(true, System.nanoTime());
        motion.update(System.nanoTime(), width, height, config.centerX, config.bottomY < 0 ? Double.NaN : config.bottomY);
    }
    public QuickMenuMotion.Geometry geometry() { return motion.geometry(); }
    public long renderedFrames() { return renderedFrames; }
    public int section() { return section; }
    public void selectSection(int index) { section = Math.floorMod(index, SECTIONS.length); scroll = 0; focused = 0; }
    public void toggle() { motion.setOpen(!motion.isOpen(), System.nanoTime()); }
    @Override public void close() { motion.setOpen(false, System.nanoTime()); }
    @Override public boolean shouldPause() { return true; }
    @Override public void removed() {}

    @Override public void render(DrawContext context, int mx, int my, float delta) {
        mouseX = mx; mouseY = my; renderedFrames++;
        var config = QuickMenuConfig.get();
        var box = motion.update(System.nanoTime(), width, height, config.centerX, config.bottomY < 0 ? Double.NaN : config.bottomY);
        controls.clear();
        clipLeft=box.x()+8;clipTop=box.y();clipRight=box.x()+box.width()-8;clipBottom=box.bottom()-8;
        if (motion.settledClosed()) { client.setScreen(null); return; }
        context.draw();
        if (GlassStyles.quality().needsBackdrop()) {
            QuickMenuRuntime.totalCaptureRequests++;
            GlassRenderer.captureBackdrop();
        }
        var style = GlassStyles.get(GlassPreset.PANEL);
        style.radius = box.radius(); style.bevel = 2.5f; style.refraction = .45f; style.dispersion = 0;
        style.wobble = 0; style.depth = .02f; style.frost = 1.25f; style.gloss = .08f;
        style.rimOuter = .18f; style.rimInner = .4f; style.light = .18f;
        style.tintR = .025f; style.tintG = .035f; style.tintB = .05f;
        style.tintA = GlassStyles.quality().needsBackdrop() ? .78f : .96f;
        int before = GlassRenderer.drawCalls();
        GlassRenderer.draw(box.x(), box.y(), box.width(), box.height(), style, Math.min(1, box.progress()*5+.05f));
        GlassRenderer.flush();
        QuickMenuRuntime.totalDrawCalls += Math.max(0, GlassRenderer.drawCalls()-before);
        if (box.contentOpacity() <= .01f) return;
        context.enableScissor((int)Math.ceil(box.x()+8), (int)Math.ceil(box.y()+8), (int)(box.x()+box.width()-8), (int)(box.bottom()-8));
        boolean scissorActive=true;
        try {
            if (!AuraRenderer.begin(context.getMatrices().peek().getPositionMatrix(), RenderSystem.getProjectionMatrix(), RenderSystem.getModelViewMatrix())) return;
            textOpacity = box.sectionOpacity(0);
            float x = box.x(), y = box.y()+box.sectionOffset(0), right = x+box.width();
            AuraRenderer.rect(x+22,y,box.width()-44,1,0,0,color(0x66FFFFFF),0);
            text("Aura",x+24,y+11,25,0xFFFFFFFF,600);
            String name = client.player == null ? client.getSession().getUsername() : client.player.getName().getString();
            text(name,right-205,y+16,15,0xFFFFFFFF,500);
            text(LocalTime.now().format(CLOCK),right-69,y+16,15,0xFFFFFFFF,500);
            AuraRenderer.rect(x+16,y+44,box.width()-32,1,0,0,color(0x1AFFFFFF),0);
            float navWidth = Math.min(170, box.width()*.27f);
            for (int i=0;i<SECTIONS.length;i++) {
                final int item=i; float ny=y+63+i*37;
                boolean active=i==section;
                rect(x+14,ny,navWidth-22,31,8,active?0x5C767680:0x00767680,active?0x1AFFFFFF:0);
                icon(i,x+25,ny+9,active?ACCENT:0xDDFFFFFF);
                text(SECTIONS[i],x+45,ny+7,16,0xFFFFFFFF,active?600:400);
                controls.add(new Control(x+14,ny,navWidth-22,31,SECTIONS[i],()->selectSection(item),null,true,0));
            }
            float contentX=x+navWidth+8, contentW=box.width()-navWidth-32;
            textOpacity=box.sectionOpacity(1); float contentY=y+58+box.sectionOffset(1);
            text(SECTIONS[section],contentX,contentY,38,0xFFFFFFFF,600);
            flushNative();
            context.disableScissor();scissorActive=false;
            context.enableScissor((int)contentX,(int)(y+112),(int)(right-18),(int)(box.bottom()-39));scissorActive=true;
            if (AuraRenderer.begin(context.getMatrices().peek().getPositionMatrix(),RenderSystem.getProjectionMatrix(),RenderSystem.getModelViewMatrix())) {
                clipLeft=contentX;clipTop=y+112;clipRight=right-18;clipBottom=box.bottom()-39;
                float rowsY=y+118-scroll+box.sectionOffset(2);textOpacity=box.sectionOpacity(2);
                if(section==0)visual(contentX,rowsY,contentW);
                else if(section==1)world(contentX,rowsY,contentW);
                else if(section==2)music(context,contentX,rowsY,contentW);
                else text("Скоро",contentX,rowsY+14,20,0xCCFFFFFF,400);
                flushNative();
            }
            context.disableScissor();scissorActive=false;
            if(AuraRenderer.begin(context.getMatrices().peek().getPositionMatrix(),RenderSystem.getProjectionMatrix(),RenderSystem.getModelViewMatrix())) {
                clipLeft=box.x()+8;clipTop=box.y();clipRight=box.x()+box.width()-8;clipBottom=box.bottom()-8;
                textOpacity=box.contentOpacity();
                if(!controls.isEmpty()){var focus=controls.get(Math.min(focused,controls.size()-1));if(focus.y>=box.y()+44&&focus.y+focus.h<box.bottom()-36)rect(focus.x-2,focus.y-2,focus.w+4,focus.h+4,9,0,ACCENT);}
                text("F8 — закрыть",x+24,box.bottom()-28,13,0xCCFFFFFF,400);flushNative();
            }
            if(box.contentOpacity()>.9f) {
                var skin=QuickMenuPlatform.skin(client);
                if(skin!=null){RenderSystem.setShaderColor(1,1,1,box.contentOpacity());context.drawTexture(skin,(int)(right-234),(int)(y+12),20,20,8,8,8,8,64,64);context.draw();RenderSystem.setShaderColor(1,1,1,1);}
            }
        } finally { if(scissorActive)context.disableScissor(); RenderSystem.setShaderColor(1,1,1,1); }
    }
    private void nativeClip() {
        double scale=client.getWindow().getScaleFactor();
        int left=(int)Math.ceil(clipLeft*scale), right=(int)Math.floor(clipRight*scale);
        int top=(int)Math.ceil(clipTop*scale), bottom=(int)Math.floor(clipBottom*scale);
        org.lwjgl.opengl.GL33C.glEnable(org.lwjgl.opengl.GL33C.GL_SCISSOR_TEST);
        org.lwjgl.opengl.GL33C.glScissor(left,client.getWindow().getFramebufferHeight()-bottom,Math.max(0,right-left),Math.max(0,bottom-top));
    }
    private void flushNative() { nativeClip(); AuraRenderer.flush(); }
    private void visual(float x,float y,float w) {
        var c=QuickMenuConfig.get();
        toggle(x,y,w,"Zoom · удерживать C",()->c.zoomEnabled,v->c.zoomEnabled=v);
        slider(x,y+=38,w,"Кратность",c.zoomFactor,1,16,v->c.zoomFactor=v,"×");
        toggle(x,y+=38,w,"Fullbright",()->c.fullbright,v->c.fullbright=v);
        slider(x,y+=38,w,"Яркость",c.brightness,0,1,v->c.brightness=v,"");
        choice(x,y+=38,w,"Прицел",new String[]{"Ванильный","Точка","Крест","Круг"},c.crosshairStyle,v->{c.crosshairStyle=v;c.customCrosshair=v!=0;});
        choice(x,y+=38,w,"Цвет прицела",new String[]{"Белый","Голубой","Зелёный","Янтарный","Красный"},colorIndex(c.crosshairColor),v->c.crosshairColor=COLORS[v]);
        slider(x,y+=38,w,"Размер прицела",c.crosshairSize,2,16,v->c.crosshairSize=v,"");
        toggle(x,y+=38,w,"Контур блока",()->c.blockOutline,v->c.blockOutline=v);
        choice(x,y+=38,w,"Цвет контура",new String[]{"Белый","Голубой","Зелёный","Янтарный","Красный"},colorIndex(c.outlineColor),v->c.outlineColor=COLORS[v]);
        slider(x,y+=38,w,"Толщина контура",c.outlineWidth,1,5,v->c.outlineWidth=v,"");
        choice(x,y+=38,w,"Частицы",new String[]{"Все","Меньше","Минимум"},c.particles,v->c.particles=v);
        toggle(x,y+=38,w,"Низкий огонь",()->c.lowFire,v->c.lowFire=v);
        toggle(x,y+=38,w,"Низкий щит",()->c.lowShield,v->c.lowShield=v);
        toggle(x,y+=38,w,"Частицы удара",()->c.hitParticles,v->c.hitParticles=v);
        choice(x,y+=38,w,"Стиль частиц",new String[]{"Крит","Искры","Сердца"},c.hitParticleStyle,v->c.hitParticleStyle=v);
        choice(x,y+=38,w,"Цвет частиц",new String[]{"Белый","Голубой","Зелёный","Янтарный","Красный"},colorIndex(c.hitParticleColor),v->c.hitParticleColor=COLORS[v]);
        slider(x,y+=38,w,"Количество частиц",c.hitParticleCount,1,24,v->c.hitParticleCount=(int)Math.round(v),"");
        toggle(x,y+=38,w,"Числа урона и лечения",()->c.damageNumbers,v->c.damageNumbers=v);
        toggle(x,y+=38,w,"Плашка цели",()->c.targetHud,v->c.targetHud=v);
        toggle(x,y+=38,w,"Своя траектория",()->c.projectilePrediction,v->c.projectilePrediction=v);
        toggle(x,y+=38,w,"Цвет попадания",()->c.hitTint,v->c.hitTint=v);
        choice(x,y+=38,w,"Оттенок попадания",new String[]{"Белый","Голубой","Зелёный","Янтарный","Красный"},colorIndex(c.hitTintColor),v->c.hitTintColor=COLORS[v]);
        toggle(x,y+=38,w,"Положение руки",()->c.handTransform,v->c.handTransform=v);
        slider(x,y+=38,w,"Рука · X",c.handOffsetX,-1,1,v->c.handOffsetX=v,"");
        slider(x,y+=38,w,"Рука · Y",c.handOffsetY,-1,1,v->c.handOffsetY=v,"");
        slider(x,y+=38,w,"Рука · Z",c.handOffsetZ,-1,1,v->c.handOffsetZ=v,"");
        slider(x,y+=38,w,"Масштаб руки",c.handScale,.5,1.5,v->c.handScale=v,"");
        choice(x,y+=38,w,"Взмах",new String[]{"Ванильный","Мягкий","Компактный"},c.swingAnimation,v->c.swingAnimation=v);
        toggle(x,y+=38,w,"Кулдауны у прицела",()->c.cooldownHud,v->c.cooldownHud=v);
    }
    private void world(float x,float y,float w) {
        var c=QuickMenuConfig.get();
        toggle(x,y,w,"Свое время суток",()->c.visualTime,v->c.visualTime=v);
        slider(x,y+=44,w,"Время",c.timeOfDay,0,23999,v->c.timeOfDay=v,"");
        toggle(x,y+=44,w,"Без осадков",()->c.clearWeather,v->c.clearWeather=v);
        text("Меняется только отображение на этом клиенте.",x,y+52,13,0xCCFFFFFF,400);
    }
    private void music(DrawContext context,float x,float y,float w) {
        var current=MusicRuntime.current();
        String title=current==null?"Ничего не играет":current.title();
        String artist=current==null?"Выберите трек из библиотеки":current.author();
        rect(x,y,84,84,10,0x3D767680,0x1AFFFFFF);
        if(current!=null){
            var cover=net.auramc.glass.client.quickmenu.music.MusicArtwork.texture(current);
            if(cover!=null){flushNative();context.drawTexture(cover,(int)x,(int)y,0,0,84,84,84,84);context.draw();AuraRenderer.begin(context.getMatrices().peek().getPositionMatrix(),RenderSystem.getProjectionMatrix(),RenderSystem.getModelViewMatrix());}
        }
        text(fit(title,w-104,22),x+100,y+8,22,0xFFFFFFFF,600);
        text(fit(artist,w-104,15),x+100,y+40,15,0xDDFFFFFF,400);
        text(fit(MusicRuntime.status(),w,13),x,y+97,13,0xDDFFFFFF,400);
        long duration=MusicRuntime.durationMs();
        slider(x,y+123,w,"Позиция · "+time(MusicRuntime.positionMs())+" / "+time(duration),MusicRuntime.positionMs(),0,Math.max(1,duration),v->MusicRuntime.seek((long)v),"");
        action(x,y+170,64,"Назад",MusicRuntime::previous);
        action(x+76,y+170,100,MusicRuntime.playing()?"Пауза":"Играть",MusicRuntime::togglePlayPause);
        action(x+188,y+170,64,"Далее",MusicRuntime::next);
        slider(x,y+216,w,"Громкость",MusicRuntime.volume(),0,1,v->{MusicRuntime.volume(v);QuickMenuConfig.get().musicVolume=v;},"");
        action(x,y+264,w,searching?"Поиск: "+searchText:searchText.isBlank()?"Поиск в каталоге Aura":searchText,()->searching=true);
        action(x,y+305,100,"Локальные",()->{musicTab=0;MusicRuntime.refreshLocal();});
        action(x+110,y+305,100,"Каталог",()->{musicTab=1;MusicRuntime.searchAsync(searchText);});
        action(x+220,y+305,86,"Очередь",()->musicTab=2);
        var tracks=musicTab==0?MusicRuntime.tracks():musicTab==1?MusicRuntime.results():MusicRuntime.queue();
        float ty=y+350;
        if(tracks.isEmpty())text(musicTab==0?"Добавьте WAV, AIFF, AU или OGG в aura-glass/music.":"Нет треков. Измените запрос.",x,ty,14,0xDDFFFFFF,400);
        for(int i=0;i<Math.min(12,tracks.size());i++){
            var track=tracks.get(i);final int index=i;float rowY=ty+i*39;
            rect(x,rowY,w,35,8,0x3D767680,0x1AFFFFFF);
            text(fit(track.title(),w-20,15),x+10,rowY+3,15,0xFFFFFFFF,500);
            text(fit(track.author(),w-20,12),x+10,rowY+21,12,0xCCFFFFFF,400);
            controls.add(new Control(x,rowY,w,35,track.title(),()->{MusicRuntime.setQueue(tracks);MusicRuntime.playTrack(tracks.get(index));},null));
        }
        ty+=Math.max(1,Math.min(12,tracks.size()))*39+20;
        var cfg=QuickMenuConfig.get();
        toggle(x,ty,w,"Караоке в мире",()->cfg.karaoke,v->cfg.karaoke=v);
        slider(x,ty+=40,w,"Размер слов",cfg.karaokeSize,.5,2,v->cfg.karaokeSize=v,"");
        slider(x,ty+=40,w,"Дистанция",cfg.karaokeDistance,3,7,v->cfg.karaokeDistance=v," м");
        choice(x,ty+=40,w,"Плотность",new String[]{"Меньше","Нормально","Больше"},cfg.karaokeDensity,v->cfg.karaokeDensity=v);
        toggle(x,ty+=40,w,"Физика слов",()->cfg.karaokePhysics,v->cfg.karaokePhysics=v);
        slider(x,ty+=40,w,"Сдвиг текста",cfg.lyricsOffsetMs,-500,500,v->cfg.lyricsOffsetMs=(int)Math.round(v)," мс");
        text(fit(net.auramc.glass.client.quickmenu.lyrics.KaraokeRuntime.status(),w,13),x,ty+48,13,0xCCFFFFFF,400);
    }
    private void action(float x,float y,float w,String label,Runnable action){
        rect(x,y,w,30,8,0x5C767680,0x1AFFFFFF);text(fit(label,w-20,14),x+10,y+7,14,0xFFFFFFFF,500);
        controls.add(new Control(x,y,w,30,label,action,null));
    }
    private static String time(long ms){long s=Math.max(0,ms)/1000;return String.format(Locale.ROOT,"%d:%02d",s/60,s%60);}
    private static String fit(String text,float width,float size){
        if(AuraRenderer.textWidth(text,size)<=width)return text;
        int end=text.length();while(end>0&&AuraRenderer.textWidth(text.substring(0,end)+"…",size)>width)end--;
        return text.substring(0,end)+"…";
    }
    private void toggle(float x,float y,float w,String label,BooleanSupplier get,Consumer<Boolean> set) {
        row(x,y,w,label);boolean on=get.getAsBoolean();
        rect(x+w-44,y+4,42,23,11.5f,on?ACCENT:0x80767680,0x1AFFFFFF);
        rect(x+w-(on?22:40),y+7,17,17,8.5f,on?ACCENT_TEXT:0xFFFFFFFF,0);
        controls.add(new Control(x,y,w,32,label,()->{set.accept(!get.getAsBoolean());QuickMenuConfig.save();},null));
    }
    private void choice(float x,float y,float w,String label,String[] values,int selected,IntConsumer set) {
        row(x,y,w,label);String value=values[Math.floorMod(selected,values.length)];float tw=AuraRenderer.textWidth(value,14);
        rect(x+w-tw-21,y+2,tw+20,28,8,0x5C767680,0x1AFFFFFF);text(value,x+w-tw-11,y+8,14,0xFFFFFFFF,500);
        controls.add(new Control(x,y,w,32,label,()->{set.accept((selected+1)%values.length);QuickMenuConfig.save();},null));
    }
    private void slider(float x,float y,float w,String label,double value,double min,double max,DoubleConsumer set,String suffix) {
        text(label,x,y+1,17,0xFFFFFFFF,400);String display=String.format(Locale.ROOT,"%.1f%s",value,suffix);
        text(display,x+w-AuraRenderer.textWidth(display,13),y+2,13,0xDDFFFFFF,400);
        float trackW=w-8, t=(float)((value-min)/(max-min));rect(x+4,y+26,trackW,3,1.5f,0x80767680,0);
        rect(x+4,y+26,trackW*t,3,1.5f,ACCENT,0);rect(x+4+trackW*t-8,y+20,16,15,7.5f,0xFFFFFFFF,0x1AFFFFFF);
        DoubleConsumer change=normalized->{set.accept(min+normalized*(max-min));QuickMenuConfig.save();};
        controls.add(new Control(x,y,w,36,label,()->change.accept(Math.min(1,(value-min)/(max-min)+.05)),change,false,(value-min)/(max-min)));
    }
    private void row(float x,float y,float w,String label) { text(label,x,y+7,17,0xFFFFFFFF,400);rect(x,y+35,w,1,0,0x16767680,0); }
    private static int colorIndex(int rgb){for(int i=0;i<COLORS.length;i++)if(COLORS[i]==rgb)return i;return 0;}
    private void text(String text,float x,float y,float size,int color,int weight){AuraRenderer.text(text,x,y,size,color(color),weight);}
    private int color(int color){return (Math.round((color>>>24)*textOpacity)<<24)|(color&0xFFFFFF);}
    private void rect(float x,float y,float w,float h,float radius,int fill,int edge){AuraRenderer.rect(x,y,w,h,radius,edge==0?0:1,color(fill),color(edge));}
    private void icon(int type,float x,float y,int color){
        switch(type){
            case 0->{rect(x,y+2,12,7,3.5f,0,color);rect(x+5,y+4,3,3,1.5f,color,0);}
            case 1->{rect(x+1,y+1,10,10,5,0,color);rect(x+5,y+1,2,10,0,color,0);}
            case 2->{rect(x,y+2,13,9,2,0,color);rect(x+4,y,5,3,1, color,0);rect(x+5,y+4,4,4,2,0,color);}
            case 3->{rect(x+8,y,2,9,0,color,0);rect(x+3,y+7,6,4,2,color,0);rect(x+8,y,5,2,0,color,0);}
            case 4->{rect(x+2,y,5,5,2.5f,color,0);rect(x,y+6,9,5,2,color,0);rect(x+9,y+2,4,4,2,color,0);}
            default->{rect(x,y,4,12,1,0,color);rect(x+4,y+2,4,12,1,0,color);rect(x+8,y,4,12,1,0,color);}
        }
    }
    @Override public boolean mouseClicked(double x,double y,int button) {
        if(QuickMenuRuntime.menuKey().matchesMouse(button)){while(QuickMenuRuntime.menuKey().wasPressed()){} toggle();return true;}
        if(button!=0||geometry().contentOpacity()<.8f)return false;
        var box=geometry();
        if(x>=box.x()&&x<=box.x()+box.width()&&y>=box.y()&&y<box.y()+44){
            long now=System.nanoTime();
            if(now-lastHeaderClick<250_000_000L){motion.resetPosition();QuickMenuConfig.get().centerX=.5;QuickMenuConfig.get().bottomY=-1;QuickMenuConfig.save();lastHeaderClick=0;return true;}
            lastHeaderClick=now;dragging=true;dragX=x-box.centerX();dragY=y-box.bottom();return true;
        }
        for(int i=0;i<controls.size();i++){var c=controls.get(i);if(c.contains(x,y)&&y>box.y()+(c.navigation?44:112)&&y<box.bottom()-36){focused=i;pressed=c;if(c.slide!=null)c.slide.accept(Math.max(0,Math.min(1,(x-c.x)/c.w)));else c.action.run();return true;}}
        return false;
    }
    @Override public boolean mouseDragged(double x,double y,int button,double dx,double dy){
        if(button!=0)return false;
        if(dragging){motion.setPosition((x-dragX)/width,(y-dragY)/height);return true;}
        if(pressed!=null&&pressed.slide!=null){pressed.slide.accept(Math.max(0,Math.min(1,(x-pressed.x)/pressed.w)));return true;}
        return false;
    }
    @Override public boolean mouseReleased(double x,double y,int button){
        if(dragging){dragging=false;var c=QuickMenuConfig.get();c.centerX=motion.centerX();c.bottomY=motion.bottomY();QuickMenuConfig.save();}
        pressed=null;return true;
    }
    public boolean mouseScrolled(double x,double y,double amount){return scroll(amount);}
    public boolean mouseScrolled(double x,double y,double horizontal,double vertical){return scroll(vertical);}
    private boolean scroll(double amount){int max=section==0?Math.max(0,29*38-(int)(geometry().height()-164)):section==2?1400:0;scroll=Math.max(0,Math.min(max,scroll-(int)(amount*28)));return true;}
    @Override public boolean charTyped(char character,int modifiers) {
        if(searching&&!Character.isISOControl(character)&&searchText.length()<120){searchText+=character;return true;}
        return super.charTyped(character,modifiers);
    }
    @Override public boolean keyReleased(int key,int scan,int modifiers) {
        if(QuickMenuRuntime.menuKey().matchesKey(key,scan)){menuKeyHeld=false;return true;}
        return super.keyReleased(key,scan,modifiers);
    }
    private void revealFocused() {
        if(controls.isEmpty())return;
        var control=controls.get(Math.min(focused,controls.size()-1));
        if(control.navigation)return;
        float top=geometry().y()+112,bottom=geometry().bottom()-39;
        if(control.y<top)scroll=Math.max(0,scroll-(int)Math.ceil(top-control.y));
        else if(control.y+control.h>bottom)scroll+=(int)Math.ceil(control.y+control.h-bottom);
    }
    @Override public boolean keyPressed(int key,int scan,int modifiers){
        if(searching){
            if(key==GLFW.GLFW_KEY_ESCAPE){searching=false;return true;}
            if(key==GLFW.GLFW_KEY_BACKSPACE){if(!searchText.isEmpty())searchText=searchText.substring(0,searchText.length()-1);return true;}
            if(key==GLFW.GLFW_KEY_ENTER){searching=false;musicTab=1;MusicRuntime.searchAsync(searchText);return true;}
        }
        if(key==GLFW.GLFW_KEY_ESCAPE){close();return true;}
        if(QuickMenuRuntime.menuKey().matchesKey(key,scan)){while(QuickMenuRuntime.menuKey().wasPressed()){} if(!menuKeyHeld){menuKeyHeld=true;toggle();}return true;}
        if(key==GLFW.GLFW_KEY_TAB){focused=Math.floorMod(focused+((modifiers&GLFW.GLFW_MOD_SHIFT)!=0?-1:1),Math.max(1,controls.size()));revealFocused();return true;}
        if(!controls.isEmpty()&&(key==GLFW.GLFW_KEY_LEFT||key==GLFW.GLFW_KEY_RIGHT)){var control=controls.get(Math.min(focused,controls.size()-1));if(control.slide!=null){control.slide.accept(Math.max(0,Math.min(1,control.sliderValue+(key==GLFW.GLFW_KEY_RIGHT?.05:-.05))));return true;}}
        if(!controls.isEmpty()&&(key==GLFW.GLFW_KEY_ENTER||key==GLFW.GLFW_KEY_SPACE)){controls.get(Math.min(focused,controls.size()-1)).action.run();return true;}
        return super.keyPressed(key,scan,modifiers);
    }
}