package net.auramc.glass.client.ui;
import com.mojang.blaze3d.systems.RenderSystem;
import net.auramc.glass.common.render.*;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.text.OrderedText;
import org.joml.Matrix4f;
public final class GlassScreenText {
    private static boolean selectedText;
    private static final java.util.Map<OrderedText,String> PLAIN=new java.util.WeakHashMap<>();
    private static final Command[] COMMANDS=new Command[8192];
    private static int cursor;
    static{for(int i=0;i<COMMANDS.length;i++)COMMANDS[i]=new Command();}
    private GlassScreenText(){}
    public static void beginFrame(){cursor=0;}
    public static void selected(Runnable drawing){boolean saved=selectedText;selectedText=true;try{drawing.run();}finally{selectedText=saved;}}
    public static boolean beginSelected(boolean selected){boolean old=selectedText;selectedText=selected;return old;}
    public static void endSelected(boolean old){selectedText=old;}
    public static String plain(OrderedText ordered){
        if(!GlassFrameHooks.styledScreen())return null;
        String cached=PLAIN.get(ordered);if(cached!=null)return cached;
        var text=new StringBuilder();boolean[] custom={false};
        ordered.accept((index,style,point)->{if(!style.getFont().getPath().equals("default"))custom[0]=true;text.appendCodePoint(point);return true;});
        if(custom[0])return null;
        String result=text.toString();PLAIN.put(ordered,result);return result;
    }
    public static boolean draw(DrawContext c,TextRenderer nativeFont,String text,int x,int y,int color,boolean shadow){
        if(!GlassFrameHooks.styledScreen() || text==null || GlassGlyphText.vanillaGlyph() || !AuraRenderer.isReady())return false;
        if(cursor==COMMANDS.length)return false;
        Command command=COMMANDS[cursor++];command.c=c;command.nativeFont=nativeFont;command.text=text;
        command.x=x;command.y=y;command.color=color;command.shadow=shadow;command.slot=selectedText?-2:-1;
        command.width=nativeFont.getWidth(text);command.matrix.set(c.getMatrices().peek().getPositionMatrix());
        GlassRenderer.foreground(command);return true;
    }
    private static final class Command implements Runnable {
        DrawContext c;TextRenderer nativeFont;String text;int x,y,color,slot,width;boolean shadow;
        final Matrix4f matrix=new Matrix4f();
        @Override public void run(){
            c.getMatrices().push();c.getMatrices().peek().getPositionMatrix().set(matrix);
            float[] clip=GlassRenderer.clipBounds();
            if(clip!=null)c.enableScissor((int)clip[0],(int)clip[1],(int)clip[2],(int)clip[3]);
            try{
                c.draw();float size=9;float measured=GlassGlyphText.measure(nativeFont,text,size);
                if(measured>width && width>0)size*=width/measured;
                int alpha=(color>>>24)==0?255:color>>>24;int rgba=(color&0xFFFFFF)|(alpha<<24);
                GlassGlyphText.draw(c,nativeFont,text,x,y,size,slot==-2?0xFFFFFFFF:rgba,shadow,slot);
            }finally{if(clip!=null)c.disableScissor();c.getMatrices().pop();}
            text=null;c=null;nativeFont=null;
        }
    }
}
