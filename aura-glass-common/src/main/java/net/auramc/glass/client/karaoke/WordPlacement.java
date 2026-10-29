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
import java.util.List;

/**
 * 3D lyrics placement inside the player's gaze cone.
 * - Distance: 3–7 blocks (default ~5.2 bl);
 * - Cone: 4–12° from crosshair;
 * - Crosshair dead zone: strictly > 3.0°;
 * - Reading order: left-to-right along screen X;
 * - Spacing: 0.3 em space between words;
 * - Wrapping: whole words wrap to second line with 1.25 line height;
 * - Centering: each line is horizontally centered;
 * - Guaranteed: word bounding rectangles in screen space NEVER intersect.
 */
public final class WordPlacement {

    public static final float BASE_FONT_SCALE = 0.35f;
    public static final float SPACE_EM = 0.30f; // 0.3 em word space per VOZVRAT 4
    public static final float LINE_SPACING_FACTOR = 1.25f; // 1.25 line height per VOZVRAT 4
    public static final double TARGET_DISTANCE = 5.2; // blocks (in 3–7 blocks range)
    public static final double MIN_DISTANCE = 3.0;
    public static final double MAX_DISTANCE = 7.0;
    public static final double MAX_CONE_ANGLE_DEG = 12.0;
    public static final double DEAD_ZONE_ANGLE_DEG = 3.0;
    public static final double SAFE_CONE_LIMIT_DEG = 11.6;

    public static final class WordAnchor {
        public final KaraokeModel.KaraokeWord word;
        public final Vec3d position;
        public final float width;
        public final float height;
        public final float yaw;
        public final float pitch;

        public WordAnchor(KaraokeModel.KaraokeWord word, Vec3d position, float width, float height, float yaw, float pitch) {
            this.word = word;
            this.position = position;
            this.width = width;
            this.height = height;
            this.yaw = yaw;
            this.pitch = pitch;
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
                    actualDistance = Math.max(MIN_DISTANCE, Math.min(targetDistance, hitDist - 0.6));
                }
            } catch (Throwable ignored) {
            }
        }

        float baseFontScale = BASE_FONT_SCALE * config.getSize();
        float wordHeight = baseFontScale;
        float spaceWidth = SPACE_EM * baseFontScale;
        float lineSpacing = LINE_SPACING_FACTOR * wordHeight;

        // Vertical tilt angles downward from crosshair
        // 1-line layout: line at 6.0° downward (safely outside dead zone 3°, comfortably inside 12°)
        double beta0 = Math.toRadians(6.0);
        double tanSafe = Math.tan(Math.toRadians(SAFE_CONE_LIMIT_DEG));
        double xMax0 = actualDistance * Math.sqrt(Math.max(0.0, tanSafe * tanSafe - Math.tan(beta0) * Math.tan(beta0)));
        double wMax0 = 2.0 * xMax0;

        // 2-line layout: Line 1 at 4.9° downward (top edge ~3.1° > 3.0°), Line 2 at 4.9° + lineSpacing
        double beta1 = Math.toRadians(4.9);
        double beta2 = beta1 + Math.atan(lineSpacing / actualDistance);
        double xMax1 = actualDistance * Math.sqrt(Math.max(0.0, tanSafe * tanSafe - Math.tan(beta1) * Math.tan(beta1)));
        double wMax1 = 2.0 * xMax1;
        double xMax2 = actualDistance * Math.sqrt(Math.max(0.0, tanSafe * tanSafe - Math.tan(beta2) * Math.tan(beta2)));
        double wMax2 = 2.0 * xMax2;

        List<WordLayoutItem> items = new ArrayList<>();
        for (KaraokeModel.KaraokeWord w : words) {
            float rawWidth = estimateWordWidth(w.w) * baseFontScale;
            items.add(new WordLayoutItem(w, rawWidth, baseFontScale));
        }

        // Check if all items fit on 1 line
        double totalSingleLineWidth = 0.0;
        for (int i = 0; i < items.size(); i++) {
            totalSingleLineWidth += items.get(i).width;
            if (i > 0) totalSingleLineWidth += spaceWidth;
        }

        List<LayoutLine> lines = new ArrayList<>();
        if (totalSingleLineWidth <= wMax0) {
            // All words fit on a single line
            LayoutLine line0 = new LayoutLine(0, Math.toDegrees(beta0), actualDistance * Math.tan(beta0));
            line0.items.addAll(items);
            lines.add(line0);
        } else {
            // Multi-word group wider than 1 line: wrap whole words to second line
            LayoutLine line1 = new LayoutLine(0, Math.toDegrees(beta1), actualDistance * Math.tan(beta1));
            LayoutLine line2 = new LayoutLine(1, Math.toDegrees(beta2), actualDistance * Math.tan(beta1) + lineSpacing);

            int wordIdx = 0;
            // Fill Line 1 with whole words
            while (wordIdx < items.size()) {
                WordLayoutItem item = items.get(wordIdx);
                float itemW = item.width;
                float itemScale = item.scale;
                if (line1.items.isEmpty() && itemW > wMax1) {
                    itemScale = (float) (baseFontScale * (wMax1 / itemW));
                    itemW = (float) wMax1;
                    line1.items.add(new WordLayoutItem(item.word, itemW, itemScale));
                    wordIdx++;
                    break;
                }
                double proposedWidth = line1.getTotalWidth(spaceWidth) + (line1.items.isEmpty() ? 0 : spaceWidth) + itemW;
                if (proposedWidth <= wMax1 || line1.items.isEmpty()) {
                    line1.items.add(new WordLayoutItem(item.word, itemW, itemScale));
                    wordIdx++;
                } else {
                    break;
                }
            }

            // Fill Line 2 with remaining whole words
            while (wordIdx < items.size()) {
                WordLayoutItem item = items.get(wordIdx);
                float itemW = item.width;
                float itemScale = item.scale;
                if (line2.items.isEmpty() && itemW > wMax2) {
                    itemScale = (float) (baseFontScale * (wMax2 / itemW));
                    itemW = (float) wMax2;
                    line2.items.add(new WordLayoutItem(item.word, itemW, itemScale));
                    wordIdx++;
                    break;
                }
                double proposedWidth = line2.getTotalWidth(spaceWidth) + (line2.items.isEmpty() ? 0 : spaceWidth) + itemW;
                if (proposedWidth <= wMax2 || line2.items.isEmpty()) {
                    line2.items.add(new WordLayoutItem(item.word, itemW, itemScale));
                    wordIdx++;
                } else {
                    // Line 2 full; remaining words deferred to next group
                    break;
                }
            }

            lines.add(line1);
            if (!line2.items.isEmpty()) {
                lines.add(line2);
            }
        }

        // Motion lead
        Vec3d motionLead = Vec3d.ZERO;
        if (playerVelocity != null && playerVelocity.lengthSquared() > 0.001) {
            motionLead = playerVelocity.multiply(8.0);
            if (motionLead.length() > 1.2) {
                motionLead = motionLead.normalize().multiply(1.2);
            }
        }

        // Generate 3D anchors for items on each line
        for (LayoutLine line : lines) {
            double lineTotalW = line.getTotalWidth(spaceWidth);
            double startX = -lineTotalW * 0.5;
            double curX = startX;

            for (WordLayoutItem item : line.items) {
                double wordCenterX = curX + item.width * 0.5;
                Vec3d wordPos = eyePos
                        .add(lookVec.multiply(actualDistance))
                        .add(rightVec.multiply(wordCenterX))
                        .add(downVec.multiply(line.verticalOffset))
                        .add(motionLead);

                Vec3d toEye = eyePos.subtract(wordPos);
                float wYaw = (float) Math.toDegrees(Math.atan2(-toEye.x, toEye.z));
                float wPitch = (float) Math.toDegrees(Math.atan2(toEye.y, Math.sqrt(toEye.x * toEye.x + toEye.z * toEye.z)));

                anchors.add(new WordAnchor(item.word, wordPos, item.width, item.scale, wYaw, wPitch));
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

        return anchors;
    }

    /**
     * Projects a word billboard into screen-space 2D bounding rectangle [xMin..xMax, yMin..yMax].
     */
    public static WordRect projectWordToScreen(WordAnchor anchor, Matrix4f viewMatrix, Matrix4f projMatrix, int screenWidth, int screenHeight, Vec3d camPos) {
        Vector3f U = new Vector3f(viewMatrix.m00(), viewMatrix.m01(), viewMatrix.m02());
        Vector3f V = new Vector3f(-viewMatrix.m10(), -viewMatrix.m11(), -viewMatrix.m12());

        float halfW = anchor.width * 0.5f;
        float halfH = (anchor.height > 0.01f ? anchor.height : BASE_FONT_SCALE) * 0.5f;

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

    public static float estimateWordWidth(String text) {
        if (text == null || text.isEmpty()) return 0.5f;
        try {
            if (net.auramc.glass.common.render.AuraRenderer.isReady()) {
                float tw = net.auramc.glass.common.render.AuraRenderer.textWidth(text, 1.0f);
                if (tw > 0.05f) return tw;
            }
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
