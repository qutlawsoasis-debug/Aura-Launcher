package net.auramc.glass.client.ui;
import com.mojang.blaze3d.systems.RenderSystem;
import net.auramc.glass.common.render.*;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.text.OrderedText;
import org.joml.Matrix4f;
public final class GlassScreenText {
    private static boolean selectedText;
    private GlassScreenText(){}
    public static void selected(Runnable drawing){boolean saved=selectedText;selectedText=true;try{drawing.run();}finally{selectedText=saved;}}
    public static String plain(OrderedText ordered){
        var text=new StringBuilder();boolean[] custom={false};
        ordered.accept((index,style,point)->{if(!style.getFont().getPath().equals("default"))custom[0]=true;text.appendCodePoint(point);return true;});
        return custom[0]?null:text.toString();
    }
    public static boolean draw(DrawContext c,TextRenderer nativeFont,String text,int x,int y,int color,boolean shadow){
        if(!GlassFrameHooks.styledScreen() || text==null || GlassGlyphText.vanillaGlyph() || !AuraRenderer.isReady())return false;
        int slot=selectedText?-2:-1;int width=nativeFont.getWidth(text);var matrix=new Matrix4f(c.getMatrices().peek().getPositionMatrix());
        GlassRenderer.foreground(()->{
            c.getMatrices().push();c.getMatrices().peek().getPositionMatrix().set(matrix);
            float[] clip=GlassRenderer.clipBounds();
            if(clip!=null)c.enableScissor((int)clip[0],(int)clip[1],(int)clip[2],(int)clip[3]);
            try{
                c.draw();float size=9;float measured=GlassGlyphText.measure(nativeFont,text,size);
                if(measured>width && width>0)size*=width/measured;
                int alpha=(color>>>24)==0?255:color>>>24;int rgba=(color&0xFFFFFF)|(alpha<<24);
                GlassGlyphText.draw(c,nativeFont,text,x,y,size,slot==-2?0xFFFFFFFF:rgba,shadow,slot);
            }finally{if(clip!=null)c.disableScissor();c.getMatrices().pop();}
        });return true;
    }
}
