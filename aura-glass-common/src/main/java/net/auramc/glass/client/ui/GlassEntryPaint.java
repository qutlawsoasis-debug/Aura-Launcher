package net.auramc.glass.client.ui;

import net.auramc.glass.common.render.GlassRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.EntryListWidget;

public final class GlassEntryPaint implements Runnable {
    private static final GlassEntryPaint[] COMMANDS=new GlassEntryPaint[4096];
    private static int cursor;
    static{for(int i=0;i<COMMANDS.length;i++)COMMANDS[i]=new GlassEntryPaint();}
    private DrawContext context;
    private EntryListWidget.Entry<?> entry;
    private int index,x,y,w,h,mx,my,l,t,r,b;
    private float delta;
    private boolean hovered,selected;
    public static void beginFrame(){cursor=0;}
    public static void draw(DrawContext context,EntryListWidget.Entry<?> entry,int index,int x,int y,int w,int h,int mx,int my,boolean hovered,float delta,int l,int t,int r,int b,boolean selected,boolean immediate){
        if(cursor==COMMANDS.length)return;
        GlassEntryPaint command=COMMANDS[cursor++];
        command.context=context;command.entry=entry;command.index=index;command.x=x;command.y=y;command.w=w;command.h=h;
        command.mx=mx;command.my=my;command.hovered=hovered;command.delta=delta;command.l=l;command.t=t;command.r=r;command.b=b;command.selected=selected;
        if(immediate)command.run();else GlassRenderer.foreground(command);
    }
    @Override public void run(){
        context.enableScissor(l,t,r,b);
        float[] clip=GlassRenderer.pushClip(l,t,r,b);
        boolean old=GlassScreenText.beginSelected(selected);
        try{entry.render(context,index,y,x,w,h,mx,my,hovered,delta);}
        finally{GlassScreenText.endSelected(old);GlassRenderer.popClip(clip);context.disableScissor();context=null;entry=null;}
    }
}
