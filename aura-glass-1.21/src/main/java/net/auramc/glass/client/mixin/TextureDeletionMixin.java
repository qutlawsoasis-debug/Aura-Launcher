package net.auramc.glass.client.mixin;
import com.mojang.blaze3d.platform.GlStateManager;
import com.mojang.blaze3d.systems.RenderSystem;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(value=GlStateManager.class,remap=false)
public abstract class TextureDeletionMixin {
    @Inject(method="_deleteTexture",at=@At("HEAD"))
    private static void aura_forgetDeletedTexture(int texture,CallbackInfo ci){
        if(texture<=0 || !RenderSystem.isOnRenderThread())return;
        // Shader texture slots can retain a world-icon ID after its GL object is deleted.
        for(int unit=0;unit<5;unit++)if(RenderSystem.getShaderTexture(unit)==texture)RenderSystem.setShaderTexture(unit,0);
    }
}
