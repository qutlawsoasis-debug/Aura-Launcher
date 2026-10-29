package net.auramc.glass.client.ui;

import java.util.List;
import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.*;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.Element;
import net.minecraft.client.gui.ParentElement;
import net.minecraft.client.gui.widget.ClickableWidget;
import net.minecraft.text.OrderedText;

public final class GlassTooltip {
    private static final int PAD=6;
    private GlassTooltip(){}
    private static ClickableWidget hovered(ParentElement parent,int x,int y,int depth,boolean focused){
        if(depth>16)return null;
        var children=parent.children();
        for(int i=0;i<children.size();i++){
            Element child=children.get(i);
            if(child instanceof ParentElement nested){var found=hovered(nested,x,y,depth+1,focused);if(found!=null)return found;}
            if(child instanceof ClickableWidget widget && widget.visible && (focused?widget.isFocused():widget.isMouseOver(x,y)))return widget;
        }
        return null;
    }
    public static boolean draw(DrawContext context,TextRenderer font,List<? extends OrderedText> lines,int mouseX,int mouseY){
        if(!GlassFrameHooks.styledScreen()||lines.isEmpty())return false;
        var client=MinecraftClient.getInstance();
        int width=0;for(int i=0;i<lines.size();i++)width=Math.max(width,font.getWidth(lines.get(i)));
        int height=lines.size()*12-2;
        width=Math.min(context.getScaledWindowWidth()-2*PAD-8,width);
        int x=Math.max(4,Math.min(mouseX+12,context.getScaledWindowWidth()-width-2*PAD-4));
        int y=mouseY+18;
        var widget=hovered(client.currentScreen,mouseX,mouseY,0,false);
        if(widget==null)widget=hovered(client.currentScreen,mouseX,mouseY,0,true);
        if(widget!=null){
            int right=widget.getX()+widget.getWidth()+8;
            int left=widget.getX()-width-2*PAD-8;
            if(right+width+2*PAD<=context.getScaledWindowWidth()-4){x=right;y=widget.getY();}
            else if(left>=4){x=left;y=widget.getY();}
            else{
                x=Math.max(4,Math.min(widget.getX(),context.getScaledWindowWidth()-width-2*PAD-4));
                y=widget.getY()+widget.getHeight()+8;
            }
        }
        if(y+height+2*PAD>context.getScaledWindowHeight()-4)
            y=(widget==null?mouseY:widget.getY())-height-2*PAD-8;
        y=Math.max(4,y);
        context.draw();GlassRenderer.flush();
        var style=GlassStyles.frame(GlassPreset.SELECTED_DARK);
        style.tintR=.06f;style.tintG=.07f;style.tintB=.09f;style.tintA=.9f;
        style.frost=3;style.refraction=0;style.radius=5;style.light=.2f;
        GlassRenderer.draw(x,y,width+2*PAD,height+2*PAD,style,1);
        GlassRenderer.flush();
        boolean old=GlassScreenText.beginSelected(true);
        try{
            for(int i=0;i<lines.size();i++)GlassScreenText.draw(context,font,lines.get(i),x+PAD,y+PAD+i*12,0xFFFFFFFF,true);
            GlassRenderer.flush();
        }finally{GlassScreenText.endSelected(old);}
        return true;
    }
}
