package net.auramc.glass.client.gui;
import net.auramc.glass.client.ui.GlassGalleryScreen;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
public final class GlassGallery120 extends GlassGalleryScreen {
    public GlassGallery120(Screen parent) { super(parent); }
    @Override protected void panorama(DrawContext context,float delta) { net.auramc.glass.client.ui.AuraUiRenderer.renderMenuPanoramaBackground(context,client,width,height); }
    @Override public boolean mouseScrolled(double x,double y,double amount) { return scrollAt(x,amount); }
}
