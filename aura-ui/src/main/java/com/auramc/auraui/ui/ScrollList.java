package com.auramc.auraui.ui;

import com.auramc.auraui.render.AuraRenderer;
import java.util.ArrayList;
import java.util.List;

public class ScrollList extends Component {
    public final List<Component> items = new ArrayList<>();
    public float scrollY = 0.0f;
    public float targetScrollY = 0.0f;
    public float itemSpacing = 8.0f;

    @Override
    public void render(float mouseX, float mouseY, float delta) {
        if (!visible) return;

        scrollY = Anim.lerp(scrollY, targetScrollY, delta * 12.0f);

        AuraRenderer.scissor(x, y, width, height);

        float currentY = y + scrollY;
        for (Component item : items) {
            item.x = x;
            item.y = currentY;
            item.width = width;
            if (currentY + item.height >= y && currentY <= y + height) {
                item.render(mouseX, mouseY, delta);
            }
            currentY += item.height + itemSpacing;
        }

        AuraRenderer.resetScissor();
    }

    @Override
    public boolean mouseScrolled(double mouseX, double mouseY, double amount) {
        if (visible && enabled && isHovered((float) mouseX, (float) mouseY)) {
            targetScrollY += (float) amount * 30.0f;
            clampScroll();
            return true;
        }
        return false;
    }

    private void clampScroll() {
        float totalH = 0.0f;
        for (Component item : items) totalH += item.height + itemSpacing;
        float maxScroll = Math.max(0.0f, totalH - height);
        targetScrollY = Math.max(-maxScroll, Math.min(0.0f, targetScrollY));
    }

    @Override
    public boolean mouseClicked(double mouseX, double mouseY, int button) {
        if (visible && enabled && isHovered((float) mouseX, (float) mouseY)) {
            for (Component item : items) {
                if (item.mouseClicked(mouseX, mouseY, button)) return true;
            }
        }
        return false;
    }
}
