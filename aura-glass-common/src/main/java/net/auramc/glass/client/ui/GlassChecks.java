package net.auramc.glass.client.ui;

import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.*;
import net.minecraft.client.gui.widget.CheckboxWidget;

public final class GlassChecks {
    private GlassChecks(){}
    public static void draw(CheckboxWidget widget,int x,int y,int width,int height){
        float h=Math.min(12,height),top=y+(height-h)*.5f;
        var track=GlassStyles.frameResolve(widget.isChecked()?GlassStyles.frameSelected():GlassStyles.frame(GlassPreset.PANEL),GlassWidgetStates.state(widget),1);
        track.radius=h*.5f;
        GlassRenderer.draw(x,top,width,h,track,1);
        var knob=GlassStyles.frameResolve(GlassPreset.BUTTON_FILLED,GlassWidgetStates.state(widget),1);knob.radius=(h-2)*.5f;
        GlassRenderer.draw(x+1+(widget.isChecked()?width-h:0),top+1,h-2,h-2,knob,widget.active?1:.5f);
    }
}
