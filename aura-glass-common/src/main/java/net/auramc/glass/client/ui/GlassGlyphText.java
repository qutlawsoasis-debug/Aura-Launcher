package net.auramc.glass.client.ui;
import net.auramc.glass.common.render.AuraRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.font.TextRenderer;
public final class GlassGlyphText {
    private static int vanillaDepth;
    private GlassGlyphText(){}
    public static boolean vanillaGlyph(){return vanillaDepth>0;}
    public static void beginVanillaGlyph(){vanillaDepth++;}
    public static void endVanillaGlyph(){vanillaDepth--;}
    public static float measure(TextRenderer font,String text,float size){
        float width=0;
        for(int offset=0;offset<text.length();){int point=text.codePointAt(offset);width+=AuraRenderer.hasGlyph(point)?AuraRenderer.glyphWidth(point,size):GlassScreenText.nativeWidth(font,point)*size/9;offset+=Character.charCount(point);}
        return width;
    }
    public static void draw(DrawContext c,TextRenderer font,String text,float x,float y,float size,int color,boolean shadow,int slot){
        GlassScreenText.drawImmediate(c,font,text,x,y,size,color,shadow,slot);
    }
}
