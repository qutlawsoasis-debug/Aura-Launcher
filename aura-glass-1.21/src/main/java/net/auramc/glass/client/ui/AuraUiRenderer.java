package net.auramc.glass.client.ui;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.ClickableWidget;
import net.minecraft.client.gui.widget.SliderWidget;
import net.minecraft.resource.ResourceReload;
import net.minecraft.text.Text;
import net.minecraft.util.math.MathHelper;

import java.util.List;

public final class AuraUiRenderer {
    private static net.minecraft.client.gui.RotatingCubeMapRenderer panorama;
    private AuraUiRenderer() {}
    public static void prepareMenuPanorama(){if(panorama==null)panorama=new net.minecraft.client.gui.RotatingCubeMapRenderer(new net.minecraft.client.gui.CubeMapRenderer(net.minecraft.util.Identifier.tryParse("minecraft:textures/gui/title/background/panorama")));}

    public static void renderMenuPanoramaBackground(DrawContext context,MinecraftClient client,int width,int height) {
        if(panorama==null)return;
        panorama.render(context,width,height,1,1);
    }

    /**
     * Renders native compact Aura loading screen with honest localized stage text and real progress.
     * No fake progress ranges or artificial pauses.
     */
    public static void renderLoadingScreen(DrawContext context, MinecraftClient client, ResourceReload reload, int width, int height) {
        AuraNativeSurfaces.loading(context, reload, width, height);
    }

    /**
     * Renders Apple-like clean Aura heading on main menu panorama (non-pixelated, non-acid, no duplicates).
     */
    public static void renderTitleScreenBrand(DrawContext context, MinecraftClient client, int cx, int y) {
        renderBrandEmblemAndText(context, client, cx, y, 120, 30);
    }

    /**
     * Renders Aura brand emblem and title scaled to widget dimensions.
     * Uniform fit (min scale) ensures text and emblem always fit completely inside bounds.
     */
    public static void renderBrandEmblemAndText(DrawContext context, MinecraftClient client, int cx, int cy, int width, int height) {
        AuraNativeSurfaces.brand(context, cx, cy, width, height);
    }

    /**
     * Guide line record for smart alignment guides.
     */
    public record GuideLine(boolean isVertical, int position, int start, int end) {}

    /**
     * Renders smart guide lines during widget dragging / alignment.
     */
    public static void renderSmartGuideLines(DrawContext context, List<GuideLine> guides) {
        if (guides == null || guides.isEmpty()) {
            return;
        }
        for (GuideLine line : guides) {
            if (line.isVertical()) {
                context.fill(line.position(), line.start(), line.position() + 1, line.end(), 0xAA40C4FF);
            } else {
                context.fill(line.start(), line.position(), line.end(), line.position() + 1, 0xAA40C4FF);
            }
        }
    }

    /**
     * Renders translucent glass button plate and rim with moderate hover tint.
     */
    public static int renderButton(DrawContext context, ClickableWidget widget, boolean isHovered, float alpha) {
        return AuraNativeSurfaces.button(context, widget, isHovered, alpha);
    }

}
