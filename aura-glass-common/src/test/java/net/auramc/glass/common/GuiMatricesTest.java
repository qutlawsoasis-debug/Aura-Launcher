package net.auramc.glass.common;

import net.auramc.glass.common.render.GuiMatrices;
import org.joml.Matrix4f;
import org.joml.Vector4f;

public final class GuiMatricesTest {
    public static void main(String[] args) {
        for (int scale = 1; scale <= 4; scale++) {
            float width = 1920f / scale, height = 1080f / scale;
            Matrix4f projection = new Matrix4f().setOrtho(0, width, height, 0, 1000, 21000);
            Matrix4f local = new Matrix4f();
            Matrix4f view = new Matrix4f().translation(0, 0, -11000);
            Matrix4f model = new Matrix4f(), scratch = new Matrix4f();
            check(!GuiMatrices.visibleDepth(projection, local, scratch), "old pipeline must reproduce clipped GUI");
            GuiMatrices.composeModel(view, local, model);
            check(GuiMatrices.visibleDepth(projection, model, scratch), "GUI view must restore visible depth");
            Vector4f center = new Vector4f(width / 2, height / 2, 0, 1);
            projection.mul(model, scratch).transform(center);
            check(Math.abs(center.x) < .00001 && Math.abs(center.y) < .00001, "center must stay at screen center");
            check(Math.abs(center.z) <= center.w, "geometry must be inside near/far clip planes");
            local.translation(20, 30, 0).scale(.5f);
            GuiMatrices.composeModel(view, local, model);
            Vector4f point = model.transform(new Vector4f(10, 10, 0, 1));
            check(point.x == 25 && point.y == 35 && point.z == -11000, "view/local multiplication order");
            check(view.m30() == 0 && view.m31() == 0 && local.m30() == 20, "do not mutate Minecraft matrices");
        }
        Matrix4f invalid = new Matrix4f().translation(0, 0, Float.NaN);
        check(!GuiMatrices.visibleDepth(new Matrix4f(), invalid, new Matrix4f()), "invalid depth must trigger fallback");
        System.out.println("GUI matrix regression checks passed: missing-view failure reproduced, scales 1-4 corrected");
    }

    private static void check(boolean ok, String message) {
        if (!ok) throw new AssertionError(message);
    }
}
