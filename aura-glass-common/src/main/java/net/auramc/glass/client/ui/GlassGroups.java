package net.auramc.glass.client.ui;

import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.GlassPreset;
import net.auramc.glass.common.style.GlassStyles;
import net.minecraft.client.MinecraftClient;
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
        collect(root, 0, 0, height);
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
                            w.setY(w.getY() + shiftBottomY);
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

            if (shiftX != 0 || shiftY != 0) {
                for (int k = 0; k < widgetCount; k++) {
                    if (find(k) == group.root) {
                        ClickableWidget w = WIDGETS[k];
                        w.setX(w.getX() + shiftX);
                        w.setY(w.getY() + shiftY);
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

            group.left = Math.max(MARGIN, gLeft);
            group.top = Math.max(MARGIN, gTop);
            group.right = Math.min(width - MARGIN, gRight);
            group.bottom = Math.min(height - MARGIN, gBottom);
        }

        // Requirement 2: зазор между группами минимум 8 GUI.
        boolean merged;
        do {
            merged = false;
            for (int i = 0; i < groupCount && !merged; i++) {
                Group a = GROUPS[i];
                for (int j = i + 1; j < groupCount; j++) {
                    Group b = GROUPS[j];
                    if (a.left < b.right + 8 && b.left < a.right + 8 && a.top < b.bottom + 8 && b.top < a.bottom + 8) {
                        a.left = Math.min(a.left, b.left);
                        a.top = Math.min(a.top, b.top);
                        a.right = Math.max(a.right, b.right);
                        a.bottom = Math.max(a.bottom, b.bottom);
                        if (b.isTabGroup) a.isTabGroup = true;
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
        var children = root.children();

        // Requirement 8: 1.21.1 «Настройки»: «Готово» внутри основной группы последним рядом
        if (root instanceof net.minecraft.client.gui.screen.option.OptionsScreen) {
            alignOptionsDoneButton(root);
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

                int left = Math.max(0, widget.getX());
                int top = Math.max(clipTop, widget.getY());
                int right = Math.min(width, widget.getX() + widget.getWidth());
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

    private static void alignOptionsDoneButton(ParentElement root) {
        var children = root.children();
        ClickableWidget doneBtn = null;
        int maxOptionY = 0;
        for (int i = 0; i < children.size(); i++) {
            Element c = children.get(i);
            if (c instanceof ClickableWidget w && w.visible) {
                if (w.getMessage() != null && w.getMessage().getContent() instanceof net.minecraft.text.TranslatableTextContent t
                        && "gui.done".equals(t.getKey())) {
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
                || name.contains("Backup") || name.contains("Confirm");
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
        return minTop == height ? 40 : minTop;
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
        for (int i = 0; i < groupCount; i++) {
            Group group = GROUPS[i];
            int w = group.right - group.left;
            int h = group.bottom - group.top;
            if (w <= 0 || h <= 0) continue;
            boolean large = w * (long) h >= 12000;
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
            style.frost = 1.5f;
            GlassRenderer.drawSurface(group, group.left, group.top, w, h, style, 1);
        }
    }

    public static int count() { return groupCount; }
    public static int left(int index) { return GROUPS[index].left; }
    public static int right(int index) { return GROUPS[index].right; }
    public static int top(int index) { return GROUPS[index].top; }
    public static int bottom(int index) { return GROUPS[index].bottom; }

    public static boolean owns(Object widget) {
        if (screen != MinecraftClient.getInstance().currentScreen) return false;
        for (int i = 0; i < widgetCount; i++) if (WIDGETS[i] == widget) return true;
        return false;
    }

    private static final class Group {
        int root;
        int left;
        int top;
        int right;
        int bottom;
        boolean isTabGroup;
    }
}
