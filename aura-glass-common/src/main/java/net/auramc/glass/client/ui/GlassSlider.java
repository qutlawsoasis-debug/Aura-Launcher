package net.auramc.glass.client.ui;

import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.*;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.SliderWidget;

public final class GlassSlider {
    private GlassSlider(){}
    public static void paint(DrawContext context,SliderWidget widget,double value){
        int color=AuraNativeSurfaces.button(context,widget,widget.isHovered(),1);
        float position=(float)Math.max(0,Math.min(1,value));
        if(GlassStyles.regular()){
            float x=widget.getX()+6,w=widget.getWidth()-12;
            float trackH=4;
            float trackY=widget.getY()+widget.getHeight()-7;
            var track=GlassStyles.frame(GlassPreset.PANEL);
            track.asFill();track.radius=2;
            track.tintR=118/255f;track.tintG=118/255f;track.tintB=128/255f;track.tintA=0.36f;
            track.rimOuter=0;
            GlassRenderer.draw(x,trackY,w,trackH,track,widget.active?1:.4f);

            if(position>0.001f){
                var fill=GlassStyles.frame(GlassPreset.PANEL);
                fill.asFill();fill.radius=2;
                fill.tintR=100/255f;fill.tintG=210/255f;fill.tintB=1;fill.tintA=1;
                fill.rimOuter=0;
                GlassRenderer.draw(x,trackY,Math.max(trackH,w*position),trackH,fill,widget.active?1:.4f);
            }

            float knobSize=14;
            float knobX=x+position*(w-knobSize);
            float knobY=trackY+(trackH-knobSize)*.5f;

            var knobShadow=GlassStyles.frame(GlassPreset.PANEL);
            knobShadow.asFill();knobShadow.radius=7;
            knobShadow.tintR=knobShadow.tintG=knobShadow.tintB=0;knobShadow.tintA=0.30f;
            knobShadow.rimOuter=0;
            GlassRenderer.draw(knobX,knobY+1,knobSize,knobSize,knobShadow,widget.active?1:.4f);

            var knob=GlassStyles.frame(GlassPreset.PANEL);
            knob.asFill();knob.radius=7;
            knob.tintR=knob.tintG=knob.tintB=1;knob.tintA=1;
            knob.rimOuter=0.08f;
            GlassRenderer.draw(knobX,knobY,knobSize,knobSize,knob,widget.active?1:.4f);
        } else {
            float x=widget.getX()+5,y=widget.getY()+widget.getHeight()-5,w=widget.getWidth()-10;
            var track=GlassStyles.frame(GlassPreset.SELECTED_DARK);
            track.asFill();track.radius=1.5f;track.tintA=.55f;track.refraction=0;track.light=.1f;
            GlassRenderer.draw(x,y,w,2,track,widget.active?1:.4f);
            if(widget.active&&(widget.isHovered()||widget.isFocused())){
                var accent=GlassStyles.frame(GlassPreset.PANEL);
                accent.tintR=100/255f;accent.tintG=210/255f;accent.tintB=1;accent.tintA=.95f;
                accent.asFill();accent.radius=1.5f;accent.refraction=0;accent.light=.1f;
                GlassRenderer.draw(x,y,w*position,2,accent,1);
            }
            var knob=GlassStyles.frame(GlassPreset.PILL_THICK);
            knob.asFill();knob.radius=3;knob.bevel=2;knob.tintA=.65f;knob.refraction=.1f;
            knob.tintR=knob.tintG=knob.tintB=.22f;knob.gloss=.1f;
            GlassRenderer.draw(x+position*(w-8),y-2,8,6,knob,widget.active?1:.4f);
        }
        AuraNativeSurfaces.label(context,widget,color);
    }
}
