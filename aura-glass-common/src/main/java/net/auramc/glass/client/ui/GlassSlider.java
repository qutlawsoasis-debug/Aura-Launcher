package net.auramc.glass.client.ui;

import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.*;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.SliderWidget;

public final class GlassSlider {
    private GlassSlider(){}
    public static void paint(DrawContext context,SliderWidget widget,double value){
        int color=AuraNativeSurfaces.button(context,widget,widget.isHovered(),1);
        float x=widget.getX()+5,y=widget.getY()+widget.getHeight()-5,w=widget.getWidth()-10;
        float position=(float)Math.max(0,Math.min(1,value));
        var track=GlassStyles.frame(GlassPreset.SELECTED_DARK);
        track.radius=1.5f;track.tintA=.55f;track.refraction=0;track.light=.1f;
        GlassRenderer.draw(x,y,w,2,track,widget.active?1:.4f);
        if(widget.active&&(widget.isHovered()||widget.isFocused())){
            var accent=GlassStyles.frame(GlassPreset.PANEL);
            accent.tintR=100/255f;accent.tintG=210/255f;accent.tintB=1;accent.tintA=.95f;
            accent.radius=1.5f;accent.refraction=0;accent.light=.1f;
            GlassRenderer.draw(x,y,w*position,2,accent,1);
        }
        var knob=GlassStyles.frame(GlassPreset.PILL_THICK);
        knob.radius=3;knob.bevel=2;knob.tintA=.65f;knob.refraction=.1f;
        knob.tintR=knob.tintG=knob.tintB=.22f;knob.gloss=.1f;
        GlassRenderer.draw(x+position*(w-8),y-2,8,6,knob,widget.active?1:.4f);
        AuraNativeSurfaces.label(context,widget,color);
    }
}
