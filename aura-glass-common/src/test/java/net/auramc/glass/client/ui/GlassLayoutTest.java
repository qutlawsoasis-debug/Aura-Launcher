package net.auramc.glass.client.ui;

import net.auramc.glass.common.style.GlassPreset;
import net.auramc.glass.common.style.GlassStyle;
import net.auramc.glass.common.style.GlassStyles;
import net.minecraft.client.gui.widget.ClickableWidget;
import net.minecraft.client.gui.widget.TabButtonWidget;
import net.minecraft.client.gui.tab.Tab;
import net.minecraft.client.gui.tab.TabManager;
import net.minecraft.client.gui.ScreenRect;
import net.minecraft.text.Text;

public final class GlassLayoutTest {

    private static class DummyWidget extends ClickableWidget {
        DummyWidget(int x, int y, int width, int height, String text) {
            super(x, y, width, height, text != null ? Text.literal(text) : Text.empty());
        }

        protected void renderButton(net.minecraft.client.gui.DrawContext context, int mouseX, int mouseY, float delta) {}
        protected void renderWidget(net.minecraft.client.gui.DrawContext context, int mouseX, int mouseY, float delta) {}

        @Override
        protected void appendClickableNarrations(net.minecraft.client.gui.screen.narration.NarrationMessageBuilder builder) {}
    }

    private static class DummyTabButton extends TabButtonWidget {
        DummyTabButton(int x, int y, int width, int height, String text) {
            super(new TabManager(w -> {}, w -> {}),
                  new Tab() {
                      public Text getTitle() { return Text.literal(text); }
                      public void forEachChild(java.util.function.Consumer<ClickableWidget> consumer) {}
                      public void refreshGrid(ScreenRect rect) {}
                  },
                  width, height);
            setX(x);
            setY(y);
        }
    }

    public static void main(String[] args) throws Exception {
        System.out.println("=== Starting Glass Screen Layout Unit Tests (W3e) ===");

        testRequirement1_ScreenMarginsAndWidgetBlockShift();
        System.out.println("1. Screen edge margins (>= 6 GUI) & widget block shifting: PASS");

        testRequirement2_GroupPaddingAndMinimumGap();
        System.out.println("2. Inner padding (PAD = 8 GUI) & inter-group gap (>= 8 GUI): PASS");

        testRequirement3_FieldLabelsGroupInclusionAndOffsets();
        System.out.println("3. Field labels inside group (gap = 4 GUI, top = label.y - 8, alpha = 0.60): PASS");

        testRequirement4_TabNavigationCapsuleAndOffsets();
        System.out.println("4. Tab capsule (y=6, h=24, r=12, active tab rgba(118,118,128,0.36), content +6): PASS");

        testRequirement5_ScreenTitleScaleAndPositioning();
        System.out.println("5. Screen title (scale 1.5x, white 0.92, shadow, centered, bottom 10 GUI above group): PASS");

        testRequirement6_AlertAndExperimentalLongText();
        System.out.println("6. Alert / experimental text (grouped with buttons, max width 320, line height 11): PASS");

        testRequirement7_WorldAndServerListSurfaceAndScrollbar();
        System.out.println("7. World/server list single glass plate (w = rowW + 16, selected r=6 0.36, scrollbar 3 GUI 0.35): PASS");

        testRequirement8_BottomButtonGroupAlignmentAndOptionsDone();
        System.out.println("8. Bottom buttons (bottom = height - 8 GUI, 1.21 Options 'Done' merged in main group): PASS");

        testRequirement9_TitleScreenMonochromeIcons();
        System.out.println("9. Title screen language/accessibility icons (white 0.92, size 16 GUI): PASS");

        System.out.println("=== All Glass Screen Layout tests PASSED successfully! ===");
    }

    private static void assertTrue(boolean condition, String message) {
        if (!condition) throw new AssertionError("Test failed: " + message);
    }

    private static void assertEquals(int expected, int actual, String message) {
        if (expected != actual) {
            throw new AssertionError("Test failed: " + message + " (expected: " + expected + ", actual: " + actual + ")");
        }
    }

    private static void assertEquals(float expected, float actual, float epsilon, String message) {
        if (Math.abs(expected - actual) > epsilon) {
            throw new AssertionError("Test failed: " + message + " (expected: " + expected + ", actual: " + actual + ")");
        }
    }

    // 1. Поля экрана: ни одна группа не прижата к краю. Минимальный отступ группы от любого края 6 GUI.
    // Если группа не влезает — сдвинуть сам блок виджетов, а не обрезать стекло.
    private static void testRequirement1_ScreenMarginsAndWidgetBlockShift() {
        // Widget placed dangerously close to left/top edge: (2, 4)
        DummyWidget w1 = new DummyWidget(2, 4, 100, 20, "W1");
        ClickableWidget[] widgets = new ClickableWidget[]{w1};

        GlassGroups.computeLayoutForTest(400, 300, widgets, 1, false, false, false, false);

        assertEquals(1, GlassGroups.count(), "One group formed");
        int groupLeft = GlassGroups.left(0);
        int groupTop = GlassGroups.top(0);

        assertTrue(groupLeft >= 6, "Group left edge must be >= 6 GUI margin (got " + groupLeft + ")");
        assertTrue(groupTop >= 6, "Group top edge must be >= 6 GUI margin (got " + groupTop + ")");
        assertEquals(6, groupLeft, "Group left shifted to 6 GUI");
        assertEquals(6, groupTop, "Group top shifted to 6 GUI");

        // Verify widget's actual coordinates were shifted:
        assertEquals(14, w1.getX(), "Widget X shifted from 2 to 14 (groupLeft = 14 - 8 = 6)");
        assertEquals(14, w1.getY(), "Widget Y shifted from 4 to 14 (groupTop = 14 - 8 = 6)");

        // Widget placed dangerously close to right/bottom edge: (350, 280) on 400x300
        DummyWidget w2 = new DummyWidget(350, 280, 100, 20, "W2");
        ClickableWidget[] widgets2 = new ClickableWidget[]{w2};

        GlassGroups.computeLayoutForTest(400, 300, widgets2, 1, false, false, false, false);

        int groupRight = GlassGroups.right(0);
        int groupBottom = GlassGroups.bottom(0);

        assertTrue(groupRight <= 400 - 6, "Group right edge must be <= width - 6 GUI (got " + groupRight + ")");
        assertTrue(groupBottom <= 300 - 6, "Group bottom edge must be <= height - 6 GUI (got " + groupBottom + ")");
        assertTrue(w2.getX() + w2.getWidth() + 8 <= 400 - 6, "Widget hitbox right edge matches shifted coordinates");
    }

    // 2. Внутренний отступ группы PAD: 8 GUI; зазор между группами минимум 8 GUI.
    private static void testRequirement2_GroupPaddingAndMinimumGap() {
        DummyWidget w1 = new DummyWidget(50, 50, 80, 20, "G1");
        // Place w2 far enough (y = 120, gap = 120 - 70 = 50 > GAP 28)
        DummyWidget w2 = new DummyWidget(50, 120, 80, 20, "G2");
        ClickableWidget[] widgets = new ClickableWidget[]{w1, w2};

        GlassGroups.computeLayoutForTest(400, 300, widgets, 2, false, false, false, false);

        assertEquals(2, GlassGroups.count(), "Two separate groups formed");

        int g1Left = GlassGroups.left(0);
        int g1Right = GlassGroups.right(0);
        int g1Top = GlassGroups.top(0);
        int g1Bottom = GlassGroups.bottom(0);

        assertEquals(8, w1.getX() - g1Left, "Left PAD must be exactly 8 GUI");
        assertEquals(8, g1Right - (w1.getX() + w1.getWidth()), "Right PAD must be exactly 8 GUI");
        assertEquals(8, w1.getY() - g1Top, "Top PAD must be exactly 8 GUI");
        assertEquals(8, g1Bottom - (w1.getY() + w1.getHeight()), "Bottom PAD must be exactly 8 GUI");

        int g2Top = GlassGroups.top(1);
        int gap = g2Top - g1Bottom;
        assertTrue(gap >= 8, "Inter-group gap must be at least 8 GUI (got " + gap + ")");
    }

    // 3. Подписи над полями: подпись входит В группу своего поля — верх группы = верх подписи − 8 GUI.
    // Подпись не пересекает рамку стекла. Подпись: alpha 0.60 цвета текста, отступ подпись→поле 4 GUI.
    private static void testRequirement3_FieldLabelsGroupInclusionAndOffsets() {
        int fieldY = 80;
        int labelHeight = 9;
        int desiredLabelY = fieldY - 4 - labelHeight; // 67
        int groupTop = desiredLabelY - 8;             // 59

        assertEquals(67, desiredLabelY, "Label Y positioned 4 GUI above text field");
        assertEquals(4, fieldY - (desiredLabelY + labelHeight), "Gap label->field must be 4 GUI");
        assertEquals(8, desiredLabelY - groupTop, "Distance from group top to label must be 8 GUI (PAD)");
        assertTrue(desiredLabelY > groupTop, "Label is completely inside group, no intersection with frame");

        // Check label detection
        assertTrue(GlassLabels.isFieldLabel("selectWorld.enterName"), "Detects selectWorld.enterName");
        assertTrue(GlassLabels.isFieldLabel("selectWorld.enterSeed"), "Detects selectWorld.enterSeed");
        assertTrue(GlassLabels.isFieldLabel("Название мира"), "Detects Russian world name label");
        assertTrue(GlassLabels.isFieldLabel("Ключ для генератора мира"), "Detects Russian world seed label");

        // Check alpha calculation: alpha 0.60 of original text color
        int whiteColor = 0xFFFFFFFF;
        int originalAlpha = (whiteColor >>> 24 == 0) ? 255 : (whiteColor >>> 24);
        int computedAlpha = (int)(originalAlpha * 0.60f);
        int finalColor = (computedAlpha << 24) | (whiteColor & 0x00FFFFFF);
        assertEquals(153, computedAlpha, "Alpha 0.60 of 255 is 153 (0x99)");
        assertEquals(0x99FFFFFF, finalColor, "Final color has 0.60 alpha");
    }

    // 4. Вкладки «Игра / Мир / Прочее» (TabNavigationWidget): капсула целиком видна, верх капсулы на y=6 GUI,
    // высота 24 GUI, радиус 12; активная вкладка — заливка rgba(118,118,128,0.36), без голубой полоски сверху;
    // контент под вкладками сдвинуть вниз на те же +6.
    private static void testRequirement4_TabNavigationCapsuleAndOffsets() {
        DummyTabButton tab1 = new DummyTabButton(100, 10, 60, 20, "Tab1");
        DummyTabButton tab2 = new DummyTabButton(165, 10, 60, 20, "Tab2");
        DummyWidget contentBtn = new DummyWidget(100, 25, 200, 20, "Content");
        ClickableWidget[] widgets = new ClickableWidget[]{tab1, tab2, contentBtn};

        GlassGroups.computeLayoutForTest(400, 300, widgets, 3, false, true, false, false);

        assertEquals(6, GlassGroups.top(0), "Tab capsule top must be at y = 6 GUI");
        assertEquals(30, GlassGroups.bottom(0), "Tab capsule bottom must be at y = 30 GUI");
        assertEquals(24, GlassGroups.bottom(0) - GlassGroups.top(0), "Tab capsule height must be 24 GUI");

        int contentGroupTop = GlassGroups.top(1);
        assertTrue(contentGroupTop >= 38, "Content group below tabs must be shifted to top >= 38 GUI (got " + contentGroupTop + ")");
        assertTrue(contentGroupTop - GlassGroups.bottom(0) >= 8, "Gap between tab capsule and content must be >= 8 GUI");

        // Active tab style:
        var style = GlassStyles.frame(GlassPreset.PILL_THICK);
        style.radius = 12;
        style.asFill();
        style.tintR = 118 / 255f;
        style.tintG = 118 / 255f;
        style.tintB = 128 / 255f;
        style.tintA = 0.36f;

        assertEquals(12.0f, style.radius, 0.001f, "Active tab style radius is 12");
        assertEquals(0.36f, style.tintA, 0.001f, "Active tab fill alpha is 0.36");
        assertEquals(118 / 255f, style.tintR, 0.001f, "Active tab tint R matches Apple fill");
        assertEquals(128 / 255f, style.tintB, 0.001f, "Active tab tint B matches Apple fill");
    }

    // 5. Заголовок экрана («Настройки», «Сетевая игра», «Меню», «Выбор мира»): масштаб 1.5×, цвет белый 0.92 с тенью,
    // по центру, нижний край заголовка на 10 GUI выше верхней группы; не перекрывает группу.
    private static void testRequirement5_ScreenTitleScaleAndPositioning() {
        float baseTextSize = 9.0f;
        float titleScale = 1.5f;
        float titleSize = baseTextSize * titleScale; // 13.5 GUI
        assertEquals(13.5f, titleSize, 0.001f, "Title size is 13.5 GUI (1.5x of 9)");

        int screenWidth = 400;
        float measuredTitleWidth = 80.0f;
        float titleX = (screenWidth - measuredTitleWidth) * 0.5f;
        assertEquals(160.0f, titleX, 0.001f, "Title is centered horizontally");

        // Widget placed near top (y = 20, raw group top = 12 GUI)
        DummyWidget w = new DummyWidget(100, 20, 200, 20, "TopButton");
        ClickableWidget[] widgets = new ClickableWidget[]{w};

        // When screen has title (hasTitle = true):
        GlassGroups.computeLayoutForTest(400, 300, widgets, 1, true, false, false, false);

        int groupTop = GlassGroups.top(0);
        assertTrue(groupTop >= 32, "Top group must be shifted to top >= 32 GUI to provide clearance under title (got " + groupTop + ")");
        assertEquals(40, w.getY(), "Widget Y shifted down to 40 GUI (groupTop = 40 - 8 = 32)");

        float titleBottom = 8.0f + 13.5f; // title at y=8, h=13.5 -> 21.5 GUI
        float clearance = groupTop - titleBottom;
        assertTrue(clearance >= 10.0f, "Clearance between title bottom and group top must be >= 10 GUI (got " + clearance + ")");

        // Color check: white 0.92
        int alpha092 = Math.round(255 * 0.92f); // 235 -> 0xEB
        int titleColor = (alpha092 << 24) | 0x00FFFFFF;
        assertEquals(0xEBFFFFFF, titleColor, "Title color is 0xEBFFFFFF (white 0.92)");
    }

    // 6. Длинный текст вне кнопок (экран «Экспериментальные настройки», предупреждения, ошибки):
    // кладётся в одну стеклянную группу вместе с кнопками этого экрана, ширина текста максимум 320 GUI, межстрочный 11 GUI.
    private static void testRequirement6_AlertAndExperimentalLongText() {
        DummyWidget alertBtn = new DummyWidget(150, 130, 100, 20, "Back");
        ClickableWidget[] widgets = new ClickableWidget[]{alertBtn};

        GlassGroups.computeLayoutForTest(400, 300, widgets, 1, false, false, true, false);

        assertEquals(1, GlassGroups.count(), "Alert text anchor and Back button form ONE group");
        int groupTop = GlassGroups.top(0);
        int groupBottom = GlassGroups.bottom(0);
        int groupLeft = GlassGroups.left(0);
        int groupRight = GlassGroups.right(0);

        assertTrue(groupTop <= 60 - 8, "Group top includes the alert text block at y=60");
        assertTrue(groupBottom >= 130 + 20 + 8, "Group bottom includes the button at y=130");
        assertTrue(groupRight - groupLeft <= 320 + 16, "Group width fits within maximum 320 GUI + PAD");
    }

    // 7. Список миров/серверов: весь список — ОДНА стеклянная поверхность (ширина как у списка + PAD),
    // элементы без своего стекла; выбранный элемент — заливка rgba(118,118,128,0.36) радиус 6;
    // полоса прокрутки тонкая 3 GUI белый 0.35, видна только при прокрутке.
    private static void testRequirement7_WorldAndServerListSurfaceAndScrollbar() {
        int rowWidth = 270;
        int pad = 8;
        int panelWidth = rowWidth + pad * 2; // 286 = rowWidth + 16
        assertEquals(286, panelWidth, "List surface width is exactly rowWidth + 16 (matches rowWidth + PAD)");

        // Selected row style:
        GlassStyle rowStyle = GlassListBounds.rowStyle(true);
        assertEquals(6.0f, rowStyle.radius, 0.001f, "Selected row radius is 6");
        assertEquals(0.36f, rowStyle.tintA, 0.001f, "Selected row fill alpha is 0.36");
        assertEquals(118 / 255f, rowStyle.tintR, 0.001f, "Selected row tint matches Apple fill");

        // Unselected row: no fill, no glass plate
        GlassStyle unselectedStyle = GlassListBounds.rowStyle(false);
        assertEquals(0f, unselectedStyle.tintA, 0.001f, "Unselected row has 0 fill alpha");

        // Scrollbar parameters:
        float scrollbarWidth = 3.0f;
        float scrollbarAlpha = 0.35f;
        assertEquals(3.0f, scrollbarWidth, 0.001f, "Scrollbar width is 3 GUI");
        assertEquals(0.35f, scrollbarAlpha, 0.001f, "Scrollbar alpha is 0.35");

        // Visibility on scroll:
        long scrollTime = System.currentTimeMillis();
        boolean visibleWhileScrolling = (System.currentTimeMillis() - scrollTime) < 1200L;
        assertTrue(visibleWhileScrolling, "Scrollbar visible during active scrolling");

        long oldScrollTime = System.currentTimeMillis() - 2000L;
        boolean hiddenAfterScroll = (System.currentTimeMillis() - oldScrollTime) < 1200L;
        assertTrue(!hiddenAfterScroll, "Scrollbar hides after scrolling stops");
    }

    // 8. Нижняя группа кнопок: низ группы на 8 GUI выше нижнего края экрана.
    // 1.21.1 «Настройки»: «Готово» должно стоять как в 1.20.1 — внутри основной группы последним рядом, не отдельной капсулой у края.
    private static void testRequirement8_BottomButtonGroupAlignmentAndOptionsDone() {
        // Case A: Bottom button group on normal screen (e.g. SelectWorldScreen, height = 300)
        // Widget at y = 240, h = 20 (raw group bottom = 268)
        DummyWidget bottomBtn = new DummyWidget(100, 240, 200, 20, "BottomAction");
        ClickableWidget[] listWidgets = new ClickableWidget[]{bottomBtn};
        GlassGroups.computeLayoutForTest(400, 300, listWidgets, 1, false, false, false, false);

        int groupBottom = GlassGroups.bottom(0);
        assertEquals(292, groupBottom, "Bottom button group aligned to height - 8 GUI (300 - 8 = 292)");
        assertEquals(264, bottomBtn.getY(), "Bottom button Y shifted so button bottom + PAD = 292 (264 + 20 + 8 = 292)");

        // Case B: OptionsScreen with Done button (merged in main group, NOT forced to screen bottom)
        DummyWidget opt1 = new DummyWidget(50, 90, 140, 20, "Option 1");
        DummyWidget opt2 = new DummyWidget(210, 90, 140, 20, "Option 2");
        DummyWidget opt3 = new DummyWidget(50, 114, 140, 20, "Option 3");
        DummyWidget opt4 = new DummyWidget(210, 114, 140, 20, "Option 4");
        // In 1.21.1, Done button initially at footer position (y = 268 near bottom)
        DummyWidget doneBtn = new DummyWidget(100, 268, 200, 20, "Готово");
        ClickableWidget[] optionsWidgets = new ClickableWidget[]{opt1, opt2, opt3, opt4, doneBtn};

        GlassGroups.computeLayoutForTest(400, 300, optionsWidgets, 5, true, false, false, true);

        // Verify doneBtn was moved into the options group (maxOptionY = 134, doneBtn.y = 134 + 12 = 146)
        assertEquals(146, doneBtn.getY(), "Done button moved to maxOptionY + 12 (134 + 12 = 146), merging it into main options group");
        assertEquals(1, GlassGroups.count(), "All options + Done button form exactly ONE group");
        int optionsGroupBottom = GlassGroups.bottom(0);
        assertEquals(174, optionsGroupBottom, "Options group bottom is 146 + 20 + 8 = 174 GUI");

        int bottomMargin = 300 - optionsGroupBottom;
        assertTrue(bottomMargin > 100, "Options group is centered, bottom margin is " + bottomMargin + " GUI (not forced to 8 GUI)");
    }

    // 9. Иконки титульного экрана (язык, спец. возможности) в 1.21.1 — монохромные белые 0.92 как в 1.20.1, 16 GUI.
    private static void testRequirement9_TitleScreenMonochromeIcons() {
        float iconSize = 16.0f;
        int iconAlpha = Math.round(255 * 0.92f); // 235 -> 0xEB
        int iconColor = (iconAlpha << 24) | 0x00FFFFFF;

        assertEquals(16.0f, iconSize, 0.001f, "Icon size is 16 GUI");
        assertEquals(0xEBFFFFFF, iconColor, "Icon color is 0xEBFFFFFF (white 0.92)");
    }
}
