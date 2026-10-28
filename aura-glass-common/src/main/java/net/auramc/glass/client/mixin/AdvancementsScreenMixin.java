package net.auramc.glass.client.mixin;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import net.auramc.glass.client.ui.GlassFrameHooks;
import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.GlassPreset;
import net.auramc.glass.common.style.GlassStyles;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.advancement.AdvancementsScreen;
import net.minecraft.util.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(AdvancementsScreen.class)
public abstract class AdvancementsScreenMixin {
    @Inject(method="render",at=@At("HEAD"))
    private void aura_window(DrawContext context,int mx,int my,float delta,CallbackInfo ci){
        if(!GlassFrameHooks.styledScreen())return;
        GlassRenderer.draw((context.getScaledWindowWidth()-252)*.5f,(context.getScaledWindowHeight()-140)*.5f,252,140,GlassStyles.frame(GlassPreset.PANEL),1);
        context.draw();GlassRenderer.flush();
    }
    @Inject(method={"drawAdvancementTree","drawWidgetTooltip"},at=@At("HEAD"))
    private void aura_nativeTree(DrawContext context,int a,int b,int x,int y,CallbackInfo ci){
        if(GlassFrameHooks.styledScreen()){context.draw();GlassRenderer.flush();GlassFrameHooks.beginNativeContent();}
    }
    @Inject(method={"drawAdvancementTree","drawWidgetTooltip"},at=@At("RETURN"))
    private void aura_endTree(DrawContext context,int a,int b,int x,int y,CallbackInfo ci){context.draw();GlassFrameHooks.endNativeContent();}
    @WrapOperation(method="drawWindow",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/DrawContext;drawTexture(Lnet/minecraft/util/Identifier;IIIIII)V",ordinal=0))
    private void aura_noPixelFrame(DrawContext context,Identifier texture,int x,int y,int u,int v,int w,int h,Operation<Void> original){
        if(!GlassFrameHooks.styledScreen())original.call(context,texture,x,y,u,v,w,h);
    }
}
