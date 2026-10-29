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
 * - Above visible surface (raycast against world blocks)
 * - Horizontal layout facing player with motion anticipation
 */
public final class WordPlacement {

    public static final double MIN_DISTANCE = 3.0;
    public static final double MAX_DISTANCE = 7.0;
    public static final double DEAD_ZONE_ANGLE_DEG = 3.0;
    public static final double MAX_CONE_ANGLE_DEG = 12.0;

    public static final float BASE_FONT_SCALE = 0.35f; // Letter height ≈ 0.35 blocks per Claude VOZVRAT 2
    public static final float SPACE_GAP_BLOCKS = 0.25f; // Visible gap between words in world blocks

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

    private WordPlacement() {}

    /**
     * Places a group of words in the 3D world.
     */
    public static List<WordAnchor> layoutGroup(List<KaraokeModel.KaraokeWord> words,
                                               Vec3d eyePos,
                                               float playerYaw,
                                               float playerPitch,
                                               Vec3d playerVelocity,
                                               float gazeAngleFactor,
                                               World world) {
        List<WordAnchor> anchors = new ArrayList<>();
        if (words.isEmpty()) return anchors;

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

        // Camera horizontal right vector in Minecraft coordinate system
        // Looking South (yaw=0): look=(0,0,1), right=(-1,0,0) (West is camera right)
        Vec3d rightVec = new Vec3d(
                -Math.cos(yawRad),
                0,
                -Math.sin(yawRad)
        ).normalize();

        Vec3d upVec = rightVec.crossProduct(lookVec).normalize();

        // Crosshair cone angle: tilt downwards by 5.2°..6.5°
        // Center is safely > 3.0° dead zone and leaves budget for horizontal spread <= 12.0°
        double baseOffsetDeg = 5.2 * Math.max(0.75, gazeAngleFactor);
        double offsetRad = Math.toRadians(baseOffsetDeg);

        // Vector tilted downwards from look vector
        Vec3d coneDir = lookVec.multiply(Math.cos(offsetRad)).add(upVec.multiply(-Math.sin(offsetRad))).normalize();

        // Raycast against blocks to prevent spawning inside walls/ground
        double actualDistance = targetDistance;
        if (world != null) {
            Vec3d rayEnd = eyePos.add(coneDir.multiply(targetDistance + 0.8));
            BlockHitResult hit = world.raycast(new RaycastContext(
                    eyePos,
                    rayEnd,
                    RaycastContext.ShapeType.COLLIDER,
                    RaycastContext.FluidHandling.NONE,
                    MinecraftClient.getInstance().player
            ));
            if (hit.getType() != HitResult.Type.MISS) {
                double hitDist = hit.getPos().distanceTo(eyePos);
                actualDistance = Math.max(1.8, hitDist - 0.6);
            }
        }

        // Base group center
        Vec3d groupCenter = eyePos.add(coneDir.multiply(actualDistance));

        // Motion anticipation: offset ahead by player velocity
        if (playerVelocity != null && playerVelocity.lengthSquared() > 0.001) {
            double leadFactor = 8.0; // ~8 ticks forward
            Vec3d motionLead = playerVelocity.multiply(leadFactor);
            if (motionLead.length() > 1.2) {
                motionLead = motionLead.normalize().multiply(1.2);
            }
            groupCenter = groupCenter.add(motionLead);
        }

        // Calculate text widths in world blocks (scale = 0.35 blocks letter height)
        float wordScale = BASE_FONT_SCALE * config.getSize();
        float spaceGap = SPACE_GAP_BLOCKS * config.getSize();

        float[] widths = new float[words.size()];
        float rawTotalWidth = 0f;

        for (int i = 0; i < words.size(); i++) {
            widths[i] = estimateWordWidth(words.get(i).w) * wordScale;
            rawTotalWidth += widths[i];
            if (i > 0) rawTotalWidth += spaceGap;
        }

        // Left-to-right reading order: start at -rawTotalWidth * 0.5 along rightVec
        double startOffset = -rawTotalWidth * 0.5;
        double currentOffset = startOffset;

        for (int i = 0; i < words.size(); i++) {
            KaraokeModel.KaraokeWord w = words.get(i);
            float wWidth = widths[i];
            double wordCenterOffset = currentOffset + wWidth * 0.5;

            // Offset along rightVec: negative offset is LEFT on screen, positive is RIGHT
            Vec3d wordPos = groupCenter.add(rightVec.multiply(wordCenterOffset));

            // Elevate above ground if needed
            if (world != null) {
                wordPos = ensureAboveSurface(wordPos, world);
            }

            // Word faces player
            Vec3d toEye = eyePos.subtract(wordPos);
            float wYaw = (float) Math.toDegrees(Math.atan2(-toEye.x, toEye.z));
            float wPitch = (float) Math.toDegrees(Math.atan2(toEye.y, Math.sqrt(toEye.x * toEye.x + toEye.z * toEye.z)));

            anchors.add(new WordAnchor(w, wordPos, wWidth, wordScale, wYaw, wPitch));
            currentOffset += wWidth + spaceGap;
        }

        return anchors;
    }

    private static Vec3d ensureAboveSurface(Vec3d pos, World world) {
        BlockPos bp = BlockPos.ofFloored(pos.x, pos.y, pos.z);
        BlockState state = world.getBlockState(bp);
        if (!state.isAir()) {
            // Push upwards to clear air
            for (int dy = 1; dy <= 4; dy++) {
                BlockPos above = bp.up(dy);
                if (world.getBlockState(above).isAir()) {
                    return new Vec3d(pos.x, above.getY() + 0.1, pos.z);
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
        // In Onest MSDF font, average character advance is ~0.55 EM
        return Math.max(0.6f, text.length() * 0.55f);
    }

    /**
     * Verifies that a point is outside the 3° dead zone around the gaze line.
     */
    public static boolean isOutsideDeadZone(Vec3d point, Vec3d eyePos, Vec3d lookVec) {
        Vec3d toPoint = point.subtract(eyePos);
        double dist = toPoint.length();
        if (dist < 0.001) return false;
        double cosAngle = toPoint.dotProduct(lookVec) / dist;
        cosAngle = Math.max(-1.0, Math.min(1.0, cosAngle));
        double angleDeg = Math.toDegrees(Math.acos(cosAngle));
        return angleDeg >= DEAD_ZONE_ANGLE_DEG;
    }

    /**
     * Verifies that the group center is positioned inside the 4–12° gaze cone.
     */
    public static boolean isInsideCone(Vec3d point, Vec3d eyePos, Vec3d lookVec) {
        Vec3d toPoint = point.subtract(eyePos);
        double dist = toPoint.length();
        if (dist < 0.001) return false;
        double cosAngle = toPoint.dotProduct(lookVec) / dist;
        cosAngle = Math.max(-1.0, Math.min(1.0, cosAngle));
        double angleDeg = Math.toDegrees(Math.acos(cosAngle));
        return angleDeg <= MAX_CONE_ANGLE_DEG;
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
        double baseOffsetDeg = 5.2 * Math.max(0.75, gazeAngleFactor);
        double offsetRad = Math.toRadians(baseOffsetDeg);
        Vec3d coneDir = lookVec.multiply(Math.cos(offsetRad)).add(upVec.multiply(-Math.sin(offsetRad))).normalize();
        return eyePos.add(coneDir.multiply(distance));
    }
}
