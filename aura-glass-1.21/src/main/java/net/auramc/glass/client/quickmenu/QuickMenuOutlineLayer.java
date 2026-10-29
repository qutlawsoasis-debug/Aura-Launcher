package net.auramc.glass.client.quickmenu;

import com.mojang.blaze3d.systems.RenderSystem;
import net.minecraft.client.render.RenderLayer;
import net.minecraft.client.render.VertexFormat;
import net.minecraft.client.render.VertexFormats;

public final class QuickMenuOutlineLayer extends RenderLayer {
    private static QuickMenuOutlineLayer layer;
    private static double previousWidth;
    private QuickMenuOutlineLayer(double width) {
        super("aura_quickmenu_block_outline", VertexFormats.LINES, VertexFormat.DrawMode.LINES, 1536, false, false,
                () -> { RenderLayer.getLines().startDrawing(); RenderSystem.lineWidth((float) width); },
                () -> RenderLayer.getLines().endDrawing());
    }
    public static RenderLayer get() {
        double width = Math.max(1, Math.min(5, QuickMenuConfig.get().outlineWidth));
        if (layer == null || width != previousWidth) { layer = new QuickMenuOutlineLayer(width); previousWidth = width; }
        return layer;
    }
}
