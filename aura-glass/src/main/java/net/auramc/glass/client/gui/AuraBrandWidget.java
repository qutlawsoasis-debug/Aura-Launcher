package net.auramc.glass.client.gui;

import net.auramc.glass.client.ui.AuraUiRenderer;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.narration.NarrationMessageBuilder;
import net.minecraft.client.gui.widget.ClickableWidget;
import net.minecraft.text.Text;

/**
 * Movable and resizable main menu branding widget (Aura emblem + title).
 * Fully integrated into the free-form layout editor, snapping, and persistence.
 */
public class AuraBrandWidget extends ClickableWidget {
    public AuraBrandWidget(int x, int y, int width, int height) {
        super(x, y, width, height, Text.literal("Aura"));
    }

    @Override
    protected void renderButton(DrawContext context, int mouseX, int mouseY, float delta) {
        MinecraftClient client = MinecraftClient.getInstance();
        int cx = this.getX() + this.getWidth() / 2;
        int cy = this.getY() + this.getHeight() / 2;
        AuraUiRenderer.renderBrandEmblemAndText(context, client, cx, cy, this.getWidth(), this.getHeight());
    }

    @Override
    protected void appendClickableNarrations(NarrationMessageBuilder builder) {
    }
}
