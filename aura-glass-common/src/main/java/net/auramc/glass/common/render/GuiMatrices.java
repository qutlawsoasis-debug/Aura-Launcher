package net.auramc.glass.common.render;

import org.joml.Matrix4f;

public final class GuiMatrices {
    private GuiMatrices() {}

    public static void composeModel(Matrix4f view, Matrix4f local, Matrix4f result) {
        result.set(view).mul(local);
    }

    public static boolean visibleDepth(Matrix4f projection, Matrix4f model, Matrix4f scratch) {
        scratch.set(projection).mul(model);
        float z = scratch.m32(), w = scratch.m33();
        return Float.isFinite(z) && Float.isFinite(w) && w > 0 && z >= -w && z <= w;
    }
}
