package net.auramc.glass.client.mixin;
import net.auramc.glass.client.ui.GlassFrameHooks;
import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.*;
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
    @Shadow protected abstract int getScrollbarX();
    public int aura_glassTop(){return ((EntryListWidget<?>)(Object)this).getY();}
    public int aura_glassBottom(){var self=(EntryListWidget<?>)(Object)this;return self.getY()+self.getHeight();}
    @Shadow private boolean renderHeader;
    @Shadow private EntryListWidget.Entry hoveredEntry;
    @Unique private final int[] aura_bounds=new int[2];
    @Unique private DrawContext aura_context;
    @Unique private int aura_mouseX,aura_mouseY;
    @Unique private final Runnable aura_header=this::aura_paintHeader,aura_decorations=this::aura_paintDecorations;
    @Unique private void aura_paintHeader(){
        var self=(EntryListWidget<?>)(Object)this;var c=aura_context;
        c.enableScissor(aura_left,aura_top,aura_right,aura_bottom);
        try{renderHeader(c,self.getRowLeft(),aura_top+4-(int)self.getScrollAmount());}finally{c.disableScissor();}
    }
    @Unique private void aura_paintDecorations(){try{renderDecorations(aura_context,aura_mouseX,aura_mouseY);}finally{aura_context=null;}}

    @Unique private int aura_left,aura_top,aura_right,aura_bottom;
    @Inject(method="renderWidget",at=@At("HEAD"),cancellable=true)
    private void aura_list(DrawContext c,int mx,int my,float delta,CallbackInfo ci) {
        if(!GlassFrameHooks.styledScreen())return;
        var self=(EntryListWidget<?>)(Object)this;int l=self.getX(),t=self.getY(),r=l+self.getWidth(),b=t+self.getHeight();
        aura_left=l;aura_top=t;aura_right=r;aura_bottom=b;
        aura_context=c;aura_mouseX=mx;aura_mouseY=my;
        hoveredEntry=mx>=l && mx<r && my>=t && my<b?getEntryAtPosition(mx,my):null;
        if(renderHeader)GlassRenderer.foreground(aura_header);
        renderList(c,mx,my,delta);
        int max=self.getMaxScroll();
        if(max>0){
            float h=Math.max(16,Math.min(b-t-8,(b-t)*(b-t)/(float)Math.max(1,getMaxPosition())));
            float y=t+(b-t-h)*(float)self.getScrollAmount()/max;
            GlassRenderer.draw(getScrollbarX()+2,y,2,h,GlassStyles.frame(GlassPreset.BUTTON_FILLED),.85f);
        }
        GlassRenderer.foreground(aura_decorations);ci.cancel();
    }
    @Inject(method="renderEntry",at=@At("HEAD"),cancellable=true)
    private void aura_entry(DrawContext c,int mx,int my,float delta,int index,int x,int y,int w,int h,CallbackInfo ci) {
        if(!GlassFrameHooks.styledScreen())return;
        EntryListWidget.Entry entry=getEntry(index);boolean hovered=entry==hoveredEntry;
        int l=aura_left,t=aura_top,r=aura_right,b=aura_bottom;
        if(!((Object)this instanceof net.minecraft.client.gui.widget.OptionListWidget) && (isSelectedEntry(index) || hovered)){
            int yy=Math.max(t,y),hh=Math.min(b,y+h)-yy;
            if(hh>0)GlassRenderer.draw(x,yy,w,hh,net.auramc.glass.client.ui.GlassListBounds.rowStyle(isSelectedEntry(index)),isSelectedEntry(index)?1:.55f);
        }
        net.auramc.glass.client.ui.GlassEntryPaint.draw(c,entry,index,x,y,w,h,mx,my,hovered,delta,l,t,r,b,isSelectedEntry(index),
                (Object)this instanceof net.minecraft.client.gui.widget.OptionListWidget);
        ci.cancel();
    }
}
