package net.auramc.glass.client.ui;
import com.mojang.blaze3d.systems.RenderSystem;
import net.auramc.glass.common.render.AuraRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.font.TextRenderer;
public final class GlassGlyphText {
    private static int vanillaDepth;
    private GlassGlyphText(){}
    public static boolean vanillaGlyph(){return vanillaDepth>0;}
    public static float measure(TextRenderer font,String text,float size){
        float width=0;
        for(int offset=0;offset<text.length();){int cp=text.codePointAt(offset);width+=AuraRenderer.hasGlyph(cp)?AuraRenderer.glyphWidth(cp,size):font.getWidth(new String(Character.toChars(cp)))*size/9;offset+=Character.charCount(cp);}
        return width;
    }
    private static boolean begin(DrawContext c){c.draw();return AuraRenderer.begin(c.getMatrices().peek().getPositionMatrix(),RenderSystem.getProjectionMatrix(),RenderSystem.getModelViewMatrix());}
    private static void run(String text,float x,float y,float size,int color,boolean shadow,int slot){
        if(slot==-2){
            int shade=Math.min(230,color>>>24)<<24;
            for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)if(dx!=0 || dy!=0)AuraRenderer.text(text,x+dx*.75f,y+dy*.75f,size,shade);
            AuraRenderer.text(text,x,y,size,(color&0xFF000000)|0xFFFFFF);
        }
        else if(slot>=0)AuraRenderer.adaptiveText(slot,text,x,y,size,color);
        else {if(shadow)AuraRenderer.text(text,x+.5f,y+.5f,size,((color>>>24)/3)<<24);AuraRenderer.text(text,x,y,size,color);}
    }
    public static void draw(DrawContext c,TextRenderer font,String text,float x,float y,float size,int color,boolean shadow,int slot){
        if(!begin(c))return;
        float pen=x;int runStart=0;
        for(int offset=0;offset<text.length();){int cp=text.codePointAt(offset),next=offset+Character.charCount(cp);
            if(!AuraRenderer.hasGlyph(cp)){
                if(offset>runStart){String known=text.substring(runStart,offset);run(known,pen,y,size,color,shadow,slot);pen+=AuraRenderer.textWidth(known,size);}
                AuraRenderer.flush();String glyph=text.substring(offset,next);
                c.getMatrices().push();c.getMatrices().translate(pen,y,0);c.getMatrices().scale(size/9,size/9,1);
                vanillaDepth++;
                try{c.drawText(font,glyph,0,0,color,shadow);c.draw();}finally{vanillaDepth--;c.getMatrices().pop();}
                pen+=font.getWidth(glyph)*size/9;runStart=next;
                if(!begin(c))return;
            }
            offset=next;
        }
        if(runStart<text.length())run(text.substring(runStart),pen,y,size,color,shadow,slot);
        AuraRenderer.flush();
    }
}
