package net.auramc.glass.client.quickmenu.mixin;

import net.auramc.glass.client.quickmenu.*;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.render.VertexConsumer;
import net.minecraft.client.render.WorldRenderer;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.util.shape.VoxelShape;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

@Mixin(WorldRenderer.class)
public abstract class QuickMenuOutlineMixin {
    @WrapOperation(method = "drawBlockOutline", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/render/WorldRenderer;drawCuboidShapeOutline(Lnet/minecraft/client/util/math/MatrixStack;Lnet/minecraft/client/render/VertexConsumer;Lnet/minecraft/util/shape/VoxelShape;DDDFFFF)V"), require = 1)
    private void auraQuickMenuBlockOutline(MatrixStack matrices, VertexConsumer consumer, VoxelShape shape,
                                           double x, double y, double z, float red, float green, float blue, float alpha, Operation<Void> original) {
        var config = QuickMenuConfig.get();
        if (!config.blockOutline) { original.call(matrices, consumer, shape, x, y, z, red, green, blue, alpha); return; }
        var buffers = MinecraftClient.getInstance().getBufferBuilders().getEntityVertexConsumers();
        var layer = QuickMenuOutlineLayer.get();
        int color = config.outlineColor;
        original.call(matrices, buffers.getBuffer(layer), shape, x, y, z, ((color >> 16) & 255) / 255f, ((color >> 8) & 255) / 255f, (color & 255) / 255f, .9f);
        buffers.draw(layer);
        QuickMenuEffects.outlineDrawn(Math.max(1, Math.min(5, config.outlineWidth)), color);
    }
}
