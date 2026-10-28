package net.auramc.glass.client.ui;
import net.minecraft.client.gui.Element;
import net.minecraft.client.gui.ParentElement;
import net.minecraft.client.gui.widget.ClickableWidget;
import net.minecraft.client.gui.widget.EntryListWidget;
public final class GlassListBounds {
    private GlassListBounds(){}
    public static int[] widgets(EntryListWidget<?> list,int[] bounds){
        bounds[0]=Integer.MAX_VALUE;bounds[1]=Integer.MIN_VALUE;
        var entries=list.children();
        for(int i=0;i<entries.size();i++)if(entries.get(i) instanceof ParentElement parent){
            var children=parent.children();
            for(int j=0;j<children.size();j++)if(children.get(j) instanceof ClickableWidget w){bounds[0]=Math.min(bounds[0],w.getX());bounds[1]=Math.max(bounds[1],w.getX()+w.getWidth());}
        }
        return bounds;
    }
}
