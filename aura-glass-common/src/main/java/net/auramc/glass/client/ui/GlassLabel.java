package net.auramc.glass.client.ui;

import net.auramc.glass.common.render.AuraRenderer;
import net.auramc.glass.common.ui.GlassTextFit;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.ClickableWidget;
import net.minecraft.text.Text;
import org.joml.Matrix4f;

public final class GlassLabel implements Runnable {
    private final Matrix4f matrix=new Matrix4f(),saved=new Matrix4f();
    private Text message;
    private Object language;
    private int width,height;
    private String plain;
    private GlassTextFit.Fitted fitted;
    private ClickableWidget widget;
    private DrawContext context;
    private int color;

    public void update(ClickableWidget widget){
        if(!AuraRenderer.isReady())return;
        Object nextLanguage=net.minecraft.util.Language.getInstance();
        if(message==widget.getMessage() && language==nextLanguage && width==widget.getWidth() && height==widget.getHeight())return;
        language=nextLanguage;
        message=widget.getMessage();width=widget.getWidth();height=widget.getHeight();plain=message.getString();
        float size=Math.min(13,height*.55f);
        var font=MinecraftClient.getInstance().textRenderer;
        fitted=GlassTextFit.fit(plain,size,width-8,value->GlassGlyphText.measure(font,value,1));
    }
    public boolean enqueue(DrawContext context,ClickableWidget widget,int color){
        update(widget);
        if(fitted==null)return false;
        this.context=context;this.widget=widget;this.color=color;
        matrix.set(context.getMatrices().peek().getPositionMatrix());
        net.auramc.glass.common.render.GlassRenderer.foreground(this);return true;
    }
    @Override public void run(){
        var current=context.getMatrices().peek().getPositionMatrix();saved.set(current);current.set(matrix);
        try{AuraNativeSurfaces.drawLabel(context,widget,fitted,color);}finally{current.set(saved);context=null;widget=null;}
    }
}
