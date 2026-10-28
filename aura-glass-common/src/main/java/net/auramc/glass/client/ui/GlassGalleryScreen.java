package net.auramc.glass.client.ui;

import com.mojang.blaze3d.systems.RenderSystem;
import net.auramc.glass.common.render.*;
import net.auramc.glass.common.style.*;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.client.gui.widget.SliderWidget;
import net.minecraft.client.texture.NativeImage;
import net.minecraft.client.util.ScreenshotRecorder;
import net.minecraft.text.Text;
import net.minecraft.util.Identifier;
import org.lwjgl.glfw.GLFW;
import java.lang.reflect.Field;
import java.nio.file.*;
import java.time.LocalDateTime;
import java.time.format.DateTimeFormatter;
import java.util.*;

public abstract class GlassGalleryScreen extends Screen implements GlassGalleryView {
    private static final GlassState[] STATES={GlassState.NORMAL,GlassState.HOVER,GlassState.PRESS,GlassState.DISABLED,GlassState.FOCUSED};
    private static final String[] STATE_NAMES={"normal","hover","pressed","disabled","focused"};
    private static final String[] BACKGROUNDS={"panorama","bright","dark","stripes","world"};
    private static final Field[] PARAMETERS=GlassStyle.class.getFields();
    private final Screen parent;
    private final boolean fromWorld;
    private final Object[][] keys=new Object[GlassPreset.values().length][5], comparisonKeys=new Object[2][5];
    private final Object[] exampleKeys={new Object(),new Object(),new Object(),new Object(),new Object(),new Object(),new Object(),new Object(),new Object()};
    private final GlassSpring[] hover={new GlassSpring(),new GlassSpring(),new GlassSpring()},press={new GlassSpring(),new GlassSpring(),new GlassSpring()};
    private Map<GlassPreset,GlassStyle> fileStyles=new EnumMap<>(GlassPreset.class),edits=new EnumMap<>(GlassPreset.class);
    private GlassPreset selected=GlassPreset.PANEL;
    private int background,page,panelX,visibleParameters,pressed=-1,selectedRow=1;
    private boolean comparing,pendingScreenshot,draggingSlider,fieldFocused;
    private float exampleValue=.5f;
    private String fieldText="Aura",status="";
    private int buttonsY,sliderY,listY,previewScroll;
    private final int[] buttonX=new int[3],buttonY=new int[3];

    protected GlassGalleryScreen(Screen parent) {
        super(Text.literal("Aura Glass Gallery"));this.parent=parent;fromWorld=MinecraftClient.getInstance().world!=null;
        for(int i=0;i<GlassPreset.values().length;i++)for(int j=0;j<5;j++)keys[i][j]=new Object();
        for(int i=0;i<2;i++)for(int j=0;j<5;j++)comparisonKeys[i][j]=new Object();
        resetToFile();
    }
    protected abstract void panorama(DrawContext context,float delta);

    @Override protected void init() { rebuildControls(); }
    private void rebuildControls() {
        clearChildren();panelX=width-Math.min(236,Math.max(190,width/3));visibleParameters=Math.max(1,(height-144)/23);
        page=Math.max(0,Math.min(page,Math.max(0,PARAMETERS.length-visibleParameters)));
        addDrawableChild(ButtonWidget.builder(Text.literal(selected.name()),b->{selected=GlassPreset.values()[(selected.ordinal()+1)%GlassPreset.values().length];rebuildControls();}).dimensions(panelX+10,40,width-panelX-20,20).build());
        for(int i=0;i<visibleParameters && page+i<PARAMETERS.length;i++)addDrawableChild(new ParameterSlider(PARAMETERS[page+i],panelX+10,72+i*23,width-panelX-20));
        addDrawableChild(ButtonWidget.builder(Text.translatable("aura.hud.title"),b->client.setScreen(new net.auramc.glass.common.hud.HudSettingsScreen(this))).dimensions(panelX+10,height-30,width-panelX-20,20).build());
        int half=(width-panelX-24)/2;
        addDrawableChild(ButtonWidget.builder(Text.literal("Save"),b->save()).dimensions(panelX+10,height-55,half,20).build());
        addDrawableChild(ButtonWidget.builder(Text.literal("Reset to file"),b->{resetToFile();rebuildControls();}).dimensions(panelX+14+half,height-55,half,20).build());
    }
    private void resetToFile() {
        try {fileStyles=GlassStyles.filePresets();edits=new EnumMap<>(GlassPreset.class);for(var e:fileStyles.entrySet())edits.put(e.getKey(),e.getValue().copy());status="Loaded styles.json";}
        catch(Exception error){for(GlassPreset p:GlassPreset.values()){fileStyles.put(p,GlassStyles.get(p));edits.put(p,GlassStyles.get(p));}status="Reset failed: "+error.getMessage();}
    }
    private void save() {
        for(var e:edits.entrySet())GlassStyles.update(e.getKey(),e.getValue());
        if(GlassStyles.save()){resetToFile();status="Saved styles.json";}else status="Save failed; see log";
    }
    public boolean scrollParameters(double delta) {
        int next=Math.max(0,Math.min(Math.max(0,PARAMETERS.length-visibleParameters),page+(delta>0?-1:1)*(int)Math.max(1,Math.abs(delta))));
        if(next!=page){page=next;rebuildControls();}return true;
    }
    public boolean scrollAt(double mouseX,double delta) {
        if(mouseX>=panelX)return scrollParameters(delta);
        previewScroll=Math.max(0,Math.min(Math.max(0,310+40*GlassPreset.values().length-height),previewScroll+(delta>0?-24:24)));return true;
    }
    public String backgroundName(){return BACKGROUNDS[background];}

    @Override public void render(DrawContext c,int mouseX,int mouseY,float delta) {
        background(c,delta);c.draw();GlassRenderer.captureBackdrop();
        GlassStyle panel=edits.get(GlassPreset.PANEL).copy();panel.refraction=0;
        GlassRenderer.draw(panelX,10,width-panelX-6,height-20,panel,1);
        if(comparing)comparison(c);else matrix(c);
        if(!comparing)examples(c,mouseX,mouseY);
        GlassRenderer.foreground(()->{
            c.drawTextWithShadow(textRenderer,"Glass Gallery",12,12,0xFFFFFFFF);
            c.drawTextWithShadow(textRenderer,"B: "+backgroundName()+"   C: "+(comparing?"file / edited":"compare")+"   P: screenshot   F8: close",12,28,0xFFE8E8E8);
            c.drawTextWithShadow(textRenderer,"Preset / parameters",panelX+10,20,0xFFFFFFFF);
            c.drawTextWithShadow(textRenderer,(page+1)+"-"+Math.min(page+visibleParameters,PARAMETERS.length)+" / "+PARAMETERS.length+"   Wheel / PgUp / PgDn",panelX+10,height-71,0xFFFFFFFF);
            c.drawTextWithShadow(textRenderer,status,12,height-16,0xFFFFFFFF);
            super.render(c,mouseX,mouseY,delta);
        });
    }
    private void background(DrawContext c,float delta) {
        switch(background){
            case 0->panorama(c,delta);
            case 1->c.drawTexture(Identifier.tryParse("aura-glass:textures/gallery/bright.png"),0,0,0,0,width,height,width,height);
            case 2->c.fill(0,0,width,height,0xFF050609);
            case 3->{
                c.fill(0,0,width,height,0xFF111111);c.getMatrices().push();c.getMatrices().translate(width/2f,height/2f,0);
                c.getMatrices().multiply(new org.joml.Quaternionf().rotationZ((float)Math.PI/4));
                int span=width+height;for(int x=-span;x<span;x+=32)c.fill(x,-span,x+16,span,0xFFF1F1F1);c.getMatrices().pop();
            }
            case 4->{c.draw();BackdropRenderer.worldImage();}
        }
    }
    private void matrix(DrawContext c) {
        float cell=(panelX-116)/5f;
        for(int j=0;j<5;j++){final int index=j;GlassRenderer.foreground(()->c.drawTextWithShadow(textRenderer,STATE_NAMES[index],98+(int)(cell*index),52,0xFFFFFFFF));}
        for(GlassPreset preset:GlassPreset.values()) {
            int row=preset.ordinal(),y=70+row*40-previewScroll;
            if(visible(y,28))GlassRenderer.foreground(()->c.drawTextWithShadow(textRenderer,preset.name(),12,y+9,0xFFFFFFFF));
            for(int state=0;state<5;state++) {
                GlassStyle style=GlassStyles.resolve(edits.get(preset),STATES[state],1);
                surfaceLabel(c,keys[row][state],98+state*cell,y,cell-8,28,style,"Aura",true);
            }
        }
    }
    private void comparison(DrawContext c) {
        float middle=width*.5f;
        float sampleWidth=Math.max(48,Math.min(middle-24,panelX-middle-20));
        float left=(middle-sampleWidth)*.5f,right=middle+8;
        GlassRenderer.foreground(()->{
            c.drawTextWithShadow(textRenderer,"FILE: "+selected.name(),(int)left,52,0xFFFFFFFF);
            c.drawTextWithShadow(textRenderer,"EDITED: "+selected.name(),(int)right,52,0xFFFFFFFF);
        });
        for(int side=0;side<2;side++)for(int state=0;state<5;state++) {
            GlassStyle style=GlassStyles.resolve((side==0?fileStyles:edits).get(selected),STATES[state],1);
            surfaceLabel(c,comparisonKeys[side][state],side==0?left:right,70+state*29-previewScroll,sampleWidth,23,style,STATE_NAMES[state],true);
        }
        float y=238-previewScroll,h=Math.min(180,height-270);
        if(h>30)for(int side=0;side<2;side++)surfaceLabel(c,keys[selected.ordinal()][side],side==0?left:right,y,sampleWidth,h,
                (side==0?fileStyles:edits).get(selected),side==0?"styles.json":"Current sliders",true);
    }
    private void examples(DrawContext c,int mx,int my) {
        buttonsY=90+40*GlassPreset.values().length-previewScroll;
        int cursor=12,rowY=buttonsY;
        if(visible(72+40*GlassPreset.values().length-previewScroll,12))GlassRenderer.foreground(()->c.drawTextWithShadow(textRenderer,"Interactive samples",12,72+40*GlassPreset.values().length-previewScroll,0xFFFFFFFF));
        for(int i=0;i<3;i++) {
            int w=i==2?20:200;
            if(cursor+w>panelX-12 && cursor>12){cursor=12;rowY+=26;}
            int x=cursor;buttonX[i]=x;buttonY[i]=rowY;cursor+=w+12;
            int y=rowY;
            boolean over=inside(mx,my,x,y,w,20);
            float h=hover[i].approach(over?1:0,System.nanoTime()),p=press[i].approach(pressed==i?1:0,System.nanoTime());
            GlassStyle style=GlassStyles.resolve(edits.get(i==0?GlassPreset.BUTTON_FILLED:GlassPreset.PILL_THICK),p>.01?GlassState.PRESS:GlassState.HOVER,p>.01?p:h);
            surfaceLabel(c,exampleKeys[i],x,y,w,20,style,i==0?"Continue":i==1?"Secondary":"+",true);
        }
        sliderY=rowY+42;listY=sliderY+42;
        GlassStyle track=edits.get(GlassPreset.PANEL).copy();track.radius=10;
        if(visible(sliderY,20))GlassRenderer.draw(12,sliderY,200,20,track,1);
        if(visible(sliderY-14,12))adaptiveLabel(c,exampleKeys[3],12,sliderY-14,200,12,track,"Value "+Math.round(exampleValue*100)+"%",false);
        GlassStyle knob=edits.get(GlassPreset.BUTTON_FILLED).copy();knob.radius=6;
        if(visible(sliderY,20))GlassRenderer.draw(12+exampleValue*188,sliderY+1,12,18,knob,1);
        GlassStyle field=GlassStyles.resolve(edits.get(GlassPreset.PILL_THICK),fieldFocused?GlassState.FOCUSED:GlassState.NORMAL,1);
        surfaceLabel(c,exampleKeys[4],224,sliderY,Math.min(200,panelX-236),20,field,fieldText+(fieldFocused && System.currentTimeMillis()/500%2==0?"|":""),false);
        float listW=Math.min(444,panelX-24),listH=106;
        if(!visible(listY,listH))return;
        GlassRenderer.draw(12,listY,listW,listH,edits.get(GlassPreset.PANEL),1);
        for(int row=0;row<3 && row*28+30<listH;row++) {
            GlassStyle style=row==selectedRow?GlassStyles.selected():edits.get(GlassPreset.PANEL);
            if(row==selectedRow)GlassRenderer.draw(18,listY+6+row*28,listW-12,24,style,1);
            adaptiveLabel(c,exampleKeys[5+row],24,listY+11+row*28,listW-24,16,style,"World "+(row+1)+(row==selectedRow?" - selected":""),false);
        }
    }
    private boolean visible(float y,float h){return y>=46 && y+h<=height-24;}
    private static boolean inside(double x,double y,float bx,float by,float bw,float bh){return x>=bx && x<bx+bw && y>=by && y<by+bh;}
    private void surfaceLabel(DrawContext c,Object key,float x,float y,float w,float h,GlassStyle style,String text,boolean centered){
        if(!visible(y,h) || w<=0)return;
        GlassRenderer.draw(x,y,w,h,style,1);adaptiveLabel(c,key,x,y,w,h,style,text,centered);
    }
    private void adaptiveLabel(DrawContext c,Object key,float x,float y,float w,float h,GlassStyle style,String text,boolean centered){
        AdaptiveText.request(key,x+w*.5f,y+h*.5f,style);
        GlassRenderer.foreground(()->{
            c.draw();if(!AuraRenderer.isReady() || !AuraRenderer.supports(text)) {c.drawTextWithShadow(textRenderer,text,(int)x+4,(int)y+4,0xFFFFFFFF);return;}
            if(!AuraRenderer.begin(c.getMatrices().peek().getPositionMatrix(),RenderSystem.getProjectionMatrix(),RenderSystem.getModelViewMatrix()))return;
            float size=Math.min(11,h*.53f),tw=AuraRenderer.textWidth(text,size);
            if(tw>w-8){size*=Math.max(0,w-8)/Math.max(tw,1);tw=AuraRenderer.textWidth(text,size);}
            if(style.isSelectedText())GlassGlyphText.draw(c,textRenderer,text,centered?x+(w-tw)/2:x+6,y+(h-size)/2-1,size,0xFFFFFFFF,true,-2);
            else {AuraRenderer.adaptiveText(AdaptiveText.slot(key),text,centered?x+(w-tw)/2:x+6,y+(h-size)/2-1,size,0xFFFFFFFF);AuraRenderer.flush();}
        });
    }
    @Override public boolean mouseClicked(double x,double y,int button) {
        if(x>=panelX)fieldFocused=false;
        if(comparing && x<panelX){fieldFocused=false;return true;}
        if(button==0 && x<panelX) {
            fieldFocused=inside(x,y,224,sliderY,Math.min(200,panelX-236),20);
            if(inside(x,y,12,sliderY,200,20)){draggingSlider=true;exampleValue=(float)Math.max(0,Math.min(1,(x-12)/200));return true;}
            for(int i=0;i<3;i++){int bx=buttonX[i],bw=i==2?20:200;if(visible(buttonY[i],20) && inside(x,y,bx,buttonY[i],bw,20)){pressed=i;return true;}}
            if(visible(listY,106) && inside(x,y,12,listY,Math.min(444,panelX-24),106)){selectedRow=Math.max(0,Math.min(2,((int)y-listY-6)/28));return true;}
            if(y>=46 && y<height-24 && y+previewScroll>=70 && y+previewScroll<70+40*GlassPreset.values().length && !comparing){selected=GlassPreset.values()[Math.min(GlassPreset.values().length-1,((int)y+previewScroll-70)/40)];rebuildControls();return true;}
            if(fieldFocused)return true;
        }
        return super.mouseClicked(x,y,button);
    }
    @Override public boolean mouseDragged(double x,double y,int button,double dx,double dy){if(draggingSlider){exampleValue=(float)Math.max(0,Math.min(1,(x-12)/200));return true;}return super.mouseDragged(x,y,button,dx,dy);}
    @Override public boolean mouseReleased(double x,double y,int button){draggingSlider=false;if(pressed>=0){status="Pressed "+(pressed==0?"Continue":pressed==1?"Secondary":"icon");pressed=-1;return true;}return super.mouseReleased(x,y,button);}
    @Override public boolean charTyped(char character,int modifiers){if(fieldFocused && !Character.isISOControl(character) && fieldText.length()<32){fieldText+=character;return true;}return super.charTyped(character,modifiers);}
    @Override public boolean keyPressed(int key,int scanCode,int modifiers) {
        if(fieldFocused){
            if(key==GLFW.GLFW_KEY_ESCAPE){fieldFocused=false;return true;}
            if(key==GLFW.GLFW_KEY_BACKSPACE && !fieldText.isEmpty()){fieldText=fieldText.substring(0,fieldText.offsetByCodePoints(fieldText.length(),-1));return true;}
            if(key==GLFW.GLFW_KEY_V && Screen.hasControlDown()){fieldText=(fieldText+client.keyboard.getClipboard()).replaceAll("[\\p{Cntrl}]","");if(fieldText.length()>32)fieldText=fieldText.substring(0,32);return true;}
            return true;
        }
        if(key==GLFW.GLFW_KEY_B){background=(background+1)%(fromWorld?5:4);return true;}
        if(key==GLFW.GLFW_KEY_C){
            if(!comparing)try{fileStyles=GlassStyles.filePresets();}catch(Exception error){status="File baseline unavailable: "+error.getMessage();return true;}
            comparing=!comparing;return true;
        }
        if(key==GLFW.GLFW_KEY_P){pendingScreenshot=true;return true;}
        if(key==GLFW.GLFW_KEY_PAGE_DOWN)return scrollParameters(-visibleParameters);
        if(key==GLFW.GLFW_KEY_PAGE_UP)return scrollParameters(visibleParameters);
        return super.keyPressed(key,scanCode,modifiers);
    }
    @Override public void close(){client.setScreen(parent);}
    @Override public void removed(){BackdropRenderer.clearWorldImage();}
    public void afterFrame() {
        if(!pendingScreenshot)return;pendingScreenshot=false;
        try {
            Path root=Path.of(System.getProperty("aura.glass.reportRoot",client.runDirectory.toPath().toString())).toAbsolutePath();
            if(System.getProperty("aura.glass.reportRoot")==null)for(Path p=root;p!=null;p=p.getParent())if(Files.isDirectory(p.resolve("aura-glass-common"))){root=p;break;}
            Path directory=root.resolve("docs/redesign/reports/glass-gallery");Files.createDirectories(directory);
            String name=selected.name()+"-"+LocalDateTime.now().format(DateTimeFormatter.ofPattern("yyyyMMdd-HHmmss-SSS"))+"-"+backgroundName()+".png";
            try(NativeImage image=ScreenshotRecorder.takeScreenshot(client.getFramebuffer())){image.writeTo(directory.resolve(name));}
            status="Saved "+name;
        }catch(Exception error){status="Screenshot failed: "+error.getMessage();GlassDiagnostics.log(status);}
    }
    private final class ParameterSlider extends SliderWidget {
        private final Field field;private final double low,high;
        ParameterSlider(Field field,int x,int y,int w){super(x,y,w,20,Text.empty(),0);this.field=field;String n=field.getName();
            low=n.equals("bevel") || n.equals("radius") || n.startsWith("lightDir")?-1:n.equals("squircleK")?2:n.equals("scale")?.5:n.equals("profile")?1.5:n.equals("specFalloff")?1:0;
            high=switch(n){case "profile"->4;case "specFalloff"->6;case "frost"->3;case "squircleK","splay","depth","light","rimOuter","gloss","saturation","wobble","gradient","innerGlow"->8;case "scale"->2;case "tintR","tintG","tintB","tintA","dispersion","noise","lightDirX","lightDirY","cornerSoften"->1;default->100;};
            try{value=(field.getFloat(edits.get(selected))-low)/(high-low);}catch(Exception impossible){throw new IllegalStateException(impossible);}updateMessage();}
        @Override protected void updateMessage(){if(field!=null)setMessage(Text.literal(field.getName()+": "+String.format(Locale.ROOT,"%.3f",low+value*(high-low))));}
        @Override protected void applyValue(){try{field.setFloat(edits.get(selected),(float)(low+value*(high-low)));edits.get(selected).validate();}catch(Exception impossible){throw new IllegalStateException(impossible);}}
    }
}
