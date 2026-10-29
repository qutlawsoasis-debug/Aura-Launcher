package net.auramc.glass.client.quickmenu.mixin;

import net.auramc.glass.client.quickmenu.*;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.hud.InGameOverlayRenderer;
import net.minecraft.client.util.math.MatrixStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

@Mixin(InGameOverlayRenderer.class)
public abstract class QuickMenuFireMixin {
    @WrapOperation(method = "renderOverlays", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/gui/hud/InGameOverlayRenderer;renderFireOverlay(Lnet/minecraft/client/MinecraftClient;Lnet/minecraft/client/util/math/MatrixStack;)V"), require = 1)
    private static void auraQuickMenuLowFire(MinecraftClient client, MatrixStack matrices, Operation<Void> original) {
        if (!QuickMenuConfig.get().lowFire) { original.call(client, matrices); return; }
        matrices.push();
        try { matrices.translate(0, -.35, 0); original.call(client, matrices); QuickMenuEffects.fireDrawn(-.35); }
        finally { matrices.pop(); }
    }
}
