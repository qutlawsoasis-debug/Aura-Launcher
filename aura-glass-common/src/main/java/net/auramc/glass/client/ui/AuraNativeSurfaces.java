package net.auramc.glass.client.ui;

import com.mojang.blaze3d.systems.RenderSystem;
import net.auramc.glass.common.render.AuraRenderer;
import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.*;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.ClickableWidget;
import net.minecraft.resource.ResourceReload;

public final class AuraNativeSurfaces {
    private static int lastPercent = -1;
    private static String progressText = "";
    private static final java.util.Set<String> PRIMARY=java.util.Set.of("menu.singleplayer","menu.returnToGame","gui.done","selectWorld.select","selectWorld.create","selectServer.select","addServer.add","addServer.save","selectServer.direct","mco.select");
    private static final NativeCommand[] COMMANDS=new NativeCommand[256];
    private static int cursor;
    static{for(int i=0;i<COMMANDS.length;i++)COMMANDS[i]=new NativeCommand();}
    public static void beginFrame(){cursor=0;}
    private static NativeCommand command(DrawContext context,int type,int color){
        if(cursor==COMMANDS.length)return null;
        NativeCommand command=COMMANDS[cursor++];command.context=context;command.type=type;command.color=color;
        return command;
    }

    private AuraNativeSurfaces() {}

    private static boolean begin(DrawContext context) {
        context.draw();
        return AuraRenderer.begin(context.getMatrices().peek().getPositionMatrix(), RenderSystem.getProjectionMatrix(), RenderSystem.getModelViewMatrix());
    }

    public static void loading(DrawContext context, ResourceReload reload, int width, int height) {
        context.fill(0, 0, width, height, 0xFF101116);
        float progress = reload == null ? 0 : Math.max(0, Math.min(1, reload.getProgress()));
        float w = Math.max(16, Math.min(260, width - 64));
        float x = (width - w) * .5f, y = height * .5f;
        if (!AuraRenderer.isReady() || !begin(context)) {
            context.fill((int) x, (int) y, (int) (x + w), (int) y + 2, 0x30FFFFFF);
            context.fill((int) x, (int) y, (int) (x + w * progress), (int) y + 2, 0xFF64D2FF);
            return;
        }
        AuraRenderer.text("Aura", x, y - 49, 30, 0xFFFFFFFF, 600);
        int pct = (int) (progress * 100);
        if (pct != lastPercent) {
            lastPercent = pct;
            progressText = pct + "%";
        }
        AuraRenderer.text("Загрузка ресурсов", x, y - 14, 11, 0xC7FFFFFF);
        AuraRenderer.text(progressText, x + w - AuraRenderer.textWidth(progressText, 11), y - 14, 11, 0xC7FFFFFF);
        AuraRenderer.rect(x, y + 8, w, 3, 1.5f, 0, 0x30FFFFFF, 0);
        AuraRenderer.rect(x, y + 8, w * progress, 3, 1.5f, 0, 0xFF64D2FF, 0);
        AuraRenderer.flush();
    }

    public static void brand(DrawContext context, int cx, int cy, int width, int height) {
        if (!AuraRenderer.isReady() || !begin(context)) {
            var client = net.minecraft.client.MinecraftClient.getInstance();
            context.drawCenteredTextWithShadow(client.textRenderer, "Aura", cx, cy - 4, 0xFFFFFFFF);
            return;
        }
        float size = Math.min(height * .65f, width / 3.0f);
        AuraRenderer.text("Aura", cx - AuraRenderer.textWidth("Aura", size, 600) * .5f, cy - size * .5f, size, 0xFFFFFFFF, 600);
        AuraRenderer.flush();
    }

    public static int button(DrawContext context, ClickableWidget widget, boolean hovered, float alpha) {
        var client = net.minecraft.client.MinecraftClient.getInstance();
        boolean primary = widget.getMessage().getContent() instanceof net.minecraft.text.TranslatableTextContent label
                && PRIMARY.contains(label.getKey());
        float hover = 0, press = 0;
        if (widget instanceof AuraMotionWidget motion) {
            long window = client.getWindow().getHandle();
            boolean mouseDown = hovered && org.lwjgl.glfw.GLFW.glfwGetMouseButton(window, 0) == org.lwjgl.glfw.GLFW.GLFW_PRESS;
            boolean keyDown = widget.isFocused() && (org.lwjgl.glfw.GLFW.glfwGetKey(window, org.lwjgl.glfw.GLFW.GLFW_KEY_ENTER) == org.lwjgl.glfw.GLFW.GLFW_PRESS
                    || org.lwjgl.glfw.GLFW.glfwGetKey(window, org.lwjgl.glfw.GLFW.GLFW_KEY_SPACE) == org.lwjgl.glfw.GLFW.GLFW_PRESS);
            hover = motion.aura_glassHover(widget.active && (hovered || widget.isFocused()) ? 1 : 0);
            press = motion.aura_glassPress(widget.active && (mouseDown || keyDown) ? 1 : 0);
        }
        GlassState state = !widget.active ? GlassState.DISABLED : press > .01f ? GlassState.PRESS
                : widget.isFocused() ? GlassState.FOCUSED : GlassState.HOVER;
        float amount = !widget.active ? 1 : state == GlassState.PRESS ? press : hover;
        boolean selectedTab=widget instanceof net.minecraft.client.gui.widget.TabButtonWidget tab && tab.isCurrentTab();
        var base=selectedTab?GlassStyles.frameSelected():GlassStyles.frame(primary?GlassPreset.BUTTON_FILLED:GlassPreset.PILL_THICK);
        var style=GlassStyles.frameResolve(base,state,amount);
        if(GlassStyles.regular()){
            style.radius=selectedTab ? 12 : 6;
            style.rimOuter=.10f;
            style.scale=GlassStyles.quality()==GlassQuality.OFF?1:1+.012f*hover-.025f*press;
            if(widget.active&&hovered&&GlassStyles.quality()!=GlassQuality.OFF)style.pointer((float)(client.mouse.getX()/client.getWindow().getScaleFactor()-widget.getX()),(float)(client.mouse.getY()/client.getWindow().getScaleFactor()-widget.getY()));
            if(primary){style.asPrimary();style.tintR=style.tintG=style.tintB=1;style.tintA=.92f;}
            else{
                style.asFill();
                style.tintR=style.tintG=118/255f;
                style.tintB=128/255f;
                // Requirement 4: активная вкладка — заливка rgba(118,118,128,0.36)
                style.tintA=!widget.active?.12f:selectedTab?.36f:.24f+.12f*hover+.14f*press;
            }
        }
        if(!GlassStyles.regular())net.auramc.glass.common.render.AdaptiveText.request(widget, widget.getX()+widget.getWidth()*.5f, widget.getY()+widget.getHeight()*.5f, style);
        GlassRenderer.drawWidget(widget, widget.getX(), widget.getY(), widget.getWidth(), widget.getHeight(), style, alpha);
        int rgb = primary ? 0x18232C : 0xF7F9FC;
        return ((int) (255 * Math.max(0, Math.min(1, alpha)) * (widget.active ? 1 : .5f)) << 24) | rgb;
    }
    public static void text(DrawContext context,String text,float x,float y,float size,int color) {
        NativeCommand command=command(context,0,color);if(command==null)return;
        command.text=text;command.x=x;command.y=y;command.size=size;GlassRenderer.foreground(command);
    }

    public static boolean label(DrawContext context, ClickableWidget widget, int color) {
        if (widget.getWidth() <= 8 || !AuraRenderer.isReady()) return false;
        return widget instanceof GlassLabelWidget cached && cached.aura_label().enqueue(context,widget,color);
    }

    static void drawLabel(DrawContext context, ClickableWidget widget,net.auramc.glass.common.ui.GlassTextFit.Fitted fitted,int color) {
        var font=net.minecraft.client.MinecraftClient.getInstance().textRenderer;
        String text=fitted.text();float size=fitted.size(),textWidth=fitted.width();
        if (!begin(context)) return;
        float[] clip=GlassRenderer.clipBounds();
        if(clip!=null)context.enableScissor((int)clip[0],(int)clip[1],(int)clip[2],(int)clip[3]);
        context.enableScissor(widget.getX() + 4, widget.getY(), widget.getX() + widget.getWidth() - 4, widget.getY() + widget.getHeight());
        try {
            int slot=GlassStyles.regular()?-1:(widget instanceof net.minecraft.client.gui.widget.TabButtonWidget tab && tab.isCurrentTab())?-2:net.auramc.glass.common.render.AdaptiveText.slot(widget);
            GlassGlyphText.draw(context,font,text,widget.getX()+(widget.getWidth()-textWidth)*.5f,widget.getY()+(widget.getHeight()-size)*.5f-1,size,color,!GlassStyles.regular(),slot);
        } finally {
            context.disableScissor();
            if(clip!=null)context.disableScissor();
        }
    }
    public static void lock(DrawContext context,ClickableWidget widget,boolean locked) {
        int color=button(context,widget,widget.isHovered(),1);
        NativeCommand command=command(context,1,color);if(command==null)return;
        command.x=widget.getX()+widget.getWidth()*.5f-3.5f;command.y=widget.getY()+widget.getHeight()*.5f-1;command.locked=locked;
        GlassRenderer.foreground(command);
    }

    public static boolean menuIcon(DrawContext context, ClickableWidget widget, boolean hovered, float alpha) {
        if (!AuraRenderer.isReady() || !(widget.getMessage().getContent() instanceof net.minecraft.text.TranslatableTextContent text)) return false;
        String key = text.getKey();
        boolean language = key.equals("narrator.button.language") || key.equals("options.language");
        boolean accessibility = key.equals("narrator.button.accessibility") || key.equals("options.accessibility")
                || key.equals("accessibility.onboarding.accessibility.button");
        if (!language && !accessibility) return false;
        button(context, widget, hovered, alpha);
        // Requirement 9: монохромные белые 0.92 как в 1.20.1, 16 GUI
        float size = 16.0f;
        float x = widget.getX() + (widget.getWidth() - size) * .5f;
        float y = widget.getY() + (widget.getHeight() - size) * .5f;
        int iconColor = 0xEBFFFFFF; // 255 * 0.92 = 235 (0xEB)
        NativeCommand command=command(context,language?2:3,iconColor);if(command==null)return false;
        command.size=size;command.x=x;command.y=y;GlassRenderer.foreground(command);return true;
    }
    private static final class NativeCommand implements Runnable {
        DrawContext context;int type,color;String text;float x,y,size;boolean locked;
        @Override public void run(){
            try{paint();}finally{context=null;text=null;}
        }
        private void paint(){
            if(!begin(context))return;
            if(type==0){AuraRenderer.text(text,x,y,size,color);AuraRenderer.flush();return;}
            if(type==1){
                AuraRenderer.rect(x,y,7,6,1,0,color,0);
                AuraRenderer.rect(x+1+(locked?0:2),y-4,5,6,2,1,0,color);AuraRenderer.flush();return;
            }
        if (type==2) {
            AuraRenderer.rect(x, y, size, size, size * .5f, 1, 0, color);
            AuraRenderer.rect(x + size * .28f, y, size * .44f, size, size * .22f, .8f, 0, color);
            AuraRenderer.rect(x, y + size * .47f, size, 1, 0, 0, color, 0);
        } else {
            AuraRenderer.rect(x + size * .38f, y, size * .24f, size * .24f, size * .12f, 0, color, 0);
            AuraRenderer.rect(x + size * .43f, y + size * .3f, size * .14f, size * .4f, 1, 0, color, 0);
            AuraRenderer.rect(x + size * .12f, y + size * .34f, size * .76f, size * .12f, 1, 0, color, 0);
            AuraRenderer.rect(x + size * .29f, y + size * .64f, size * .13f, size * .36f, 1, 0, color, 0);
            AuraRenderer.rect(x + size * .58f, y + size * .64f, size * .13f, size * .36f, 1, 0, color, 0);
        }
        AuraRenderer.flush();
        }
    }
}
