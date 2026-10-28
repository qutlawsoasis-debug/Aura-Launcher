package net.auramc.glass.common.hud;

import java.util.LinkedHashMap;
import java.util.Map;

public final class HudRegistry {
    public static final String STATUS="status",COORDS="coords",TOASTS="toasts",PLAYER="miniPlayer";
    private final Map<String,HudElement> elements=new LinkedHashMap<>();
    public HudRegistry(){
        register(new HudElement(STATUS,HudElement.Anchor.TOP_CENTER,0,.02f,false));
        register(new HudElement(COORDS,HudElement.Anchor.TOP_LEFT,.02f,.10f,false));
        register(new HudElement(TOASTS,HudElement.Anchor.TOP_RIGHT,-.02f,.10f,false));
        register(new HudElement(PLAYER,HudElement.Anchor.BOTTOM_LEFT,.02f,-.08f,false));
    }
    public void register(HudElement element){element.validate();if(elements.putIfAbsent(element.id,element)!=null)throw new IllegalArgumentException("duplicate HUD id "+element.id);}
    public HudElement get(String id){return elements.get(id);}
    Map<String,HudElement> layouts(){return elements;}
    void apply(Map<String,HudElement> loaded){
        for(var entry:loaded.entrySet()){
            HudElement value=entry.getValue();if(value==null||!entry.getKey().equals(value.id))throw new IllegalArgumentException("HUD id mismatch");
            value.validate();
        }
        elements.putAll(loaded);
    }
}
