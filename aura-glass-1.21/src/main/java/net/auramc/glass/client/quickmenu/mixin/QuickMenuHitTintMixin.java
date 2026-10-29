package net.auramc.glass.client.quickmenu.mixin;

import net.auramc.glass.client.quickmenu.*;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.llamalad7.mixinextras.sugar.Local;
import net.minecraft.client.render.entity.LivingEntityRenderer;
import net.minecraft.client.render.entity.model.EntityModel;
import net.minecraft.client.render.VertexConsumer;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.entity.LivingEntity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

@Mixin(LivingEntityRenderer.class)
public abstract class QuickMenuHitTintMixin {
    @WrapOperation(method = "render(Lnet/minecraft/entity/LivingEntity;FFLnet/minecraft/client/util/math/MatrixStack;Lnet/minecraft/client/render/VertexConsumerProvider;I)V", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/render/entity/model/EntityModel;render(Lnet/minecraft/client/util/math/MatrixStack;Lnet/minecraft/client/render/VertexConsumer;III)V"), require = 1)
    private void auraQuickMenuHitTint(EntityModel<?> model, MatrixStack matrices, VertexConsumer vertices, int light, int overlay, int color, Operation<Void> original, @Local(argsOnly = true) LivingEntity entity) {
        if (QuickMenuVisuals.tint(entity)) {
            int tint = QuickMenuConfig.get().hitTintColor;
            int red = ((color >> 16) & 255) * ((tint >> 16) & 255) / 255;
            int green = ((color >> 8) & 255) * ((tint >> 8) & 255) / 255;
            int blue = (color & 255) * (tint & 255) / 255;
            original.call(model, matrices, vertices, light, overlay, (color & 0xFF000000) | red << 16 | green << 8 | blue); QuickMenuVisuals.tinted();
        } else original.call(model, matrices, vertices, light, overlay, color);
    }
}
