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
    private static final int GAP = 28;
    private static final int MARGIN = 6;
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
    private static int lastChildrenCount = -1;

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
        lastChildrenCount = root != null ? root.children().size() : 0;
        alignOptionsDoneButton(root);
        collect(root, 0, 0, height);
        layoutPreparedWidgets(hasScreenTitle(root), hasTabs(root), isAlertOrExperimentalScreen(root));
    }

    public static void computeLayoutForTest(int scrWidth, int scrHeight, ClickableWidget[] widgets, int count,
                                           boolean hasTitle, boolean hasTabs, boolean isAlert, boolean isMainOptions) {
        for (int i = 0; i < widgetCount; i++) WIDGETS[i] = null;
        widgetCount = 0;
        groupCount = 0;
        screen = null;
        width = scrWidth;
        height = scrHeight;

        if (isMainOptions) {
            ClickableWidget doneBtn = null;
            int maxOptionY = 0;
            for (int i = 0; i < count; i++) {
                ClickableWidget w = widgets[i];
                if (w != null && w.visible) {
                    if (isDoneButton(w)) {
                        doneBtn = w;
                    } else {
                        maxOptionY = Math.max(maxOptionY, w.getY() + w.getHeight());
                    }
                }
            }
            if (doneBtn != null && maxOptionY > 0 && doneBtn.getY() > maxOptionY + 16) {
                doneBtn.setY(maxOptionY + 12);
                doneBtn.setX((width - doneBtn.getWidth()) / 2);
            }
        }

        if (isAlert) {
            int textW = Math.min(320, width - 40);
            int textLeft = (width - textW) / 2;
            int textRight = (width + textW) / 2;
            int textTop = 60;
            int textBottom = 115;
            if (widgetCount < MAX_WIDGETS) {
                int index = widgetCount++;
                WIDGETS[index] = null;
                LEFT[index] = textLeft;
                TOP[index] = textTop;
                RIGHT[index] = textRight;
                BOTTOM[index] = textBottom;
                PARENT[index] = index;
            }
        }

        for (int i = 0; i < count; i++) {
            ClickableWidget w = widgets[i];
            if (w == null || !w.visible) continue;
            if (w instanceof net.minecraft.client.gui.widget.TabButtonWidget) {
                w.setY(6);
            }
            int index = widgetCount++;
            WIDGETS[index] = w;
            LEFT[index] = w.getX();
            TOP[index] = w.getY();
            RIGHT[index] = w.getX() + w.getWidth();
            BOTTOM[index] = w.getY() + w.getHeight();
            PARENT[index] = index;
        }

        layoutPreparedWidgets(hasTitle, hasTabs, isAlert);
    }

    private static void layoutPreparedWidgets(boolean hasTitle, boolean hasTabs, boolean isAlert) {
        if (isAlert) {
            for (int i = 1; i < widgetCount; i++) {
                PARENT[find(i)] = find(0);
            }
        } else {
            for (int i = 0; i < widgetCount; i++) {
                for (int j = 0; j < i; j++) {
                    if (near(i, j)) PARENT[find(i)] = find(j);
                }
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

        // Determine if group is tab group
        for (int i = 0; i < groupCount; i++) {
            Group group = GROUPS[i];
            group.isTabGroup = false;
            for (int k = 0; k < widgetCount; k++) {
                if (find(k) == group.root && WIDGETS[k] instanceof net.minecraft.client.gui.widget.TabButtonWidget) {
                    group.isTabGroup = true;
                    break;
                }
            }
        }

        // Requirement 1, 2, 4, 8: Margin >= 6, PAD = 8, bottom button alignment, widget block shifting
        for (int i = 0; i < groupCount; i++) {
            Group group = GROUPS[i];
            if (group.isTabGroup) {
                // Requirement 4: TabNavigationWidget capsule top at y=6, height 24 (bottom 30), radius 12
                group.top = 6;
                group.bottom = 30;
                group.left = Math.max(MARGIN, group.left);
                group.right = Math.min(width - MARGIN, group.right);
                continue;
            }

            int gLeft = group.left - PAD;
            int gRight = group.right + PAD;
            int gTop = group.top - PAD;
            int gBottom = group.bottom + PAD;

            // Requirement 8: bottom button group низ группы на 8 GUI выше нижнего края экрана
            if (gBottom >= height - 64 && group.bottom >= height - 52) {
                int targetBottom = height - 8;
                int shiftBottomY = targetBottom - gBottom;
                if (shiftBottomY != 0) {
                    for (int k = 0; k < widgetCount; k++) {
                        if (find(k) == group.root) {
                            ClickableWidget w = WIDGETS[k];
                            if (w != null) w.setY(w.getY() + shiftBottomY);
                            TOP[k] += shiftBottomY;
                            BOTTOM[k] += shiftBottomY;
                        }
                    }
                    gTop += shiftBottomY;
                    gBottom = targetBottom;
                }
            }

            // Requirement 1: Минимальный отступ группы от любого края экрана 6 GUI;
            // если группа не влезает — сдвинуть сам блок виджетов (виджеты + хитбоксы вместе), а не обрезать стекло.
            int shiftX = 0;
            int shiftY = 0;
            if (gLeft < MARGIN) {
                shiftX = MARGIN - gLeft;
            } else if (gRight > width - MARGIN) {
                shiftX = (width - MARGIN) - gRight;
            }
            if (gTop < MARGIN) {
                shiftY = MARGIN - gTop;
            } else if (gBottom > height - MARGIN) {
                shiftY = (height - MARGIN) - gBottom;
            }

            // Return 1, point 1: Title clearance >= 10 GUI above group.
            // Title is at y=8, h=13.5 (bottom=21.5). Minimum group top must be >= 32 GUI.
            if (hasTitle && gTop < 32 && !isAlert) {
                int shiftTitle = 32 - gTop;
                shiftY = Math.max(shiftY, shiftTitle);
            }
            if (hasTabs && gTop < 38) {
                int shiftTabs = 38 - gTop;
                shiftY = Math.max(shiftY, shiftTabs);
            }

            if (shiftX != 0 || shiftY != 0) {
                for (int k = 0; k < widgetCount; k++) {
                    if (find(k) == group.root) {
                        ClickableWidget w = WIDGETS[k];
                        if (w != null) {
                            w.setX(w.getX() + shiftX);
                            w.setY(w.getY() + shiftY);
                        }
                        LEFT[k] += shiftX;
                        RIGHT[k] += shiftX;
                        TOP[k] += shiftY;
                        BOTTOM[k] += shiftY;
                    }
                }
                gLeft += shiftX;
                gRight += shiftX;
                gTop += shiftY;
                gBottom += shiftY;
            }

            group.left = gLeft;
            group.top = gTop;
            group.right = gRight;
            group.bottom = gBottom;
        }

        // Requirement 2: зазор между группами минимум 8 GUI.
        boolean merged;
        do {
            merged = false;
            for (int i = 0; i < groupCount && !merged; i++) {
                Group a = GROUPS[i];
                for (int j = i + 1; j < groupCount; j++) {
                    Group b = GROUPS[j];
                    if (a.isTabGroup || b.isTabGroup) continue;
                    if (a.left < b.right + 8 && b.left < a.right + 8 && a.top < b.bottom + 8 && b.top < a.bottom + 8) {
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

    private static void collect(ParentElement root, int depth, int clipTop, int clipBottom) {
        if (root instanceof GlassListViewport list) {
            clipTop = Math.max(clipTop, list.aura_glassTop());
            clipBottom = Math.min(clipBottom, list.aura_glassBottom());
        }
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

        // Requirement 6: Notice / Confirm / Experimental screens: text + buttons in ONE group
        if (depth == 0 && isAlertOrExperimentalScreen(root)) {
            int textW = Math.min(320, width - 40);
            int textLeft = (width - textW) / 2;
            int textRight = (width + textW) / 2;
            int textTop = 60;
            int textBottom = 115;
            if (widgetCount < MAX_WIDGETS) {
                int index = widgetCount++;
                WIDGETS[index] = null;
                LEFT[index] = textLeft;
                TOP[index] = textTop;
                RIGHT[index] = textRight;
                BOTTOM[index] = textBottom;
                PARENT[index] = index;
            }
        }

        for (int i = 0; i < children.size(); i++) {
            Element child = children.get(i);
            if (child instanceof EntryListWidget<?> && !(child instanceof OptionListWidget)) continue;

            // Requirement 6: Длинный текст вне кнопок (экран «Экспериментальные настройки», предупреждения, ошибки)
            if (child instanceof net.minecraft.client.gui.widget.MultilineTextWidget textWidget) {
                if (textWidget.visible && isAlertOrExperimentalScreen(root)) {
                    int left = Math.max(MARGIN, textWidget.getX());
                    int top = Math.max(clipTop, textWidget.getY());
                    int right = Math.min(width - MARGIN, textWidget.getX() + Math.min(320, textWidget.getWidth()));
                    int bottom = Math.min(clipBottom, textWidget.getY() + textWidget.getHeight());
                    if (left < right && top < bottom && widgetCount < MAX_WIDGETS) {
                        int index = widgetCount++;
                        WIDGETS[index] = textWidget;
                        LEFT[index] = left;
                        TOP[index] = top;
                        RIGHT[index] = right;
                        BOTTOM[index] = bottom;
                        PARENT[index] = index;
                    }
                }
            }

            if (child instanceof ClickableWidget widget && !(child instanceof EntryListWidget<?>)) {
                if (!widget.visible || widget instanceof PressableTextWidget || widget instanceof net.minecraft.client.gui.widget.AbstractTextWidget) continue;

                if (widget instanceof net.minecraft.client.gui.widget.TabButtonWidget) {
                    widget.setY(6);
                }

                int left = widget.getX();
                int top = Math.max(clipTop, widget.getY());
                int right = widget.getX() + widget.getWidth();
                int bottom = Math.min(clipBottom, widget.getY() + widget.getHeight());

                // Requirement 3: Подписи над полями («Название мира», «Ключ для генератора мира»)
                // подпись входит В группу своего поля — верх группы = верх подписи − 8 GUI.
                // отступ подпись→поле 4 GUI.
                if (widget instanceof net.minecraft.client.gui.widget.TextFieldWidget) {
                    net.minecraft.client.gui.widget.AbstractTextWidget fieldLabel = findFieldLabelAbove(root, widget);
                    if (fieldLabel != null && fieldLabel.visible) {
                        int labelH = fieldLabel.getHeight() > 0 ? fieldLabel.getHeight() : 9;
                        int desiredLabelY = widget.getY() - 4 - labelH;
                        fieldLabel.setY(desiredLabelY);
                        top = Math.max(clipTop, desiredLabelY);
                        GlassLabels.markFieldLabel(fieldLabel);
                    }
                }

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
            if (child instanceof ParentElement parent) collect(parent, depth + 1, clipTop, clipBottom);
        }
    }

    private static net.minecraft.client.gui.widget.AbstractTextWidget findFieldLabelAbove(ParentElement parent, ClickableWidget field) {
        var children = parent.children();
        for (int i = 0; i < children.size(); i++) {
            Element c = children.get(i);
            if (c instanceof net.minecraft.client.gui.widget.AbstractTextWidget textWidget) {
                if (textWidget.getY() < field.getY() && textWidget.getY() >= field.getY() - 32
                        && textWidget.getX() >= field.getX() - 16 && textWidget.getX() <= field.getX() + field.getWidth()) {
                    return textWidget;
                }
            }
        }
        return null;
    }

    public static boolean isDoneButton(ClickableWidget w) {
        if (w == null || w.getMessage() == null) return false;
        String str = w.getMessage().getString();
        if ("Готово".equals(str) || "Done".equalsIgnoreCase(str)) return true;
        if (w.getMessage().getContent() instanceof net.minecraft.text.TranslatableTextContent t) {
            return "gui.done".equals(t.getKey());
        }
        return false;
    }

    public static boolean isMainOptionsScreen(ParentElement root) {
        if (root == null) return false;
        String name = root.getClass().getName();
        return name.endsWith(".OptionsScreen") || name.endsWith(".class_429");
    }

    private static void alignOptionsDoneButton(ParentElement root) {
        if (!isMainOptionsScreen(root)) return;
        var children = root.children();
        ClickableWidget doneBtn = null;
        int maxOptionY = 0;
        for (int i = 0; i < children.size(); i++) {
            Element c = children.get(i);
            if (c instanceof ClickableWidget w && w.visible) {
                if (isDoneButton(w)) {
                    doneBtn = w;
                } else if (!(c instanceof EntryListWidget<?>)) {
                    maxOptionY = Math.max(maxOptionY, w.getY() + w.getHeight());
                }
            }
        }
        if (doneBtn != null && maxOptionY > 0 && doneBtn.getY() > maxOptionY + 16) {
            doneBtn.setY(maxOptionY + 12);
            doneBtn.setX((width - doneBtn.getWidth()) / 2);
        }
    }

    private static boolean isAlertOrExperimentalScreen(ParentElement root) {
        if (root == null) return false;
        String name = root.getClass().getName();
        return name.contains("Experimental") || name.contains("Warning") || name.contains("Notice")
                || name.contains("Backup") || name.contains("Confirm")
                || name.endsWith(".class_403") || name.endsWith(".class_410");
    }

    private static boolean hasScreenTitle(ParentElement root) {
        if (root == null || root instanceof net.minecraft.client.gui.screen.TitleScreen) return false;
        if (root instanceof net.minecraft.client.gui.screen.Screen s) {
            return s.getTitle() != null && !s.getTitle().getString().isEmpty();
        }
        return false;
    }

    private static boolean hasTabs(ParentElement root) {
        if (root == null) return false;
        var children = root.children();
        for (int i = 0; i < children.size(); i++) {
            if (children.get(i) instanceof net.minecraft.client.gui.widget.TabButtonWidget
                    || children.get(i) instanceof net.minecraft.client.gui.widget.TabNavigationWidget) {
                return true;
            }
        }
        return false;
    }

    public static int width() { return width; }
    public static int height() { return height; }

    public static int topGroupY() {
        int minTop = height;
        for (int i = 0; i < groupCount; i++) {
            if (!GROUPS[i].isTabGroup && GROUPS[i].top < minTop) {
                minTop = GROUPS[i].top;
            }
        }
        return (minTop == height || minTop > height / 2) ? 40 : minTop;
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
        if (!GlassStyles.regular() || client.currentScreen == null) return;
        int currentChildren = client.currentScreen.children().size();
        if (screen != client.currentScreen || (currentChildren > 0 && currentChildren != lastChildrenCount)) {
            prepare(client.currentScreen);
        }
        if (screen != client.currentScreen) return;
        if (width != client.getWindow().getScaledWidth() || height != client.getWindow().getScaledHeight()) return;
        long now = System.nanoTime();
        for (int i = 0; i < groupCount; i++) {
            Group group = GROUPS[i];
            int w = group.right - group.left;
            int h = group.bottom - group.top;
            if (w <= 0 || h <= 0) continue;
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
            style.radius = group.isTabGroup ? 12 : 14;
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

            if (group.isTabGroup) {
                continue;
            }
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
    public static float lightProgress() { return 0.0f; }
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
        boolean isTabGroup;
        public boolean isLight;
        public float lightProgress;
        public long lastTimeNanos;
        public float ambient = 0.2f;
    }
}
