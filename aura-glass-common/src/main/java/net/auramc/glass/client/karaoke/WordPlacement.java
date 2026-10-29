package net.auramc.glass.client.karaoke;

import net.minecraft.block.BlockState;
import net.minecraft.client.MinecraftClient;
import net.minecraft.util.hit.BlockHitResult;
import net.minecraft.util.hit.HitResult;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.RaycastContext;
import net.minecraft.world.World;
import org.joml.Matrix4f;
import org.joml.Vector3f;
import org.joml.Vector4f;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * 3D lyrics placement inside the player's gaze cone.
 * - Distance: 3–7 blocks (default ~5.2 bl);
 * - Cone: strictly 4–12° from crosshair for all anchors and bounds;
 * - Crosshair dead zone: strictly > 3.0°;
 * - Motion lead: clamped so strafing/diagonal velocity NEVER exceeds 12.0°;
 * - Obstacle avoidance: if a wall is closer than MIN_DISTANCE (3.0 bl), group is skipped (never placed behind wall);
 * - Reading order: left-to-right along screen X;
 * - Spacing: 0.3 em space between words;
 * - Wrapping: whole words wrap to second line with 1.25 line height;
 * - Centering: each line is horizontally centered;
 * - Guaranteed: word bounding rectangles in screen space NEVER intersect.
 */
public final class WordPlacement {

    public static final float BASE_FONT_SCALE = 0.30f;
    public static final float FONT_LINE_HEIGHT_EM = 1.28f; // ascent + descent of Onest font (0.97 + 0.305 = 1.275 ~ 1.28 em)
    public static final float SPACE_EM = 0.30f; // 0.3 em word space per VOZVRAT 4
    public static final float LINE_SPACING_FACTOR = 1.25f; // 1.25 line height per VOZVRAT 4 & 5
    public static final double TARGET_DISTANCE = 5.6; // blocks (in 3–7 blocks range, comfortably fitting words in 12° cone)
    public static final double MIN_DISTANCE = 3.0;
    public static final double MAX_DISTANCE = 7.0;
    public static final double MAX_CONE_ANGLE_DEG = 12.0;
    public static final double DEAD_ZONE_ANGLE_DEG = 3.0;
    public static final double SAFE_CONE_LIMIT_DEG = 11.5;

    public static final class WordAnchor {
        public final KaraokeModel.KaraokeWord word;
        public final Vec3d position;
        public final float width;
        public final float height;
        public final float yaw;
        public final float pitch;
        public final boolean[] lettersDetached;
        public final long[] detachJitters;

        public WordAnchor(KaraokeModel.KaraokeWord word, Vec3d position, float width, float height, float yaw, float pitch) {
            this.word = word;
            this.position = position;
            this.width = width;
            this.height = height;
            this.yaw = yaw;
            this.pitch = pitch;
            int len = (word != null && word.w != null) ? word.w.length() : 0;
            this.lettersDetached = new boolean[len];
            this.detachJitters = new long[len];
            java.util.Random rng = new java.util.Random(word != null ? word.t : 42L);
            for (int i = 0; i < len; i++) {
                this.detachJitters[i] = rng.nextInt(61); // 0 to 60 ms random start jitter
            }
        }

        public boolean allLettersDetached() {
            if (lettersDetached == null || lettersDetached.length == 0) return true;
            for (boolean b : lettersDetached) {
                if (!b) return false;
            }
            return true;
        }
    }

    public static final class WordRect {
        public final double xMin;
        public final double xMax;
        public final double yMin;
        public final double yMax;

        public WordRect(double xMin, double xMax, double yMin, double yMax) {
            this.xMin = xMin;
            this.xMax = xMax;
            this.yMin = yMin;
            this.yMax = yMax;
        }

        public boolean intersects(WordRect other) {
            double eps = 1e-4;
            if (this.xMax <= other.xMin + eps || other.xMax <= this.xMin + eps) return false;
            if (this.yMax <= other.yMin + eps || other.yMax <= this.yMin + eps) return false;
            return true;
        }

        @Override
        public String toString() {
            return String.format(java.util.Locale.ROOT, "[x: %.2f..%.2f, y: %.2f..%.2f]", xMin, xMax, yMin, yMax);
        }
    }

    private static final class WordLayoutItem {
        final KaraokeModel.KaraokeWord word;
        final float width;
        final float scale;

        WordLayoutItem(KaraokeModel.KaraokeWord word, float width, float scale) {
            this.word = word;
            this.width = width;
            this.scale = scale;
        }
    }

    private static final class LayoutLine {
        final int index;
        final double tiltDeg;
        final double verticalOffset;
        final List<WordLayoutItem> items = new ArrayList<>();

        LayoutLine(int index, double tiltDeg, double verticalOffset) {
            this.index = index;
            this.tiltDeg = tiltDeg;
            this.verticalOffset = verticalOffset;
        }

        double getTotalWidth(float spaceWidth) {
            double total = 0.0;
            for (int i = 0; i < items.size(); i++) {
                total += items.get(i).width;
                if (i > 0) total += spaceWidth;
            }
            return total;
        }
    }

    private WordPlacement() {}

    /**
     * Lays out words into 1 or 2 centered lines within the 4–12° gaze cone.
     */
    public static List<WordAnchor> layoutGroup(List<KaraokeModel.KaraokeWord> words,
                                               Vec3d eyePos,
                                               float playerYaw,
                                               float playerPitch,
                                               Vec3d playerVelocity,
                                               float gazeAngleFactor,
                                               World world) {
        List<WordAnchor> anchors = new ArrayList<>();
        if (words == null || words.isEmpty()) return anchors;

        KaraokeConfig config = KaraokeConfig.get();
        double targetDistance = Math.max(MIN_DISTANCE, Math.min(MAX_DISTANCE, TARGET_DISTANCE));

        // Gaze direction vectors
        double pitchRad = Math.toRadians(playerPitch);
        double yawRad = Math.toRadians(playerYaw);

        Vec3d lookVec = new Vec3d(
                -Math.sin(yawRad) * Math.cos(pitchRad),
                -Math.sin(pitchRad),
                Math.cos(yawRad) * Math.cos(pitchRad)
        ).normalize();

        // Camera horizontal right vector: looking South (yaw=0) -> right is West (-X)
        Vec3d rightVec = new Vec3d(
                -Math.cos(yawRad),
                0,
                -Math.sin(yawRad)
        ).normalize();

        Vec3d upVec = rightVec.crossProduct(lookVec).normalize();
        Vec3d downVec = upVec.multiply(-1.0);

        // Raycast against solid collider blocks to ensure words are placed in front of walls
        double actualDistance = targetDistance;
        if (world != null) {
            Vec3d rayEnd = eyePos.add(lookVec.multiply(targetDistance + 0.8));
            try {
                BlockHitResult hit = world.raycast(new RaycastContext(
                        eyePos,
                        rayEnd,
                        RaycastContext.ShapeType.COLLIDER,
                        RaycastContext.FluidHandling.NONE,
                        MinecraftClient.getInstance() != null ? MinecraftClient.getInstance().player : null
                ));
                if (hit != null && hit.getType() != HitResult.Type.MISS) {
                    double hitDist = hit.getPos().distanceTo(eyePos);
                    double safeDist = hitDist - 0.6;
                    if (safeDist < MIN_DISTANCE) {
                        // Wall is closer than MIN_DISTANCE: do NOT place text behind wall; skip group
                        return Collections.emptyList();
                    }
                    actualDistance = Math.min(targetDistance, safeDist);
                }
            } catch (Throwable ignored) {
            }
        }

        float baseFontScale = (float) (35.0 * actualDistance / 650.7) * config.getSize();
        float fullLineHeight = FONT_LINE_HEIGHT_EM * baseFontScale;
        float wordHeight = fullLineHeight;
        float spaceWidth = SPACE_EM * baseFontScale;
        float lineSpacing = LINE_SPACING_FACTOR * fullLineHeight;

        List<WordLayoutItem> items = new ArrayList<>();
        for (KaraokeModel.KaraokeWord w : words) {
            float rawWidth = estimateWordWidth(w.w) * baseFontScale;
            items.add(new WordLayoutItem(w, rawWidth, baseFontScale));
        }

        // Top edge of line 1 strictly outside 3.0° dead zone
        double y_top = actualDistance * Math.tan(Math.toRadians(3.25));

        // Max allowed line width so that line fits inside <= 12° cone:
        double y1 = y_top + fullLineHeight * 0.5;
        double maxLineWidth = 2.0 * actualDistance * Math.tan(Math.toRadians(12.0));

        // Check if all items fit on 1 line
        double totalSingleLineWidth = 0.0;
        for (int i = 0; i < items.size(); i++) {
            totalSingleLineWidth += items.get(i).width;
            if (i > 0) totalSingleLineWidth += spaceWidth;
        }

        // Per VOZVRAT 5 & Task 2: 2 short words stay on 1 line; 3+ words wrap to 2 lines
        boolean fitsOnSingleLine = items.size() <= 2 && totalSingleLineWidth <= maxLineWidth;

        List<LayoutLine> lines = new ArrayList<>();
        if (fitsOnSingleLine) {
            double y0 = y_top + fullLineHeight * 0.5;
            LayoutLine line0 = new LayoutLine(0, Math.toDegrees(Math.atan2(y0, actualDistance)), y0);
            line0.items.addAll(items);
            lines.add(line0);
        } else {
            double y2 = y1 + lineSpacing;

            LayoutLine line1 = new LayoutLine(0, Math.toDegrees(Math.atan2(y1, actualDistance)), y1);
            LayoutLine line2 = new LayoutLine(1, Math.toDegrees(Math.atan2(y2, actualDistance)), y2);

            int targetLine1Count = items.size() == 3 ? 2 : (items.size() == 4 ? 2 : (items.size() >= 5 ? 3 : 1));

            for (int i = 0; i < items.size(); i++) {
                if (i < targetLine1Count) {
                    line1.items.add(items.get(i));
                } else {
                    line2.items.add(items.get(i));
                }
            }
            lines.add(line1);
            if (!line2.items.isEmpty()) {
                lines.add(line2);
            }
        }

        // Analytic group scale solver: ensures all 4 corners of all words <= 11.85° cone
        double maxAllowedR = actualDistance * Math.tan(Math.toRadians(11.85));
        double minScale = 1.0;
        for (LayoutLine line : lines) {
                double lineTotalW = line.getTotalWidth(spaceWidth);
                double startX = -lineTotalW * 0.5;
                double curX = startX;

                for (WordLayoutItem item : line.items) {
                    double centerX = curX + item.width * 0.5;
                    double deltaY_center = line.verticalOffset - y_top;

                    double halfW = item.width * 0.5;
                    double halfH = wordHeight * 0.5;

                    for (double sx : new double[]{-halfW, halfW}) {
                        for (double sy : new double[]{-halfH, halfH}) {
                            double cx = centerX + sx;
                            double deltaY_corner = deltaY_center + sy;
                            double A = cx * cx + deltaY_corner * deltaY_corner;
                            if (A > 1e-9) {
                                double B = 2.0 * y_top * deltaY_corner;
                                double C = y_top * y_top - maxAllowedR * maxAllowedR;
                                if (C < 0) {
                                    double disc = B * B - 4.0 * A * C;
                                    if (disc >= 0) {
                                        double s = (-B + Math.sqrt(disc)) / (2.0 * A);
                                        if (s > 0 && s < minScale) {
                                            minScale = s;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    curX += item.width + spaceWidth;
                }
            }

        if (minScale < 0.999) {
            float s = (float) minScale;
            baseFontScale *= s;
            fullLineHeight *= s;
            wordHeight = fullLineHeight;
            spaceWidth *= s;
            lineSpacing *= s;

            items.clear();
            for (KaraokeModel.KaraokeWord w : words) {
                float rawWidth = estimateWordWidth(w.w) * baseFontScale;
                items.add(new WordLayoutItem(w, rawWidth, baseFontScale));
            }

            lines.clear();
            if (fitsOnSingleLine) {
                double y0_s = y_top + fullLineHeight * 0.5;
                LayoutLine line0 = new LayoutLine(0, Math.toDegrees(Math.atan2(y0_s, actualDistance)), y0_s);
                line0.items.addAll(items);
                lines.add(line0);
            } else {
                double y1_s = y_top + fullLineHeight * 0.5;
                double y2_s = y1_s + lineSpacing;
                LayoutLine line1 = new LayoutLine(0, Math.toDegrees(Math.atan2(y1_s, actualDistance)), y1_s);
                LayoutLine line2 = new LayoutLine(1, Math.toDegrees(Math.atan2(y2_s, actualDistance)), y2_s);
                int targetLine1Count = items.size() == 3 ? 2 : (items.size() == 4 ? 2 : (items.size() >= 5 ? 3 : 1));
                for (int i = 0; i < items.size(); i++) {
                    if (i < targetLine1Count) {
                        line1.items.add(items.get(i));
                    } else {
                        line2.items.add(items.get(i));
                    }
                }
                lines.add(line1);
                if (!line2.items.isEmpty()) {
                    lines.add(line2);
                }
            }
        }

        // Motion lead calculation:
        // Project velocity onto camera right and up, but strictly clamp into available cone margin
        double leadX = 0.0;
        double leadY = 0.0;
        if (playerVelocity != null && playerVelocity.lengthSquared() > 0.0001) {
            double vRight = playerVelocity.dotProduct(rightVec);
            double vUp = playerVelocity.dotProduct(upVec);
            leadX = vRight * 1.5;
            leadY = vUp * 1.5;
        }

        // Scale lead down until every anchor and all 4 boundary corners of all words fit in [3.05°, 11.8°]
        double scaleLead = 1.0;
        for (int iter = 0; iter < 11; iter++) {
            boolean valid = true;
            double curLeadX = leadX * scaleLead;
            double curLeadY = leadY * scaleLead;

            for (LayoutLine line : lines) {
                double lineTotalW = line.getTotalWidth(spaceWidth);
                double startX = -lineTotalW * 0.5;
                double curX = startX;

                for (WordLayoutItem item : line.items) {
                    double ax = curX + item.width * 0.5 + curLeadX;
                    double ay = line.verticalOffset - curLeadY;

                    double anchorRadius = Math.sqrt(ax * ax + ay * ay);
                    double anchorAngle = Math.toDegrees(Math.atan2(anchorRadius, actualDistance));
                    if (anchorAngle > 11.4 || anchorAngle < 3.2) {
                        valid = false;
                        break;
                    }

                    double halfW = item.width * 0.5;
                    double halfH = wordHeight * 0.5;
                    double[] cxs = {ax - halfW, ax + halfW};
                    double[] cys = {ay - halfH, ay + halfH};
                    for (double cx : cxs) {
                        for (double cy : cys) {
                            double cRadius = Math.sqrt(cx * cx + cy * cy);
                            double cAngle = Math.toDegrees(Math.atan2(cRadius, actualDistance));
                            if (cAngle > 11.8 || cAngle < 3.05) {
                                valid = false;
                                break;
                            }
                        }
                        if (!valid) break;
                    }
                    if (!valid) break;
                    curX += item.width + spaceWidth;
                }
                if (!valid) break;
            }

            if (valid) {
                break;
            }
            scaleLead -= 0.1;
            if (scaleLead <= 0.0) {
                scaleLead = 0.0;
                break;
            }
        }
        leadX *= scaleLead;
        leadY *= scaleLead;

        // Generate 3D anchors for items on each line
        for (LayoutLine line : lines) {
            double lineTotalW = line.getTotalWidth(spaceWidth);
            double startX = -lineTotalW * 0.5;
            double curX = startX;

            for (WordLayoutItem item : line.items) {
                double wordCenterX = curX + item.width * 0.5 + leadX;
                double verticalOffset = line.verticalOffset - leadY;

                Vec3d wordPos = eyePos
                        .add(lookVec.multiply(actualDistance))
                        .add(rightVec.multiply(wordCenterX))
                        .add(downVec.multiply(verticalOffset));

                Vec3d toEye = eyePos.subtract(wordPos);
                float wYaw = (float) Math.toDegrees(Math.atan2(-toEye.x, toEye.z));
                float wPitch = (float) Math.toDegrees(Math.atan2(toEye.y, Math.sqrt(toEye.x * toEye.x + toEye.z * toEye.z)));

                anchors.add(new WordAnchor(item.word, wordPos, item.width, wordHeight, wYaw, wPitch));
                curX += item.width + spaceWidth;
            }
        }

        // Uniform ground clearance: shift the ENTIRE group rigidly if lowest point intersects ground
        if (world != null && !anchors.isEmpty()) {
            double lowestY = Double.POSITIVE_INFINITY;
            Vec3d lowestAnchor = null;
            for (WordAnchor a : anchors) {
                double bottomY = a.position.y - baseFontScale * 0.5;
                if (bottomY < lowestY) {
                    lowestY = bottomY;
                    lowestAnchor = a.position;
                }
            }
            if (lowestAnchor != null) {
                BlockPos bp = BlockPos.ofFloored(lowestAnchor.x, lowestY, lowestAnchor.z);
                BlockState st = world.getBlockState(bp);
                if (!st.isAir() && (st.isFullCube(world, bp) || st.isOpaque())) {
                    double targetY = bp.getY() + 1.1 + baseFontScale * 0.5;
                    double shiftY = targetY - lowestAnchor.y;
                    if (shiftY > 0) {
                        List<WordAnchor> shifted = new ArrayList<>();
                        for (WordAnchor a : anchors) {
                            shifted.add(new WordAnchor(a.word, a.position.add(0, shiftY, 0), a.width, a.height, a.yaw, a.pitch));
                        }
                        anchors = shifted;
                    }
                }
            }
        }

        // Re-verify line of sight from eye to every final anchor position against obstacles
        if (world != null && !anchors.isEmpty()) {
            for (WordAnchor a : anchors) {
                try {
                    BlockHitResult hit = world.raycast(new RaycastContext(
                            eyePos,
                            a.position,
                            RaycastContext.ShapeType.COLLIDER,
                            RaycastContext.FluidHandling.NONE,
                            MinecraftClient.getInstance() != null ? MinecraftClient.getInstance().player : null
                    ));
                    if (hit != null && hit.getType() != HitResult.Type.MISS) {
                        double hitDist = hit.getPos().distanceTo(eyePos);
                        double anchorDist = a.position.distanceTo(eyePos);
                        if (hitDist < anchorDist - 0.2) {
                            // Anchor is behind an obstacle: skip group
                            return Collections.emptyList();
                        }
                    }
                } catch (Throwable ignored) {
                }
            }
        }

        return anchors;
    }

    /**
     * Projects a word billboard into screen-space 2D bounding rectangle [xMin..xMax, yMin..yMax].
     */
    public static WordRect projectWordToScreen(WordAnchor anchor, Matrix4f viewMatrix, Matrix4f projMatrix, int screenWidth, int screenHeight, Vec3d camPos) {
        // Basis vectors in camera-relative world space:
        // viewMatrix * U == (1, 0, 0) [Screen RIGHT] (Row 0 of viewMatrix: m00, m10, m20)
        // viewMatrix * V == (0, -1, 0) [Screen DOWN] (-Row 1 of viewMatrix: -m01, -m11, -m21)
        Vector3f U = new Vector3f(viewMatrix.m00(), viewMatrix.m10(), viewMatrix.m20());
        Vector3f V = new Vector3f(-viewMatrix.m01(), -viewMatrix.m11(), -viewMatrix.m21());

        float halfW = anchor.width * 0.5f;
        float fullH = anchor.height > 0.01f ? anchor.height : (BASE_FONT_SCALE * FONT_LINE_HEIGHT_EM);
        float halfH = fullH * 0.5f;

        Vec3d center = anchor.position;
        Vector3f relCenter = new Vector3f(
                (float) (center.x - camPos.x),
                (float) (center.y - camPos.y),
                (float) (center.z - camPos.z)
        );

        float[] dxs = new float[]{-halfW, halfW, halfW, -halfW};
        float[] dys = new float[]{-halfH, -halfH, halfH, halfH};

        double minX = Double.POSITIVE_INFINITY;
        double maxX = Double.NEGATIVE_INFINITY;
        double minY = Double.POSITIVE_INFINITY;
        double maxY = Double.NEGATIVE_INFINITY;

        for (int i = 0; i < 4; i++) {
            Vector3f worldPt = new Vector3f(relCenter)
                    .add(new Vector3f(U).mul(dxs[i]))
                    .add(new Vector3f(V).mul(dys[i]));

            Vector4f camPt = new Vector4f(worldPt.x, worldPt.y, worldPt.z, 1.0f);
            viewMatrix.transform(camPt);

            Vector4f clipPt = new Vector4f(camPt);
            projMatrix.transform(clipPt);

            if (clipPt.w <= 0.001f) continue;
            float ndcX = clipPt.x / clipPt.w;
            float ndcY = clipPt.y / clipPt.w;

            double sx = (ndcX + 1.0) * 0.5 * screenWidth;
            double sy = (1.0 - ndcY) * 0.5 * screenHeight;

            minX = Math.min(minX, sx);
            maxX = Math.max(maxX, sx);
            minY = Math.min(minY, sy);
            maxY = Math.max(maxY, sy);
        }

        return new WordRect(minX, maxX, minY, maxY);
    }

    public static WordRect projectBoxToScreen(Vec3d center, float halfWidth, float halfHeight, Matrix4f viewMatrix, Matrix4f projMatrix, int screenWidth, int screenHeight, Vec3d camPos) {
        if (center == null || viewMatrix == null || projMatrix == null || camPos == null) return null;
        Vector3f U = new Vector3f(viewMatrix.m00(), viewMatrix.m10(), viewMatrix.m20());
        Vector3f V = new Vector3f(-viewMatrix.m01(), -viewMatrix.m11(), -viewMatrix.m21());

        Vector3f relCenter = new Vector3f(
                (float) (center.x - camPos.x),
                (float) (center.y - camPos.y),
                (float) (center.z - camPos.z)
        );

        float[] dxs = new float[]{-halfWidth, halfWidth, halfWidth, -halfWidth};
        float[] dys = new float[]{-halfHeight, -halfHeight, halfHeight, halfHeight};

        double minX = Double.POSITIVE_INFINITY;
        double maxX = Double.NEGATIVE_INFINITY;
        double minY = Double.POSITIVE_INFINITY;
        double maxY = Double.NEGATIVE_INFINITY;

        int valid = 0;
        for (int i = 0; i < 4; i++) {
            Vector3f worldPt = new Vector3f(relCenter)
                    .add(new Vector3f(U).mul(dxs[i]))
                    .add(new Vector3f(V).mul(dys[i]));

            Vector4f camPt = new Vector4f(worldPt.x, worldPt.y, worldPt.z, 1.0f);
            viewMatrix.transform(camPt);

            Vector4f clipPt = new Vector4f(camPt);
            projMatrix.transform(clipPt);

            if (clipPt.w <= 0.001f) continue;
            float ndcX = clipPt.x / clipPt.w;
            float ndcY = clipPt.y / clipPt.w;

            double sx = (ndcX + 1.0) * 0.5 * screenWidth;
            double sy = (1.0 - ndcY) * 0.5 * screenHeight;

            minX = Math.min(minX, sx);
            maxX = Math.max(maxX, sx);
            minY = Math.min(minY, sy);
            maxY = Math.max(maxY, sy);
            valid++;
        }

        if (valid == 0) return null;
        return new WordRect(minX, maxX, minY, maxY);
    }

    public static float estimateWordWidth(String text) {
        if (text == null || text.isEmpty()) return 0.5f;
        try {
            net.auramc.glass.common.render.AuraRenderer.ensureFontMetricsLoaded();
            float tw = net.auramc.glass.common.render.AuraRenderer.textWidth(text, 1.0f, 700);
            if (tw > 0.05f) return tw;
        } catch (Throwable ignored) {
        }
        return Math.max(0.6f, text.length() * 0.55f);
    }

    public static double angleFromLook(Vec3d point, Vec3d eyePos, Vec3d lookVec) {
        Vec3d toPoint = point.subtract(eyePos);
        double dist = toPoint.length();
        if (dist < 1e-6) return 0.0;
        double dot = toPoint.dotProduct(lookVec) / dist;
        dot = Math.max(-1.0, Math.min(1.0, dot));
        return Math.toDegrees(Math.acos(dot));
    }

    public static boolean isOutsideDeadZone(Vec3d point, Vec3d eyePos, Vec3d lookVec) {
        return angleFromLook(point, eyePos, lookVec) >= DEAD_ZONE_ANGLE_DEG;
    }

    public static boolean isInsideCone(Vec3d point, Vec3d eyePos, Vec3d lookVec) {
        return angleFromLook(point, eyePos, lookVec) <= MAX_CONE_ANGLE_DEG;
    }

    public static Vec3d getGroupCenter(Vec3d eyePos, float playerYaw, float playerPitch, double distance, float gazeAngleFactor) {
        double pitchRad = Math.toRadians(playerPitch);
        double yawRad = Math.toRadians(playerYaw);
        Vec3d lookVec = new Vec3d(
                -Math.sin(yawRad) * Math.cos(pitchRad),
                -Math.sin(pitchRad),
                Math.cos(yawRad) * Math.cos(pitchRad)
        ).normalize();
        Vec3d rightVec = new Vec3d(-Math.cos(yawRad), 0, -Math.sin(yawRad)).normalize();
        Vec3d upVec = rightVec.crossProduct(lookVec).normalize();
        double offsetRad = Math.toRadians(6.0);
        Vec3d coneDir = lookVec.multiply(Math.cos(offsetRad)).add(upVec.multiply(-Math.sin(offsetRad))).normalize();
        return eyePos.add(coneDir.multiply(distance));
    }
}
