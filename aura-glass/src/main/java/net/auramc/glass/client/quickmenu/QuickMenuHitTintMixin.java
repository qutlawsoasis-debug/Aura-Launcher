package net.auramc.glass.client.quickmenu;

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
    @WrapOperation(method = "render(Lnet/minecraft/entity/LivingEntity;FFLnet/minecraft/client/util/math/MatrixStack;Lnet/minecraft/client/render/VertexConsumerProvider;I)V", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/render/entity/model/EntityModel;render(Lnet/minecraft/client/util/math/MatrixStack;Lnet/minecraft/client/render/VertexConsumer;IIFFFF)V"), require = 1)
    private void auraQuickMenuHitTint(EntityModel<?> model, MatrixStack matrices, VertexConsumer vertices, int light, int overlay, float red, float green, float blue, float alpha, Operation<Void> original, @Local(argsOnly = true) LivingEntity entity) {
        int tint = QuickMenuConfig.get().hitTintColor;
        if (QuickMenuVisuals.tint(entity)) {
            red *= ((tint >> 16) & 255) / 255f; green *= ((tint >> 8) & 255) / 255f; blue *= (tint & 255) / 255f;
            original.call(model, matrices, vertices, light, overlay, red, green, blue, alpha); QuickMenuVisuals.tinted();
        } else original.call(model, matrices, vertices, light, overlay, red, green, blue, alpha);
    }
}
