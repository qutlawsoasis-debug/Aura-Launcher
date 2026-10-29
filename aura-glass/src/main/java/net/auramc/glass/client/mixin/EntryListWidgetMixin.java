package net.auramc.glass.client.mixin;

import net.auramc.glass.client.ui.GlassFrameHooks;
import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.*;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.EntryListWidget;
import org.spongepowered.asm.mixin.*;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(EntryListWidget.class)
public abstract class EntryListWidgetMixin implements net.auramc.glass.client.ui.GlassListViewport {
    @Shadow protected abstract EntryListWidget.Entry getEntryAtPosition(double x,double y);
    @Shadow protected abstract EntryListWidget.Entry getEntry(int index);
    @Shadow protected abstract boolean isSelectedEntry(int index);
    @Shadow protected abstract void renderList(DrawContext c,int mx,int my,float delta);
    @Shadow protected abstract void renderHeader(DrawContext c,int x,int y);
    @Shadow protected abstract void renderDecorations(DrawContext c,int mx,int my);
    @Shadow protected abstract int getMaxPosition();
    @Shadow protected int left,top,right,bottom;
    public int aura_glassTop(){return top;}
    public int aura_glassBottom(){return bottom;}
    @Shadow protected abstract int getScrollbarPositionX();
    @Shadow private boolean renderHeader;
    @Shadow private EntryListWidget.Entry hoveredEntry;
    @Unique private final int[] aura_bounds=new int[2];
    @Unique private DrawContext aura_context;
    @Unique private int aura_mouseX,aura_mouseY;
    @Unique private long aura_lastScrollTime;
    @Unique private final Runnable aura_header=this::aura_paintHeader,aura_decorations=this::aura_paintDecorations;
    @Unique private void aura_paintHeader(){
        var self=(EntryListWidget<?>)(Object)this;var c=aura_context;
        c.enableScissor(aura_left,aura_top,aura_right,aura_bottom);
        try{renderHeader(c,self.getRowLeft(),aura_top+4-(int)self.getScrollAmount());}finally{c.disableScissor();}
    }
    @Unique private void aura_paintDecorations(){try{renderDecorations(aura_context,aura_mouseX,aura_mouseY);}finally{aura_context=null;}}

    @Inject(method="setScrollAmount",at=@At("HEAD"))
    private void aura_onScroll(double amount, CallbackInfo ci){
        aura_lastScrollTime = System.currentTimeMillis();
    }

    @Unique private int aura_left,aura_top,aura_right,aura_bottom;
    @Inject(method="render",at=@At("HEAD"),cancellable=true)
    private void aura_list(DrawContext c,int mx,int my,float delta,CallbackInfo ci) {
        if(!GlassFrameHooks.styledScreen())return;
        var self=(EntryListWidget<?>)(Object)this;int l=left,t=top,r=right,b=bottom;
        aura_left=l;aura_top=t;aura_right=r;aura_bottom=b;
        aura_context=c;aura_mouseX=mx;aura_mouseY=my;
        hoveredEntry=mx>=l && mx<r && my>=t && my<b?getEntryAtPosition(mx,my):null;

        if (!((Object)this instanceof net.minecraft.client.gui.widget.OptionListWidget)
                && !this.getClass().getName().contains("Keybind")) {
            @SuppressWarnings({"rawtypes", "unchecked"})
            EntryListWidget raw = (EntryListWidget) (Object) this;
            if (raw.getSelectedOrNull() == null && !raw.children().isEmpty()) {
                raw.setSelected((net.minecraft.client.gui.widget.EntryListWidget.Entry) raw.children().get(0));
            }
            int screenW = MinecraftClient.getInstance().getWindow().getScaledWidth();
            float listW = self.getRowWidth() + 16;
            float listX = (screenW - listW) / 2.0f;
            float listY = t;
            float listH = b - t;
            var listBacking = GlassStyles.frame(GlassPreset.PANEL);
            listBacking.asSurface();
            listBacking.radius = 12;
            listBacking.bevel = 2;
            listBacking.refraction = 0.20f;
            listBacking.dispersion = 0.005f;
            listBacking.frost = 2.5f;
            listBacking.rimOuter = 0.22f;
            listBacking.tintR = 28f / 255f;
            listBacking.tintG = 28f / 255f;
            listBacking.tintB = 30f / 255f;
            listBacking.tintA = 0.30f;
            GlassRenderer.drawSurface(this, listX, listY, listW, listH, listBacking, 1.0f);
        }

        if(renderHeader)GlassRenderer.foreground(aura_header);
        renderList(c,mx,my,delta);
        int max=self.getMaxScroll();
        if(max>0 && (System.currentTimeMillis() - aura_lastScrollTime < 1200L)){
            float h=Math.max(16,Math.min(b-t-8,(b-t)*(b-t)/(float)Math.max(1,getMaxPosition())));
            float y=t+(b-t-h)*(float)self.getScrollAmount()/max;
            var scrollStyle = GlassStyles.frame(GlassPreset.PANEL);
            scrollStyle.asFill();
            scrollStyle.tintR = 1f; scrollStyle.tintG = 1f; scrollStyle.tintB = 1f;
            scrollStyle.tintA = 0.35f;
            scrollStyle.radius = 1.5f;
            scrollStyle.rimOuter = 0;
            scrollStyle.refraction = 0;
            int screenW = MinecraftClient.getInstance().getWindow().getScaledWidth();
            float scrollX = (screenW + self.getRowWidth()) / 2.0f + 2.0f;
            GlassRenderer.draw(scrollX, y, 3.0f, h, scrollStyle, 1.0f);
        }
        GlassRenderer.foreground(aura_decorations);ci.cancel();
    }
    @Inject(method="renderEntry",at=@At("HEAD"),cancellable=true)
    private void aura_entry(DrawContext c,int mx,int my,float delta,int index,int x,int y,int w,int h,CallbackInfo ci) {
        if(!GlassFrameHooks.styledScreen())return;
        EntryListWidget.Entry entry=getEntry(index);boolean hovered=entry==hoveredEntry;
        int l=aura_left,t=aura_top,r=aura_right,b=aura_bottom;
        if(!((Object)this instanceof net.minecraft.client.gui.widget.OptionListWidget) && isSelectedEntry(index)){
            int yy=Math.max(t,y),hh=Math.min(b,y+h)-yy;
            if(hh>0){
                int screenW = MinecraftClient.getInstance().getWindow().getScaledWidth();
                float rowW = w;
                float rowX = (screenW - rowW) / 2.0f;
                GlassRenderer.draw(rowX, yy, rowW, hh, net.auramc.glass.client.ui.GlassListBounds.rowStyle(true), 1.0f);
            }
        }
        net.auramc.glass.client.ui.GlassEntryPaint.draw(c,entry,index,x,y,w,h,mx,my,hovered,delta,l,t,r,b,isSelectedEntry(index),
                (Object)this instanceof net.minecraft.client.gui.widget.OptionListWidget);
        ci.cancel();
    }
}
