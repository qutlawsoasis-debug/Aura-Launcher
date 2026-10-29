package net.auramc.glass.client.karaoke;

import net.minecraft.block.BlockState;
import net.minecraft.client.MinecraftClient;
import net.minecraft.util.hit.BlockHitResult;
import net.minecraft.util.hit.HitResult;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.RaycastContext;
import net.minecraft.world.World;

import java.util.ArrayList;
import java.util.List;

/**
 * Calculates 3D world placement of karaoke word groups:
 * - Inside crosshair cone (4–12° from gaze direction)
 * - Dead zone: strictly outside 3° around crosshair center
 * - Distance: 3–7 blocks
 * - Above visible surface (raycast against world blocks, in front of walls)
 * - Multi-line and width-bounded layout: anchors and word bounds never exceed 12.0°
 * - Left-to-right reading order with visible space gaps
 */
public final class WordPlacement {

    public static final double MIN_DISTANCE = 3.0;
    public static final double MAX_DISTANCE = 7.0;
    public static final double DEAD_ZONE_ANGLE_DEG = 3.0;
    public static final double MIN_CONE_ANGLE_DEG = 4.0;
    public static final double MAX_CONE_ANGLE_DEG = 12.0;

    public static final float BASE_FONT_SCALE = 0.35f; // Letter height ≈ 0.35 blocks per Claude VOZVRAT 2
    public static final float SPACE_GAP_BLOCKS = 0.20f; // Visible gap between words in world blocks

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
        final int tiltIndex;
        final double tiltDeg;
        final List<WordLayoutItem> items = new ArrayList<>();

        LayoutLine(int tiltIndex, double tiltDeg) {
            this.tiltIndex = tiltIndex;
            this.tiltDeg = tiltDeg;
        }
    }

    private WordPlacement() {}

    /**
     * Places words in the 3D world ensuring all anchors and word boundaries fit within 4–12° cone.
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
        double targetDistance = Math.max(MIN_DISTANCE, Math.min(MAX_DISTANCE, config.getDistance()));

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
        float spaceGap = SPACE_GAP_BLOCKS * config.getSize();

        // Vertical tilt angles for multi-line layout within [4.0°, 12.0°] cone
        double factor = Math.max(0.75, gazeAngleFactor);
        double[] tilts = new double[] {
                5.0 * factor,
                7.0 * factor,
                9.0 * factor,
                11.0 * factor
        };

        // Safety limit angle: 11.4° leaves budget for letter geometry inside 12.0°
        double safeLimitDeg = 11.4;
        double cosSafeLimit = Math.cos(Math.toRadians(safeLimitDeg));

        // Pack words into lines
        List<LayoutLine> lines = new ArrayList<>();
        int currentTiltIndex = 0;
        LayoutLine currentLine = new LayoutLine(currentTiltIndex, tilts[currentTiltIndex]);

        for (KaraokeModel.KaraokeWord w : words) {
            double tilt = tilts[currentTiltIndex];
            double cosTilt = Math.cos(Math.toRadians(tilt));
            double ratio = (cosTilt / cosSafeLimit);
            double xMax = actualDistance * Math.sqrt(Math.max(0.0, ratio * ratio - 1.0));
            double maxLineWidth = 2.0 * xMax;

            float rawWordWidth = estimateWordWidth(w.w) * baseFontScale;
            float wordScale = baseFontScale;
            float wordWidth = rawWordWidth;

            // If a single word is wider than the line width limit, clamp its scale so it fits
            if (wordWidth > maxLineWidth) {
                wordScale = (float) (baseFontScale * (maxLineWidth / wordWidth));
                wordWidth = (float) maxLineWidth;
            }

            // Calculate current line width
            double lineTotalWidth = 0.0;
            for (WordLayoutItem item : currentLine.items) {
                lineTotalWidth += item.width;
            }
            if (!currentLine.items.isEmpty()) {
                lineTotalWidth += currentLine.items.size() * spaceGap;
            }

            // Check if word fits on current line
            if (!currentLine.items.isEmpty() && (lineTotalWidth + spaceGap + wordWidth > maxLineWidth)) {
                // Word does not fit on current line: advance to next line if available
                if (currentTiltIndex + 1 < tilts.length) {
                    lines.add(currentLine);
                    currentTiltIndex++;
                    currentLine = new LayoutLine(currentTiltIndex, tilts[currentTiltIndex]);
                    tilt = tilts[currentTiltIndex];
                    cosTilt = Math.cos(Math.toRadians(tilt));
                    ratio = (cosTilt / cosSafeLimit);
                    xMax = actualDistance * Math.sqrt(Math.max(0.0, ratio * ratio - 1.0));
                    maxLineWidth = 2.0 * xMax;

                    if (rawWordWidth > maxLineWidth) {
                        wordScale = (float) (baseFontScale * (maxLineWidth / rawWordWidth));
                        wordWidth = (float) maxLineWidth;
                    }
                } else {
                    // All available lines are full; remaining words will be placed in the next group
                    break;
                }
            }

            currentLine.items.add(new WordLayoutItem(w, wordWidth, wordScale));
        }

        if (!currentLine.items.isEmpty()) {
            lines.add(currentLine);
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
            double tiltRad = Math.toRadians(line.tiltDeg);
            Vec3d lineDir = lookVec.multiply(Math.cos(tiltRad)).add(upVec.multiply(-Math.sin(tiltRad))).normalize();
            Vec3d lineCenter = eyePos.add(lineDir.multiply(actualDistance)).add(motionLead);

            double totalLineWidth = 0.0;
            for (WordLayoutItem it : line.items) {
                totalLineWidth += it.width;
            }
            if (line.items.size() > 1) {
                totalLineWidth += (line.items.size() - 1) * spaceGap;
            }

            double startX = -totalLineWidth * 0.5;
            double curX = startX;

            for (WordLayoutItem item : line.items) {
                double wordCenterX = curX + item.width * 0.5;
                Vec3d wordPos = lineCenter.add(rightVec.multiply(wordCenterX));

                if (world != null) {
                    wordPos = ensureAboveSurface(wordPos, world);
                }

                Vec3d toEye = eyePos.subtract(wordPos);
                float wYaw = (float) Math.toDegrees(Math.atan2(-toEye.x, toEye.z));
                float wPitch = (float) Math.toDegrees(Math.atan2(toEye.y, Math.sqrt(toEye.x * toEye.x + toEye.z * toEye.z)));

                anchors.add(new WordAnchor(item.word, wordPos, item.width, item.scale, wYaw, wPitch));
                curX += item.width + spaceGap;
            }
        }

        return anchors;
    }

    private static Vec3d ensureAboveSurface(Vec3d pos, World world) {
        BlockPos bp = BlockPos.ofFloored(pos.x, pos.y, pos.z);
        BlockState state = world.getBlockState(bp);
        // If inside non-air (blocks or vegetation/tall grass), elevate above it
        if (!state.isAir()) {
            for (int dy = 1; dy <= 4; dy++) {
                BlockPos above = bp.up(dy);
                BlockState aboveState = world.getBlockState(above);
                if (aboveState.isAir()) {
                    return new Vec3d(pos.x, above.getY() + 0.8, pos.z);
                }
            }
        }
        return pos;
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
        double baseOffsetDeg = 5.0 * Math.max(0.75, gazeAngleFactor);
        double offsetRad = Math.toRadians(baseOffsetDeg);
        Vec3d coneDir = lookVec.multiply(Math.cos(offsetRad)).add(upVec.multiply(-Math.sin(offsetRad))).normalize();
        return eyePos.add(coneDir.multiply(distance));
    }
}
