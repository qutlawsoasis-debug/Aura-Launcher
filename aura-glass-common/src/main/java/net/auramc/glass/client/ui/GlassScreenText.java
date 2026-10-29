package net.auramc.glass.client.ui;

import com.mojang.blaze3d.systems.RenderSystem;
import net.auramc.glass.common.render.AuraRenderer;
import net.auramc.glass.common.render.GlassRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.text.CharacterVisitor;
import net.minecraft.text.OrderedText;
import net.minecraft.text.Style;
import org.joml.Matrix4f;

public final class GlassScreenText {
    private static boolean selectedText,entryText;
    public static boolean beginEntry(){boolean old=entryText;entryText=true;return old;}
    public static void endEntry(boolean old){entryText=old;}
    private static final Command[] COMMANDS=new Command[8192];
    private static final FontProbe FONT_PROBE=new FontProbe();
    private static final GlyphMask WIDTH_MASK=new GlyphMask();
    private static int cursor;
    static{for(int i=0;i<COMMANDS.length;i++)COMMANDS[i]=new Command();}
    private GlassScreenText(){}
    public static void beginFrame(){cursor=0;}
    public static boolean beginSelected(boolean selected){boolean old=selectedText;selectedText=selected;return old;}
    public static void endSelected(boolean old){selectedText=old;}
    public static int nativeWidth(TextRenderer font,int point){WIDTH_MASK.style=Style.EMPTY;WIDTH_MASK.point=point;return font.getWidth(WIDTH_MASK);}
    public static void drawImmediate(DrawContext c,TextRenderer font,String text,float x,float y,float size,int color,boolean shadow,int slot){
        if(cursor==COMMANDS.length)return;
        Command command=COMMANDS[cursor++];command.c=c;command.font=font;command.text=text;command.ordered=null;
        command.x=x;command.y=y;command.color=color;command.shadow=shadow;command.selected=slot==-2;command.slot=slot;
        command.baseSize=size;command.width=0;command.matrix.set(c.getMatrices().peek().getPositionMatrix());command.run();
    }

    public static boolean draw(DrawContext c,TextRenderer font,String text,int x,int y,int color,boolean shadow){
        if(text==null)return false;
        if(net.auramc.glass.common.style.GlassStyles.regular() && GlassGroups.insideAnyGroup(x,y)){
            shadow=false;
            GlassGroups.Group g=GlassGroups.groupAt(x,y);
            int modeColor=GlassGroups.textColorForGroup(g);
            if(GlassActiveField.isPlaceholder()){
                color=(Math.round(255*0.45f)<<24)|(modeColor&0xFFFFFF);
            }else if(GlassActiveField.isActive()&&"_".equals(text)){
                color=0xFF64D2FF;
            }else if((color&0xFFFFFF)!=0x00283A){
                int a=(color>>>24);
                if(a>0 && a<=128){
                    int modeAlpha=(modeColor>>>24);
                    color=(Math.round(modeAlpha*0.30f)<<24)|(modeColor&0xFFFFFF);
                }else{
                    color=modeColor;
                }
            }
        }
        Command command=command(c,font,x,y,color,shadow);if(command==null)return false;
        command.text=text;command.ordered=null;command.width=font.getWidth(text);GlassRenderer.foreground(command);return true;
    }
    public static boolean draw(DrawContext c,TextRenderer font,OrderedText text,int x,int y,int color,boolean shadow){
        if(!allowed()||text==null)return false;
        FONT_PROBE.custom=false;text.accept(FONT_PROBE);if(FONT_PROBE.custom)return false;
        if(net.auramc.glass.common.style.GlassStyles.regular() && GlassGroups.insideAnyGroup(x,y)){
            shadow=false;
            GlassGroups.Group g=GlassGroups.groupAt(x,y);
            int modeColor=GlassGroups.textColorForGroup(g);
            if(GlassActiveField.isPlaceholder()){
                color=(Math.round(255*0.45f)<<24)|(modeColor&0xFFFFFF);
            }else if((color&0xFFFFFF)!=0x00283A){
                int a=(color>>>24);
                if(a>0 && a<=128){
                    int modeAlpha=(modeColor>>>24);
                    color=(Math.round(modeAlpha*0.30f)<<24)|(modeColor&0xFFFFFF);
                }else{
                    color=modeColor;
                }
            }
        }
        Command command=command(c,font,x,y,color,shadow);if(command==null)return false;
        command.text=null;command.ordered=text;command.width=font.getWidth(text);GlassRenderer.foreground(command);return true;
    }
    private static int entryColor(int color){
        int r=(color>>16)&255,g=(color>>8)&255,b=color&255;
        return Math.max(r,Math.max(g,b))-Math.min(r,Math.min(g,b))<12 && r<220?0xB3FFFFFF:color;
    }
    private static boolean allowed(){return GlassFrameHooks.styledScreen()&&!GlassGlyphText.vanillaGlyph()&&AuraRenderer.isReady();}
    private static Command command(DrawContext c,TextRenderer font,int x,int y,int color,boolean shadow){
        if(!allowed()||cursor==COMMANDS.length)return null;
        int finalColor=(net.auramc.glass.common.style.GlassStyles.regular()&&GlassGroups.insideAnyGroup(x,y))?color:(entryText?entryColor(color):color);
        Command command=COMMANDS[cursor++];command.c=c;command.font=font;command.x=x;command.y=y;command.color=finalColor;command.shadow=shadow;command.selected=selectedText;command.slot=-1;command.baseSize=9;
        command.matrix.set(c.getMatrices().peek().getPositionMatrix());return command;
    }
    private static final class FontProbe implements CharacterVisitor {
        boolean custom;
        @Override public boolean accept(int index,Style style,int point){
            custom=!style.getFont().getNamespace().equals("minecraft")||!style.getFont().getPath().equals("default");return !custom;
        }
    }
    private static final class GlyphMask implements OrderedText {
        Style style;int point;
        @Override public boolean accept(CharacterVisitor visitor){return visitor.accept(0,style,point);}
    }
    private static final class Command implements Runnable,CharacterVisitor {
        DrawContext c;TextRenderer font;String text;OrderedText ordered;
        int color,width,slot;boolean shadow,selected,measuring;
        float x,y,size,pen,measured,baseSize;
        final Matrix4f matrix=new Matrix4f(),saved=new Matrix4f(),glyphMatrix=new Matrix4f();
        final GlyphMask mask=new GlyphMask();
        private void visit(){
            if(ordered!=null){ordered.accept(this);return;}
            net.minecraft.text.TextVisitFactory.visitFormatted(text,Style.EMPTY,this);
        }
        @Override public boolean accept(int index,Style style,int point){
            int weight=style.isBold()?700:400;
            mask.style=style;mask.point=point;
            boolean nativeGlyph=!AuraRenderer.hasGlyph(point);
            float advance=nativeGlyph?font.getWidth(mask)*size/9:AuraRenderer.glyphWidth(point,size,weight);
            if(measuring){measured+=advance;return true;}
            int alpha=(color>>>24)==0?255:color>>>24;
            int rgba=(alpha<<24)|(selected?0xFFFFFF:style.getColor()==null?color&0xFFFFFF:style.getColor().getRgb());
            if(nativeGlyph){
                AuraRenderer.flush();var position=c.getMatrices().peek().getPositionMatrix();glyphMatrix.set(position);
                position.translate(pen,y,0).scale(size/9,size/9,1);GlassGlyphText.beginVanillaGlyph();
                try{c.drawText(font,mask,0,0,rgba,shadow);c.draw();}
                finally{GlassGlyphText.endVanillaGlyph();position.set(glyphMatrix);}
                AuraRenderer.begin(position,RenderSystem.getProjectionMatrix(),RenderSystem.getModelViewMatrix());
            }else{
                if(shadow && slot<0)AuraRenderer.glyph(point,pen+.5f,y+.5f,size,(alpha/3)<<24,weight);
                if(slot>=0)AuraRenderer.adaptiveGlyph(slot,point,pen,y,size,rgba,weight);else AuraRenderer.glyph(point,pen,y,size,rgba,weight);
            }
            if(style.isUnderlined())AuraRenderer.rect(pen,y+size,advance,Math.max(.5f,size/9),0,0,rgba,0);
            pen+=advance;return true;
        }
        @Override public void run(){
            var position=c.getMatrices().peek().getPositionMatrix();saved.set(position);position.set(matrix);
            float[] clip=GlassRenderer.clipBounds();if(clip!=null)c.enableScissor((int)clip[0],(int)clip[1],(int)clip[2],(int)clip[3]);
            try{
                size=baseSize;
                if(width>0){measured=0;measuring=true;visit();if(measured>width)size*=width/measured;}
                measuring=false;
                c.draw();if(!AuraRenderer.begin(position,RenderSystem.getProjectionMatrix(),RenderSystem.getModelViewMatrix()))return;
                pen=x;visit();AuraRenderer.flush();
            }finally{if(clip!=null)c.disableScissor();position.set(saved);text=null;ordered=null;c=null;font=null;}
        }
    }
}
