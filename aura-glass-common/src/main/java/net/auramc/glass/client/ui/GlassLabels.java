package net.auramc.glass.client.ui;

import net.minecraft.client.gui.Element;
import net.minecraft.client.gui.ParentElement;
import net.minecraft.client.gui.widget.ClickableWidget;

public final class GlassLabels {
    private GlassLabels(){}
    public static void prepare(ParentElement root){if(GlassFrameHooks.styledScreen()){visit(root,0);GlassGroups.prepare(root);}}
    private static void visit(ParentElement root,int depth){
        if(depth>32)return;
        var children=root.children();
        for(int i=0;i<children.size();i++){
            Element child=children.get(i);
            if(child instanceof GlassLabelWidget cached && child instanceof ClickableWidget widget)cached.aura_label().update(widget);
            if(child instanceof ParentElement parent)visit(parent,depth+1);
        }
    }
}
