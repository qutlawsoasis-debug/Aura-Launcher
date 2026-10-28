package net.auramc.glass.client.gui;
import net.auramc.glass.client.ui.GlassGalleryScreen;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
public final class GlassGallery121 extends GlassGalleryScreen {
    public GlassGallery121(Screen parent) { super(parent); }
    @Override protected void panorama(DrawContext context,float delta) { renderPanoramaBackground(context,delta); }
    @Override public boolean mouseScrolled(double x,double y,double horizontal,double amount) { return scrollAt(x,amount); }
}
