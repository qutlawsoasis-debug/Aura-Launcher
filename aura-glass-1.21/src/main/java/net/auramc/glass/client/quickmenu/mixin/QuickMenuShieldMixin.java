package net.auramc.glass.client.quickmenu.mixin;

import net.auramc.glass.client.quickmenu.*;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import net.minecraft.client.network.AbstractClientPlayerEntity;
import net.minecraft.client.render.VertexConsumerProvider;
import net.minecraft.client.render.item.HeldItemRenderer;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.item.ItemStack;
import net.minecraft.item.Items;
import net.minecraft.util.Hand;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

@Mixin(HeldItemRenderer.class)
public abstract class QuickMenuShieldMixin {
    @WrapOperation(method = "renderItem(FLnet/minecraft/client/util/math/MatrixStack;Lnet/minecraft/client/render/VertexConsumerProvider$Immediate;Lnet/minecraft/client/network/ClientPlayerEntity;I)V", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/render/item/HeldItemRenderer;renderFirstPersonItem(Lnet/minecraft/client/network/AbstractClientPlayerEntity;FFLnet/minecraft/util/Hand;FLnet/minecraft/item/ItemStack;FLnet/minecraft/client/util/math/MatrixStack;Lnet/minecraft/client/render/VertexConsumerProvider;I)V"), require = 1)
    private void auraQuickMenuLowShield(HeldItemRenderer renderer, AbstractClientPlayerEntity player, float delta, float pitch,
                                        Hand hand, float swing, ItemStack stack, float equip, MatrixStack matrices,
                                        VertexConsumerProvider buffers, int light, Operation<Void> original) {
        var config = QuickMenuConfig.get();
        boolean loweredShield = config.lowShield && stack.isOf(Items.SHIELD);
        boolean transformed = config.handTransform;
        if (!loweredShield && !transformed) { original.call(renderer, player, delta, pitch, hand, swing, stack, equip, matrices, buffers, light); return; }
        matrices.push();
        try {
            if (loweredShield) matrices.translate(0, -.35, 0);
            if (transformed) {
                matrices.translate(config.handOffsetX, config.handOffsetY, config.handOffsetZ);
                float scale = (float) Math.max(.5, Math.min(1.5, config.handScale));
                matrices.scale(scale, scale, scale);
                if (config.swingAnimation == 1) swing *= .45f;
                else if (config.swingAnimation == 2) swing = (float) Math.sin(swing * Math.PI * .5);
            }
            original.call(renderer, player, delta, pitch, hand, swing, stack, equip, matrices, buffers, light);
            if (loweredShield) QuickMenuEffects.shieldDrawn(-.35);
        } finally { matrices.pop(); }
    }
}
