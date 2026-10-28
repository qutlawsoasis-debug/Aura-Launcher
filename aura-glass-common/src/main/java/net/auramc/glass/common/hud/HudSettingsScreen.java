package net.auramc.glass.common.hud;

import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.text.Text;

public final class HudSettingsScreen extends Screen {
    private final Screen parent;
    public HudSettingsScreen(Screen parent){super(Text.translatable("aura.hud.title"));this.parent=parent;}
    @Override protected void init(){
        HudConfig config=HudRuntime.config();int x=width/2-120,y=Math.max(28,height/2-110);
        addDrawableChild(ButtonWidget.builder(label("aura.hud.title",config.enabled),button->{config.enabled=!config.enabled;config.save();clearAndInit();}).dimensions(x,y,240,20).build());
        String[] ids={HudRegistry.STATUS,HudRegistry.COORDS,HudRegistry.TOASTS,HudRegistry.PLAYER};
        for(int i=0;i<ids.length;i++){
            String id=ids[i];HudElement element=config.registry().get(id);
            Text source=id.equals(HudRegistry.PLAYER)&&!HudRuntime.musicAvailable()?Text.translatable("aura.hud.no_source"):Text.empty();
            addDrawableChild(ButtonWidget.builder(label("aura.hud."+id,element.visible).append(source),button->{element.visible=!element.visible;config.save();clearAndInit();}).dimensions(x,y+28+i*24,240,20).build());
        }
        addDrawableChild(ButtonWidget.builder(label("aura.hud.biome",config.biome),button->{config.biome=!config.biome;config.save();clearAndInit();}).dimensions(x,y+128,240,20).build());
        addDrawableChild(ButtonWidget.builder(Text.translatable("gui.done"),button->close()).dimensions(x,y+160,240,20).build());
    }
    private static net.minecraft.text.MutableText label(String key,boolean enabled){return Text.translatable(key).append(": ").append(Text.translatable(enabled?"options.on":"options.off"));}
    @Override public void render(DrawContext c,int mx,int my,float delta){c.fill(0,0,width,height,0xDF1C1E24);c.drawCenteredTextWithShadow(textRenderer,title,width/2,12,0xFFFFFFFF);super.render(c,mx,my,delta);}
    @Override public void close(){
        if(parent instanceof net.auramc.glass.client.ui.GlassGalleryScreen&&client.world!=null)net.auramc.glass.common.render.BackdropRenderer.requestWorldImage();
        client.setScreen(parent);
    }
}
