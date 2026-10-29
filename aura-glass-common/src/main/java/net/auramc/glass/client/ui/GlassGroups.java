package net.auramc.glass.client.ui;

import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.GlassPreset;
import net.auramc.glass.common.style.GlassStyles;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.screen.GameMenuScreen;
import net.minecraft.client.gui.Element;
import net.minecraft.client.gui.ParentElement;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.ClickableWidget;
import net.minecraft.client.gui.widget.EntryListWidget;
import net.minecraft.client.gui.widget.OptionListWidget;
import net.minecraft.client.gui.widget.PressableTextWidget;

public final class GlassGroups {
    private static final int MAX_WIDGETS = 1024;
    private static final int PAD = 8;
    private static final int GAP = 12;
    private static final ClickableWidget[] WIDGETS = new ClickableWidget[MAX_WIDGETS];
    private static final int[] LEFT = new int[MAX_WIDGETS];
    private static final int[] TOP = new int[MAX_WIDGETS];
    private static final int[] RIGHT = new int[MAX_WIDGETS];
    private static final int[] BOTTOM = new int[MAX_WIDGETS];
    private static final int[] PARENT = new int[MAX_WIDGETS];
    private static final Group[] GROUPS = new Group[128];
    private static ParentElement screen;
    private static int widgetCount;
    private static int groupCount;
    private static int width;
    private static int height;

    static {
        for (int i = 0; i < GROUPS.length; i++) GROUPS[i] = new Group();
    }

    private GlassGroups() {}

    public static void prepare(ParentElement root) {
        for (int i = 0; i < widgetCount; i++) WIDGETS[i] = null;
        widgetCount = 0;
        groupCount = 0;
        screen = null;
        MinecraftClient client = MinecraftClient.getInstance();
        if (root != client.currentScreen || !GlassStyles.regular()) return;
        width = client.getWindow().getScaledWidth();
        height = client.getWindow().getScaledHeight();
        screen = root;
        collect(root, 0,0,height);
        for (int i = 0; i < widgetCount; i++) {
            for (int j = 0; j < i; j++) {
                if (near(i, j)) PARENT[find(i)] = find(j);
            }
        }
        for (int i = 0; i < widgetCount; i++) {
            int rootIndex = find(i);
            Group group = null;
            for (int j = 0; j < groupCount; j++) {
                if (GROUPS[j].root == rootIndex) {
                    group = GROUPS[j];
                    break;
                }
            }
            if (group == null) {
                if (groupCount == GROUPS.length) continue;
                group = GROUPS[groupCount++];
                group.root = rootIndex;
                group.left = LEFT[i];
                group.top = TOP[i];
                group.right = RIGHT[i];
                group.bottom = BOTTOM[i];
            } else {
                group.left = Math.min(group.left, LEFT[i]);
                group.top = Math.min(group.top, TOP[i]);
                group.right = Math.max(group.right, RIGHT[i]);
                group.bottom = Math.max(group.bottom, BOTTOM[i]);
            }
        }
        for (int i = 0; i < groupCount; i++) {
            Group group = GROUPS[i];
            group.left = Math.max(0, group.left - PAD);
            group.top = Math.max(0, group.top - PAD);
            group.right = Math.min(width, group.right + PAD);
            group.bottom = Math.min(height, group.bottom + PAD);
        }
        // Padding can connect separate components; merge again to prevent stacked glass.
        boolean merged;
        do {
            merged = false;
            for (int i = 0; i < groupCount && !merged; i++) {
                Group a = GROUPS[i];
                for (int j = i + 1; j < groupCount; j++) {
                    Group b = GROUPS[j];
                    if (a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom) {
                        a.left = Math.min(a.left, b.left);
                        a.top = Math.min(a.top, b.top);
                        a.right = Math.max(a.right, b.right);
                        a.bottom = Math.max(a.bottom, b.bottom);
                        GROUPS[j] = GROUPS[--groupCount];
                        GROUPS[groupCount] = b;
                        merged = true;
                        break;
                    }
                }
            }
        } while (merged);
    }

    private static void collect(ParentElement root, int depth,int clipTop,int clipBottom) {
        if(root instanceof GlassListViewport list){clipTop=Math.max(clipTop,list.aura_glassTop());clipBottom=Math.min(clipBottom,list.aura_glassBottom());}
        if (depth > 32 || widgetCount == MAX_WIDGETS) return;
        if (depth == 0 && root instanceof GameMenuScreen) {
            ClickableWidget openToLan = null;
            ClickableWidget configBtn = null;
            for (var child : root.children()) {
                if (child instanceof ClickableWidget w) {
                    String cn = w.getClass().getName();
                    if (cn.contains("ConfigButton") || cn.contains("supplementaries")) {
                        configBtn = w;
                    } else if (w.getMessage() != null) {
                        String txt = w.getMessage().getString().toLowerCase();
                        if (txt.contains("open to lan") || txt.contains("открыть для сети") || txt.contains("share to lan")) {
                            openToLan = w;
                        }
                    }
                }
            }
            if (openToLan != null && configBtn != null && configBtn.getX() > openToLan.getX()) {
                openToLan.setWidth(74);
                configBtn.setX(openToLan.getX() + 74 + 4);
            }
        }
        var children = root.children();
        for (int i = 0; i < children.size(); i++) {
            Element child = children.get(i);
            if (child instanceof EntryListWidget<?> && !(child instanceof OptionListWidget)) continue;
            if (child instanceof ClickableWidget widget && !(child instanceof EntryListWidget<?>)) {
                if (!widget.visible || widget instanceof PressableTextWidget || widget instanceof net.minecraft.client.gui.widget.AbstractTextWidget) continue;
                int left = Math.max(0, widget.getX());
                int top = Math.max(clipTop, widget.getY());
                int right = Math.min(width, widget.getX() + widget.getWidth());
                int bottom = Math.min(clipBottom, widget.getY() + widget.getHeight());
                if (left < right && top < bottom && widgetCount < MAX_WIDGETS) {
                    int index = widgetCount++;
                    WIDGETS[index] = widget;
                    LEFT[index] = left;
                    TOP[index] = top;
                    RIGHT[index] = right;
                    BOTTOM[index] = bottom;
                    PARENT[index] = index;
                }
            }
            if (child instanceof ParentElement parent) collect(parent, depth + 1,clipTop,clipBottom);
        }
    }

    private static boolean near(int a, int b) {
        boolean overlapX = LEFT[a] <= RIGHT[b] && LEFT[b] <= RIGHT[a];
        boolean overlapY = TOP[a] <= BOTTOM[b] && TOP[b] <= BOTTOM[a];
        int gapX = Math.max(LEFT[a] - RIGHT[b], LEFT[b] - RIGHT[a]);
        int gapY = Math.max(TOP[a] - BOTTOM[b], TOP[b] - BOTTOM[a]);
        return (overlapX && gapY <= GAP) || (overlapY && gapX <= GAP);
    }

    private static int find(int index) {
        while (PARENT[index] != index) {
            PARENT[index] = PARENT[PARENT[index]];
            index = PARENT[index];
        }
        return index;
    }

    public static void draw(DrawContext context) {
        MinecraftClient client = MinecraftClient.getInstance();
        if (!GlassStyles.regular() || screen == null || screen != client.currentScreen) return;
        if (width != client.getWindow().getScaledWidth() || height != client.getWindow().getScaledHeight()) return;
        long now = System.nanoTime();
        for (int i = 0; i < groupCount; i++) {
            Group group = GROUPS[i];
            int w = group.right - group.left;
            int h = group.bottom - group.top;
            boolean large = w * (long) h >= 12000;

            float cx = (group.left + group.right) * 0.5f / width;
            float cy = (group.top + group.bottom) * 0.5f / height;
            float ambient = net.auramc.glass.common.render.BlurService.get().sampleAmbient(cx, cy);
            group.ambient = ambient;

            if (ambient > 0.60f) {
                group.isLight = true;
            } else if (ambient < 0.50f) {
                group.isLight = false;
            }

            if (group.lastTimeNanos == 0) {
                group.lastTimeNanos = now;
                group.lightProgress = group.isLight ? 1.0f : 0.0f;
            } else {
                float dt = Math.min(0.040f, (now - group.lastTimeNanos) / 1_000_000_000.0f);
                group.lastTimeNanos = now;
                float target = group.isLight ? 1.0f : 0.0f;
                float step = dt / 0.200f;
                if (group.lightProgress < target) {
                    group.lightProgress = Math.min(target, group.lightProgress + step);
                } else if (group.lightProgress > target) {
                    group.lightProgress = Math.max(target, group.lightProgress - step);
                }
            }

            var style = GlassStyles.frame(GlassPreset.PANEL);
            style.asSurface();
            style.radius = 12;
            style.bevel = large ? 3 : 2;
            style.refraction = large ? .30f : .20f;
            style.dispersion = .005f;
            style.depth = 0;
            style.wobble = 0;
            style.gloss = .16f;
            style.light = 0;
            style.rimOuter = .22f;
            style.rimInner = 0;
            style.frost = 2.5f;
            style.saturation = 1.9f;

            float t = group.lightProgress;
            style.tintR = (28f + (255f - 28f) * t) / 255.0f;
            style.tintG = (28f + (255f - 28f) * t) / 255.0f;
            style.tintB = (30f + (255f - 30f) * t) / 255.0f;
            style.tintA = 0.30f;

            GlassRenderer.drawSurface(group, group.left, group.top, w, h, style, 1);
        }
    }

    public static int textColorForGroup(Group group) {
        if (group == null) return 0xEBFFFFFF;
        float t = group.lightProgress;
        if (t < 0.50f) {
            float k = t <= 0.30f ? 0.0f : (t - 0.30f) / 0.20f;
            int alpha = Math.round(235 - 18 * k);
            return (alpha << 24) | 0x00FFFFFF;
        } else {
            float k = t >= 0.70f ? 1.0f : (t - 0.50f) / 0.20f;
            int alpha = Math.round(200 + 17 * k);
            return (alpha << 24);
        }
    }

    public static Group groupAt(float x, float y) {
        if (!GlassStyles.regular() || screen == null) return null;
        for (int i = 0; i < groupCount; i++) {
            Group g = GROUPS[i];
            if (x >= g.left && x <= g.right && y >= g.top && y <= g.bottom) return g;
        }
        return null;
    }

    public static boolean insideAnyGroup(float x, float y) {
        return groupAt(x, y) != null;
    }

    public static Group groupForWidget(ClickableWidget widget) {
        if (!GlassStyles.regular() || screen == null || widget == null) return null;
        for (int i = 0; i < widgetCount; i++) {
            if (WIDGETS[i] == widget) {
                int root = find(i);
                for (int j = 0; j < groupCount; j++) {
                    if (GROUPS[j].root == root) return GROUPS[j];
                }
            }
        }
        return groupAt(widget.getX(), widget.getY());
    }

    public static int count() { return groupCount; }
    public static int left(int index) { return GROUPS[index].left; }
    public static int right(int index) { return GROUPS[index].right; }
    public static int top(int index) { return GROUPS[index].top; }
    public static int bottom(int index) { return GROUPS[index].bottom; }
    public static Group get(int index) { return index >= 0 && index < groupCount ? GROUPS[index] : null; }
    public static float ambient(int index) { return index >= 0 && index < groupCount ? GROUPS[index].ambient : 0.2f; }
    public static float lightProgress(int index) { return index >= 0 && index < groupCount ? GROUPS[index].lightProgress : 0f; }
    public static boolean isLight(int index) { return index >= 0 && index < groupCount && GROUPS[index].isLight; }

    public static boolean owns(Object widget) {
        if (screen != MinecraftClient.getInstance().currentScreen) return false;
        for (int i = 0; i < widgetCount; i++) if (WIDGETS[i] == widget) return true;
        return false;
    }

    public static final class Group {
        int root;
        int left;
        int top;
        int right;
        int bottom;
        public boolean isLight;
        public float lightProgress;
        public long lastTimeNanos;
        public float ambient = 0.2f;
    }
}
