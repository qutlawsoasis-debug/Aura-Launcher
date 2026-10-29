package net.auramc.glass.client.mixin;
import net.auramc.glass.client.ui.GlassScreenText;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.text.OrderedText;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;
@Mixin(DrawContext.class)
public abstract class DrawContextTextMixin {
    @Inject(method="drawText(Lnet/minecraft/client/font/TextRenderer;Ljava/lang/String;IIIZ)I",at=@At("HEAD"),cancellable=true)
    private void aura_string(TextRenderer font,String text,int x,int y,int color,boolean shadow,CallbackInfoReturnable<Integer> cir){
        if(GlassScreenText.draw((DrawContext)(Object)this,font,text,x,y,color,shadow))cir.setReturnValue(x+font.getWidth(text));
    }
    @Inject(method="drawText(Lnet/minecraft/client/font/TextRenderer;Lnet/minecraft/text/OrderedText;IIIZ)I",at=@At("HEAD"),cancellable=true)
    private void aura_ordered(TextRenderer font,OrderedText text,int x,int y,int color,boolean shadow,CallbackInfoReturnable<Integer> cir){
        if(!net.auramc.glass.client.ui.GlassFrameHooks.styledScreen())return;
        if(GlassScreenText.draw((DrawContext)(Object)this,font,text,x,y,color,shadow))cir.setReturnValue(x+font.getWidth(text));
    }
    @Inject(method="drawTexture(Lnet/minecraft/util/Identifier;IIFFIIII)V",at=@At("HEAD"),cancellable=true)
    private void aura_separators(net.minecraft.util.Identifier texture,int x,int y,float u,float v,int w,int h,int tw,int th,org.spongepowered.asm.mixin.injection.callback.CallbackInfo ci){
        if(net.auramc.glass.client.ui.GlassFrameHooks.styledScreen() && (texture.getPath().contains("header_separator") || texture.getPath().contains("footer_separator")))ci.cancel();
    }
    @Inject(method="drawOrderedTooltip",at=@At("HEAD"),cancellable=true)
    private void aura_tooltip(TextRenderer font,java.util.List<? extends OrderedText> lines,int x,int y,org.spongepowered.asm.mixin.injection.callback.CallbackInfo ci){
        if(net.auramc.glass.client.ui.GlassTooltip.draw((DrawContext)(Object)this,font,lines,x,y))ci.cancel();
    }
    @Inject(method="drawTooltip(Lnet/minecraft/client/font/TextRenderer;Ljava/util/List;Lnet/minecraft/client/gui/tooltip/TooltipPositioner;II)V",at=@At("HEAD"),cancellable=true)
    private void aura_positionedTooltip(TextRenderer font,java.util.List<OrderedText> lines,net.minecraft.client.gui.tooltip.TooltipPositioner positioner,int x,int y,org.spongepowered.asm.mixin.injection.callback.CallbackInfo ci){
        if(net.auramc.glass.client.ui.GlassTooltip.draw((DrawContext)(Object)this,font,lines,x,y))ci.cancel();
    }
}
