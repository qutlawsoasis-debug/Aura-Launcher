package net.auramc.glass.common;

import java.util.List;
import net.auramc.glass.common.ui.AuraVirtualListModel;

public final class SampleListTest {
    public static void main(String[] args) {
        AuraVirtualListModel<String> list = new AuraVirtualListModel<>(10, 20, 160, 56);
        list.setItems(List.of("one", "two", "three", "four"));
        check(!list.onMouseClicked(30, 22, 0), "top padding must not select a row");
        check(list.onMouseClicked(30, 47, 0) && list.getSelectedIndex() == 0, "hit-test must match painted first row");
        check(list.onMouseClicked(30, 48, 0) && list.getSelectedIndex() == 1, "next row begins after padded first row");
        check(!list.onMouseClicked(165, 40, 0), "scrollbar gutter must not select a row");
        list.update(0L);
        list.onMouseScrolled(30, 40, -100);
        check(list.getTargetScrollOffset() == 48, "scroll destination excludes padding");
        list.update(1_000_000_000L);
        check(list.getScrollOffset() == 48, "scroll range excludes padding");
        check(list.getRowY(3) + list.getItemHeight() == list.getViewportY() + list.getViewportHeight(), "last row must align with viewport bottom");
        list.setHeight(104);
        check(list.getScrollOffset() == 0 && list.getSelectedIndex() == 1, "resize clamps scrolling and preserves selection");
        System.out.println("Sample list checks passed: padding, hit-testing, scrollbar gutter and resize");
    }

    private static void check(boolean ok, String message) {
        if (!ok) throw new AssertionError(message);
    }
}
