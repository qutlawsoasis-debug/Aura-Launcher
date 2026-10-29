package net.auramc.glass.client.ui;

import java.util.ArrayList;
import java.util.List;

public final class AutoCompleteGlassTest {

    public static void main(String[] args) throws Exception {
        System.out.println("--- Starting AutoComplete Glass Unit Tests ---");

        testRowGeometryAndHitboxes();
        System.out.println("1. Native row hitbox geometry (top/center/bottom and boundaries) PASS");

        testDisplayOffsetLastRowHitbox();
        System.out.println("2. Display offset with last row hitbox PASS");

        testLastMousePositionSemanticsAndKeyboardNavigation();
        System.out.println("3. lastMousePosition semantics and keyboard navigation PASS");

        testVisibilityInvariants();
        System.out.println("4. Visibility invariants (hide/show dropdown) PASS");

        System.out.println("All AutoComplete Glass tests PASSED successfully!");
    }

    private static void assertTrue(boolean condition, String msg) {
        if (!condition) {
            throw new AssertionError("Test failed: " + msg);
        }
    }

    private static void assertEquals(int expected, int actual, String msg) {
        if (expected != actual) {
            throw new AssertionError("Test failed: " + msg + " (expected: " + expected + ", actual: " + actual + ")");
        }
    }

    private static void assertEquals(String expected, String actual, String msg) {
        if (!java.util.Objects.equals(expected, actual)) {
            throw new AssertionError("Test failed: " + msg + " (expected: " + expected + ", actual: " + actual + ")");
        }
    }

    private static void testRowGeometryAndHitboxes() {
        int[] heights = {10, 12, 14, 16};
        int widgetY = 60;
        int shown = 5;

        for (int h : heights) {
            // Verify each row's hitbox
            for (int i = 0; i < shown; i++) {
                int expectedY = widgetY + i * h;
                assertEquals(expectedY, GlassAutoComplete.getRowY(widgetY, h, i), "Row Y mismatch for index " + i);
                assertEquals(h, GlassAutoComplete.getRowHeight(h), "Row height mismatch");

                // Test 1: exact top edge of row i
                int hitTop = GlassAutoComplete.hitTestRow(widgetY, h, shown, expectedY);
                assertEquals(i, hitTop, "Top edge of row " + i + " must hit row " + i);

                // Test 2: vertical center of row i
                int hitCenter = GlassAutoComplete.hitTestRow(widgetY, h, shown, expectedY + h / 2.0);
                assertEquals(i, hitCenter, "Center of row " + i + " must hit row " + i);

                // Test 3: exact bottom pixel of row i (expectedY + h - 1)
                int hitBottom = GlassAutoComplete.hitTestRow(widgetY, h, shown, expectedY + h - 1.0);
                assertEquals(i, hitBottom, "Bottom pixel of row " + i + " must hit row " + i);
            }

            // Test boundary 1: 1 pixel above the dropdown
            int hitAbove = GlassAutoComplete.hitTestRow(widgetY, h, shown, widgetY - 1.0);
            assertEquals(-1, hitAbove, "Pixel above dropdown must be outside (-1)");

            // Test boundary 2: exact bottom edge of the entire list (widgetY + shown * h)
            int hitBelow = GlassAutoComplete.hitTestRow(widgetY, h, shown, widgetY + shown * h);
            assertEquals(-1, hitBelow, "Pixel at widget bottom must be outside (-1)");

            // Test boundary 3: 50 pixels below dropdown
            int hitFarBelow = GlassAutoComplete.hitTestRow(widgetY, h, shown, widgetY + shown * h + 50.0);
            assertEquals(-1, hitFarBelow, "Pixel far below dropdown must be outside (-1)");
        }
    }

    private static void testDisplayOffsetLastRowHitbox() {
        int widgetY = 100;
        int suggestionHeight = 12;
        int maxSuggestions = 5;
        int totalSuggestions = 11;
        int displayOffset = 4; // shows suggestions 4, 5, 6, 7, 8

        int shown = Math.min(maxSuggestions, totalSuggestions - displayOffset); // 5
        assertEquals(5, shown, "Shown count must be 5");

        List<String> suggestions = new ArrayList<>();
        for (int i = 0; i < totalSuggestions; i++) {
            suggestions.add("Item " + i);
        }

        // Check mapping of rows to suggestions
        for (int i = 0; i < shown; i++) {
            int suggestionIndex = displayOffset + i;
            assertEquals("Item " + (4 + i), suggestions.get(suggestionIndex), "Item mapping mismatch at row " + i);
        }

        // Test the last displayed row (i = 4, which is suggestion index 8)
        int lastRowIndex = shown - 1;
        int lastRowY = GlassAutoComplete.getRowY(widgetY, suggestionHeight, lastRowIndex);
        assertEquals(widgetY + 4 * 12, lastRowY, "Last row Y mismatch");

        // Top of last row
        int hitLastTop = GlassAutoComplete.hitTestRow(widgetY, suggestionHeight, shown, lastRowY);
        assertEquals(lastRowIndex, hitLastTop, "Top of last row must hit last row index");

        // Center of last row
        int hitLastCenter = GlassAutoComplete.hitTestRow(widgetY, suggestionHeight, shown, lastRowY + suggestionHeight / 2.0);
        assertEquals(lastRowIndex, hitLastCenter, "Center of last row must hit last row index");

        // Bottom of last row
        int hitLastBottom = GlassAutoComplete.hitTestRow(widgetY, suggestionHeight, shown, lastRowY + suggestionHeight - 0.5);
        assertEquals(lastRowIndex, hitLastBottom, "Bottom of last row must hit last row index");

        // 1 pixel past last row
        int hitPastLast = GlassAutoComplete.hitTestRow(widgetY, suggestionHeight, shown, lastRowY + suggestionHeight);
        assertEquals(-1, hitPastLast, "1 pixel past last row must not hit any row");
    }

    private static void testLastMousePositionSemanticsAndKeyboardNavigation() {
        // Native Searchables updateHoveringState semantics:
        // If current (mx, my) equals lastMousePosition, updateHoveringState returns without modifying selectedIndex!
        // At the end of each frame, lastMousePosition is updated to (mx, my).

        class MockVector2d {
            double x;
            double y;

            MockVector2d(double x, double y) { this.x = x; this.y = y; }
            boolean equals(double ox, double oy) { return Double.compare(this.x, ox) == 0 && Double.compare(this.y, oy) == 0; }
            void set(double nx, double ny) { this.x = nx; this.y = ny; }
        }

        class MockAutoComplete {
            final MockVector2d lastMousePos = new MockVector2d(-1, -1);
            int selectedIndex = -1;
            int widgetX = 50;
            int widgetY = 50;
            int suggestionHeight = 12;
            int shown = 4;

            void updateHoveringState(double mx, double my) {
                // Native Searchables check
                if (lastMousePos.equals(mx, my)) {
                    return; // Skip updating hover state! Keyboard selection is preserved!
                }
                selectedIndex = -1;
                if (mx >= widgetX && mx <= widgetX + 100 && my >= widgetY && my < widgetY + shown * suggestionHeight) {
                    selectedIndex = (int) ((my - widgetY) / suggestionHeight);
                }
            }

            void render(double mx, double my) {
                updateHoveringState(mx, my);
                // After rendering, native code updates lastMousePosition
                lastMousePos.set(mx, my);
            }

            void onKeyDown() {
                selectedIndex = Math.min(shown - 1, selectedIndex + 1);
            }

            void onKeyUp() {
                selectedIndex = Math.max(0, selectedIndex - 1);
            }
        }

        MockAutoComplete auto = new MockAutoComplete();

        // Frame 1: Mouse enters over row 0 (y = 55)
        auto.render(60.0, 55.0);
        assertEquals(0, auto.selectedIndex, "Mouse over row 0 must select row 0");
        assertTrue(auto.lastMousePos.equals(60.0, 55.0), "lastMousePos must be updated to (60, 55)");

        // User presses Down Arrow on keyboard (mouse does NOT move)
        auto.onKeyDown();
        assertEquals(1, auto.selectedIndex, "Keyboard Down arrow must select row 1");

        // Frame 2: Mouse stationary at (60.0, 55.0)
        auto.render(60.0, 55.0);
        assertEquals(1, auto.selectedIndex, "Stationary mouse must NOT reset keyboard selection to row 0");

        // User presses Down Arrow again
        auto.onKeyDown();
        assertEquals(2, auto.selectedIndex, "Keyboard Down arrow must select row 2");

        // Frame 3: Mouse still stationary at (60.0, 55.0)
        auto.render(60.0, 55.0);
        assertEquals(2, auto.selectedIndex, "Stationary mouse must NOT reset keyboard selection to row 0 on subsequent frames");

        // User presses Up Arrow
        auto.onKeyUp();
        assertEquals(1, auto.selectedIndex, "Keyboard Up arrow must select row 1");

        // Frame 4: Mouse still stationary
        auto.render(60.0, 55.0);
        assertEquals(1, auto.selectedIndex, "Stationary mouse must NOT reset selection after Up arrow");

        // User hits Enter to insert suggestion 1
        int committedIndex = auto.selectedIndex;
        assertEquals(1, committedIndex, "Committed selection must be row 1");

        // Frame 5: Mouse now moves to row 3 (y = 90)
        auto.render(60.0, 90.0);
        assertEquals(3, auto.selectedIndex, "Moving mouse to row 3 must update selectedIndex to 3");
        assertTrue(auto.lastMousePos.equals(60.0, 90.0), "lastMousePos must be updated to (60, 90)");
    }

    private static void testVisibilityInvariants() {
        // Test basic visibility truth table
        assertTrue(!GlassAutoComplete.shouldRender(false, true, true, 5), "widget invisible must not render");
        assertTrue(!GlassAutoComplete.shouldRender(true, false, true, 5), "editBox invisible must not render");
        assertTrue(!GlassAutoComplete.shouldRender(true, true, false, 5), "editBox unfocused must not render");
        assertTrue(!GlassAutoComplete.shouldRender(true, true, true, 0), "empty suggestions must not render");
        assertTrue(GlassAutoComplete.shouldRender(true, true, true, 5), "all visible and focused with items must render");

        // Test hide/show lifecycle transitions
        boolean widgetVisible = true;
        boolean editBoxVisible = true;
        boolean editBoxFocused = false;
        int suggestions = 3;

        // 1. Initially unfocused -> hidden
        assertTrue(!GlassAutoComplete.shouldRender(widgetVisible, editBoxVisible, editBoxFocused, suggestions), "Initial state must be hidden");

        // 2. User focuses search box -> shown
        editBoxFocused = true;
        assertTrue(GlassAutoComplete.shouldRender(widgetVisible, editBoxVisible, editBoxFocused, suggestions), "Focused search box must show dropdown");

        // 3. User clicks outside / search box loses focus -> hidden
        editBoxFocused = false;
        assertTrue(!GlassAutoComplete.shouldRender(widgetVisible, editBoxVisible, editBoxFocused, suggestions), "Blur must hide dropdown");

        // 4. User refocuses -> shown
        editBoxFocused = true;
        assertTrue(GlassAutoComplete.shouldRender(widgetVisible, editBoxVisible, editBoxFocused, suggestions), "Refocus must show dropdown");

        // 5. Parent screen/tab hides widget -> hidden
        widgetVisible = false;
        assertTrue(!GlassAutoComplete.shouldRender(widgetVisible, editBoxVisible, editBoxFocused, suggestions), "Hiding widget must hide dropdown even when editBox still has focus");

        // 6. Restoring widget visibility -> shown
        widgetVisible = true;
        assertTrue(GlassAutoComplete.shouldRender(widgetVisible, editBoxVisible, editBoxFocused, suggestions), "Restoring visibility must restore dropdown");
    }
}
