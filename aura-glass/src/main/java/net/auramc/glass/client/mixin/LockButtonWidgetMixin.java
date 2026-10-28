package net.auramc.glass.client.mixin;
import net.auramc.glass.client.ui.*;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.LockButtonWidget;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(LockButtonWidget.class)
public abstract class LockButtonWidgetMixin {
    @Inject(method="renderButton",at=@At("HEAD"),cancellable=true)
    private void aura_lock(DrawContext c,int x,int y,float delta,CallbackInfo ci){
        if(!GlassFrameHooks.styledScreen())return;
        var self=(LockButtonWidget)(Object)this;AuraNativeSurfaces.lock(c,self,self.isLocked());ci.cancel();
    }
}
