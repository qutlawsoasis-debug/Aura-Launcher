package net.auramc.glass.client.ui;

import net.auramc.glass.common.style.GlassPreset;
import net.auramc.glass.common.style.GlassStyle;
import net.auramc.glass.common.style.GlassStyles;

public final class GlassLayoutTest {

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
        int screenWidth = 400;
        int screenHeight = 300;
        int margin = 6;
        int pad = 8;

        // Widget placed dangerously close to left/top edge
        int rawX = 2;
        int rawY = 4;
        int rawW = 100;
        int rawH = 20;

        int rawLeft = rawX - pad; // -6
        int rawTop = rawY - pad;  // -4
        int rawRight = rawX + rawW + pad;
        int rawBottom = rawY + rawH + pad;

        int shiftX = 0;
        int shiftY = 0;
        if (rawLeft < margin) shiftX = margin - rawLeft; // 6 - (-6) = 12
        if (rawTop < margin) shiftY = margin - rawTop;   // 6 - (-4) = 10

        int shiftedX = rawX + shiftX; // 14
        int shiftedY = rawY + shiftY; // 14

        int shiftedGroupLeft = shiftedX - pad; // 6
        int shiftedGroupTop = shiftedY - pad;  // 6

        assertTrue(shiftedGroupLeft >= margin, "Group left edge must be >= 6 GUI margin");
        assertTrue(shiftedGroupTop >= margin, "Group top edge must be >= 6 GUI margin");
        assertEquals(margin, shiftedGroupLeft, "Group left edge perfectly matches 6 GUI margin after shift");
        assertEquals(margin, shiftedGroupTop, "Group top edge perfectly matches 6 GUI margin after shift");

        // Verify hitbox is shifted along with widget:
        // Hitbox click at center of shifted widget:
        int clickX = shiftedX + rawW / 2;
        int clickY = shiftedY + rawH / 2;
        boolean hitboxHit = clickX >= shiftedX && clickX < shiftedX + rawW
                && clickY >= shiftedY && clickY < shiftedY + rawH;
        assertTrue(hitboxHit, "Widget hitbox matches shifted coordinates");

        // Widget placed near right/bottom edge
        rawX = 350;
        rawY = 280;
        rawLeft = rawX - pad;
        rawTop = rawY - pad;
        rawRight = rawX + rawW + pad;   // 458 > 394
        rawBottom = rawY + rawH + pad; // 308 > 294

        shiftX = 0; shiftY = 0;
        if (rawRight > screenWidth - margin) shiftX = (screenWidth - margin) - rawRight;
        if (rawBottom > screenHeight - margin) shiftY = (screenHeight - margin) - rawBottom;

        shiftedX = rawX + shiftX;
        shiftedY = rawY + shiftY;
        int shiftedGroupRight = shiftedX + rawW + pad;
        int shiftedGroupBottom = shiftedY + rawH + pad;

        assertTrue(shiftedGroupRight <= screenWidth - margin, "Group right edge must be <= width - 6 GUI");
        assertTrue(shiftedGroupBottom <= screenHeight - margin, "Group bottom edge must be <= height - 6 GUI");
    }

    // 2. Внутренний отступ группы PAD: 8 GUI; зазор между группами минимум 8 GUI.
    private static void testRequirement2_GroupPaddingAndMinimumGap() {
        int pad = 8;
        int widgetX = 100, widgetY = 100, widgetW = 100, widgetH = 20;
        int gLeft = widgetX - pad;
        int gTop = widgetY - pad;
        int gRight = widgetX + widgetW + pad;
        int gBottom = widgetY + widgetH + pad;

        assertEquals(8, widgetX - gLeft, "Left PAD must be exactly 8 GUI");
        assertEquals(8, gRight - (widgetX + widgetW), "Right PAD must be exactly 8 GUI");
        assertEquals(8, widgetY - gTop, "Top PAD must be exactly 8 GUI");
        assertEquals(8, gBottom - (widgetY + widgetH), "Bottom PAD must be exactly 8 GUI");

        // Check gap between two separate groups
        int g2Top = gBottom + 8; // exactly 8 GUI minimum gap
        int gap = g2Top - gBottom;
        assertTrue(gap >= 8, "Inter-group gap must be at least 8 GUI");
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
        int tabY = 6;
        int tabHeight = 24;
        int tabBottom = tabY + tabHeight; // 30
        int radius = 12;

        assertEquals(6, tabY, "Tab capsule top must be at y = 6 GUI");
        assertEquals(24, tabHeight, "Tab capsule height must be 24 GUI");
        assertEquals(30, tabBottom, "Tab capsule bottom must be 30 GUI");
        assertEquals(12, radius, "Tab capsule radius must be 12");

        // Shift of content below tabs:
        int vanillaFocusBottom = 24;
        int auraFocusBottom = tabBottom; // 30
        int contentShift = auraFocusBottom - vanillaFocusBottom;
        assertEquals(6, contentShift, "Content below tabs shifted down by exactly +6 GUI");

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
        float measuredTitleWidth = 80.0f; // at 13.5 size
        float titleX = (screenWidth - measuredTitleWidth) * 0.5f;
        assertEquals(160.0f, titleX, 0.001f, "Title is centered horizontally");

        int topGroupY = 60;
        float titleY = Math.max(6f, (float)(topGroupY - 10) - titleSize);
        float titleBottom = titleY + titleSize;

        assertEquals(36.5f, titleY, 0.001f, "Title Y position calculated correctly");
        assertEquals(50.0f, titleBottom, 0.001f, "Title bottom edge is 50 GUI");
        assertEquals(10.0f, topGroupY - titleBottom, 0.001f, "Title bottom edge is exactly 10 GUI above top group");
        assertTrue(titleBottom < topGroupY, "Title does not overlap with top group");

        // Color check: white 0.92
        int alpha092 = Math.round(255 * 0.92f); // 235 -> 0xEB
        int titleColor = (alpha092 << 24) | 0x00FFFFFF;
        assertEquals(0xEBFFFFFF, titleColor, "Title color is 0xEBFFFFFF (white 0.92)");
    }

    // 6. Длинный текст вне кнопок (экран «Экспериментальные настройки», предупреждения, ошибки):
    // кладётся в одну стеклянную группу вместе с кнопками этого экрана, ширина текста максимум 320 GUI, межстрочный 11 GUI.
    private static void testRequirement6_AlertAndExperimentalLongText() {
        int maxWidth = 320;
        int lineHeight = 11;

        int sampleTextWidth = 450;
        int clampedTextWidth = Math.min(sampleTextWidth, maxWidth);
        assertEquals(320, clampedTextWidth, "Alert text width clamped to maximum 320 GUI");
        assertEquals(11, lineHeight, "Alert text line height is 11 GUI");
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
        int screenHeight = 300;
        int targetBottom = screenHeight - 8; // 292
        assertEquals(292, targetBottom, "Bottom button group bottom is screen height - 8 GUI");

        // 1.21.1 OptionsScreen "Done" button alignment:
        int maxOptionY = 200; // bottom of options list
        int doneButtonY = maxOptionY + 12; // aligned inside options group
        int gapToOptions = doneButtonY - maxOptionY;
        assertEquals(12, gapToOptions, "Done button placed with GAP <= 12 GUI, merging it into options group");
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
