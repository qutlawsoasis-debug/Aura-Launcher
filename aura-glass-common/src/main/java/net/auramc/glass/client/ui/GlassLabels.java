package net.auramc.glass.client.ui;

import net.minecraft.client.gui.Element;
import net.minecraft.client.gui.ParentElement;
import net.minecraft.client.gui.widget.ClickableWidget;
import java.util.Collections;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

public final class GlassLabels {
    private static final Set<String> FIELD_LABELS = Collections.newSetFromMap(new ConcurrentHashMap<>());

    private GlassLabels(){}

    public static void prepare(ParentElement root){
        FIELD_LABELS.clear();
        if(GlassFrameHooks.styledScreen()){
            visit(root,0);
            GlassGroups.prepare(root);
        }
    }

    public static void markFieldLabel(Element label) {
        if (label instanceof ClickableWidget widget && widget.getMessage() != null) {
            FIELD_LABELS.add(widget.getMessage().getString());
        }
    }

    public static boolean isFieldLabel(String text) {
        if (text == null) return false;
        if (FIELD_LABELS.contains(text)) return true;
        return text.equals("selectWorld.enterName") || text.equals("selectWorld.enterSeed")
                || text.equals("Название мира") || text.equals("Ключ для генератора мира")
                || text.equals("World Name") || text.equals("Seed for the world generator")
                || text.equals("Имя сервера") || text.equals("Адрес сервера")
                || text.equals("Server Name") || text.equals("Server Address");
    }

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
