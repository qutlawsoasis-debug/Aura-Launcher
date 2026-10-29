package net.auramc.glass.client.karaoke;

import net.minecraft.block.BlockState;
import net.minecraft.block.ShapeContext;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Box;
import net.minecraft.util.math.Vec3d;
import net.minecraft.util.shape.VoxelShape;
import net.minecraft.world.World;
import org.joml.Vector3f;

import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;
import java.util.Random;

/**
 * 20 Hz voxel physics simulation for fallen karaoke letters (MiSide style):
 * - Individual letter detachment after 0.35 s word hold with 0–60 ms random jitter
 * - Initial velocities: upward 0.6–1.2 bl/s, lateral ±0.5 bl/s, towards player 0
 * - Gravity: 9.8 bl/s² (0.0245 bl/tick²), air drag 0.4/s (0.98/tick)
 * - Rotation: ±90…240°/s around random axis
 * - Collision with terrain blocks (AABB 0.6 * size): restitution 0.25, friction 0.7
 * - Resting on ground: 1.2 s (24 ticks), then dissolves alpha 1->0, scale -> 0.85 in 0.4 s (8 ticks)
 * - Void check: if no ground within 6 blocks below spawn, disappears after 1.5 s (30 ticks)
 * - Active limit: <= 160 live letters
 * - Rendered behind active line; on screen intersection with active line, alpha drops to 0.25
 */
public final class KaraokePhysics {

    public static final int MAX_LETTERS = 160;
    public static final float GRAVITY_TICK = 0.0245f;      // 9.8 blocks/s^2 at 20 Hz
    public static final float AIR_DRAG_TICK = 0.980f;      // 0.4 / s drag factor per tick
    public static final float RESTITUTION = 0.25f;         // bounce factor 0.25
    public static final float GROUND_FRICTION_FACTOR = 0.30f; // friction 0.7 -> retains 0.3 of lateral velocity
    public static final int GROUND_REST_TICKS = 24;        // 1.2 s at 20 Hz
    public static final int DISSOLVE_TICKS = 8;            // 0.4 s at 20 Hz
    public static final int VOID_EXPIRATION_TICKS = 30;    // 1.5 s at 20 Hz when no ground below

    private static final Random RNG = new Random(1337L);

    public static final class PhysicalLetter {
        public final char ch;
        public final float baseSize;
        public final long spawnTimeMs;
        public final Vec3d spawnPos;
        public boolean hasGroundBelow;

        // Current tick physics state
        public double x, y, z;
        public double vx, vy, vz;
        public float pitch, yaw, roll;
        public float vPitch, vYaw, vRoll;

        // Previous tick state for sub-frame interpolation
        public double prevX, prevY, prevZ;
        public float prevPitch, prevYaw, prevRoll;

        public boolean sleeping = false;
        public int restTicks = 0;
        public int fallTicks = 0;
        public int dissolveTicks = 0;
        public boolean dissolving = false;
        public boolean expired = false;

        public float currentAlpha = 0.70f; // Falling letters alpha 0.70, on ground 0.85 (Return 1)
        public float currentScale = 1.0f;
        public boolean isOccluded = false;

        public PhysicalLetter(char ch, Vec3d pos, Vector3f billboardU, Vector3f billboardV, float size, long spawnTimeMs, World world) {
            this.ch = ch;
            this.baseSize = size;
            this.spawnTimeMs = spawnTimeMs > 0L ? spawnTimeMs : System.currentTimeMillis();
            this.spawnPos = pos;

            this.x = pos.x;
            this.y = pos.y;
            this.z = pos.z;
            this.prevX = x;
            this.prevY = y;
            this.prevZ = z;

            this.yaw = 0.0f;
            this.pitch = 0.0f;
            this.roll = 0.0f;
            this.prevYaw = 0.0f;
            this.prevPitch = 0.0f;
            this.prevRoll = 0.0f;

            // Initial velocities:
            // Upward: 0.6–1.2 bl/s -> 0.030–0.060 bl/tick
            double upSpeed = (0.6 + RNG.nextDouble() * 0.6) * 0.050;
            // Lateral in billboard plane: ±0.5 bl/s -> ±0.025 bl/tick
            double latSpeed = ((RNG.nextDouble() - 0.5) * 1.0) * 0.050;
            // Towards player: 0

            Vector3f u = billboardU != null ? billboardU : new Vector3f(1, 0, 0);
            this.vx = u.x * latSpeed;
            this.vy = upSpeed;
            this.vz = u.z * latSpeed;

            // Rotation: ±90…240°/s -> ±4.5…12.0°/tick (primarily roll / view axis)
            float rotSpeed = (float) ((90.0 + RNG.nextDouble() * 150.0) * (RNG.nextBoolean() ? 1.0 : -1.0) * 0.050);
            this.vRoll = rotSpeed;
            this.vPitch = (float) ((RNG.nextDouble() - 0.5) * 3.0);
            this.vYaw = (float) ((RNG.nextDouble() - 0.5) * 3.0);

            this.hasGroundBelow = checkGroundBelow(pos, world, 6);
        }

        private static boolean checkGroundBelow(Vec3d pos, World world, int maxBlocks) {
            if (world == null) return true;
            BlockPos.Mutable bp = new BlockPos.Mutable();
            int bx = (int) Math.floor(pos.x);
            int by = (int) Math.floor(pos.y);
            int bz = (int) Math.floor(pos.z);
            for (int dy = 1; dy <= maxBlocks; dy++) {
                bp.set(bx, by - dy, bz);
                if (!world.getBlockState(bp).isAir()) {
                    return true;
                }
            }
            return false;
        }

        public Box getBox(double px, double py, double pz) {
            double halfExtent = baseSize * 0.6 * 0.5;
            double halfDepth = 0.05;
            return new Box(
                    px - halfExtent, py - halfExtent, pz - halfDepth,
                    px + halfExtent, py + halfExtent, pz + halfDepth
            );
        }

        public Vec3d getInterpolatedPos(float tickDelta) {
            return new Vec3d(
                    prevX + (x - prevX) * tickDelta,
                    prevY + (y - prevY) * tickDelta,
                    prevZ + (z - prevZ) * tickDelta
            );
        }

        public float getInterpolatedYaw(float tickDelta) {
            return prevYaw + (yaw - prevYaw) * tickDelta;
        }

        public float getInterpolatedPitch(float tickDelta) {
            return prevPitch + (pitch - prevPitch) * tickDelta;
        }

        public float getInterpolatedRoll(float tickDelta) {
            return prevRoll + (roll - prevRoll) * tickDelta;
        }

        public float getAlpha() {
            if (isOccluded) {
                return 0.25f;
            }
            if (sleeping && !dissolving) {
                return 0.85f;
            }
            return currentAlpha;
        }

        public float getScale() {
            return currentScale * baseSize;
        }
    }

    // Backward-compatible PhysicalWord wrapper for unit tests & legacy callers
    public static final class PhysicalWord {
        public final String text;
        public double x, y, z;
        public double vx, vy, vz;
        public double prevX, prevY, prevZ;
        public float pitch, yaw, roll;
        public float prevPitch, prevYaw, prevRoll;
        public float vPitch, vYaw, vRoll;
        public int ageTicks = 0;
        public boolean expired = false;
        public boolean dissolving = false;

        public PhysicalWord(String text, Vec3d pos, float width, float height, float yaw, float pitch) {
            this.text = text;
            this.x = pos.x; this.y = pos.y; this.z = pos.z;
            this.prevX = x; this.prevY = y; this.prevZ = z;
            this.yaw = yaw; this.pitch = pitch; this.roll = 0f;
            this.prevYaw = yaw; this.prevPitch = pitch; this.prevRoll = 0f;
            this.vx = 0.0; this.vy = 0.04; this.vz = 0.0;
        }

        public Vec3d getInterpolatedPos(float tickDelta) {
            return new Vec3d(prevX + (x - prevX) * tickDelta, prevY + (y - prevY) * tickDelta, prevZ + (z - prevZ) * tickDelta);
        }

        public float getAlpha() { return 0.70f; }
        public float getScale() { return 1.0f; }
    }

    private final List<PhysicalLetter> letters = new ArrayList<>();
    private final List<PhysicalWord> bodies = new ArrayList<>();

    public KaraokePhysics() {}

    public synchronized void spawnLetter(char ch, Vec3d pos, Vector3f billboardU, Vector3f billboardV, float size, long spawnTimeMs, World world) {
        if (!KaraokeConfig.get().isPhysicsEnabled()) return;

        if (letters.size() >= MAX_LETTERS) {
            letters.remove(0); // Evict oldest
        }

        letters.add(new PhysicalLetter(ch, pos, billboardU, billboardV, size, spawnTimeMs, world));
    }

    public synchronized void spawnBody(String text, Vec3d pos, float width, float height, float yaw, float pitch, long spawnTimeMs) {
        if (!KaraokeConfig.get().isPhysicsEnabled()) return;
        if (bodies.size() >= 32) bodies.remove(0);
        bodies.add(new PhysicalWord(text, pos, width, height, yaw, pitch));
    }

    public synchronized void spawnBody(String text, Vec3d pos, float width, float height, float yaw, float pitch) {
        spawnBody(text, pos, width, height, yaw, pitch, System.currentTimeMillis());
    }

    public synchronized void clear() {
        letters.clear();
        bodies.clear();
    }

    public synchronized void dissolveAll() {
        for (PhysicalLetter l : letters) {
            l.dissolving = true;
        }
        bodies.clear();
    }

    public synchronized List<PhysicalLetter> getLetters() {
        return new ArrayList<>(letters);
    }

    public synchronized List<PhysicalWord> getBodies() {
        return new ArrayList<>(bodies);
    }

    public synchronized int getLetterCount() {
        return letters.size();
    }

    public synchronized int getBodyCount() {
        return letters.size() + bodies.size();
    }

    /**
     * Executes one 20 Hz simulation tick.
     */
    public synchronized void tick(World world) {
        // 1. Tick physical letters
        if (!letters.isEmpty()) {
            Iterator<PhysicalLetter> it = letters.iterator();
            while (it.hasNext()) {
                PhysicalLetter l = it.next();

                l.prevX = l.x;
                l.prevY = l.y;
                l.prevZ = l.z;
                l.prevPitch = l.pitch;
                l.prevYaw = l.yaw;
                l.prevRoll = l.roll;

                if (l.expired) {
                    it.remove();
                    continue;
                }

                l.fallTicks++;
                if (!l.hasGroundBelow && l.fallTicks >= VOID_EXPIRATION_TICKS && !l.sleeping) {
                    it.remove();
                    continue;
                }

                if (l.sleeping) {
                    l.restTicks++;
                    if (l.restTicks >= GROUND_REST_TICKS) {
                        l.dissolving = true;
                    }
                }

                if (l.dissolving) {
                    l.dissolveTicks++;
                    float p = Math.min(1.0f, (float) l.dissolveTicks / DISSOLVE_TICKS);
                    l.currentAlpha = (l.sleeping ? 0.85f : 0.70f) * (1.0f - p); // Return 1: letters on ground alpha 0.85
                    l.currentScale = 1.0f - 0.15f * p; // scale -> 0.85
                    if (l.dissolveTicks >= DISSOLVE_TICKS) {
                        it.remove();
                        continue;
                    }
                    if (l.sleeping) {
                        continue;
                    }
                } else if (l.sleeping) {
                    continue;
                }

                // Gravity (9.8 bl/s^2 -> 0.0245 bl/tick^2)
                l.vy -= GRAVITY_TICK;

                // Air drag (0.4/s -> 0.98/tick)
                l.vx *= AIR_DRAG_TICK;
                l.vz *= AIR_DRAG_TICK;
                l.vPitch *= 0.96f;
                l.vYaw *= 0.96f;
                l.vRoll *= 0.96f;

                l.pitch += l.vPitch;
                l.yaw += l.vYaw;
                l.roll += l.vRoll;

                // Voxel collision along Y
                double targetY = l.y + l.vy;
                Box yBox = l.getBox(l.x, targetY, l.z);
                if (world != null && collidesNearby(yBox, world, l.x, l.y, l.z)) {
                    if (l.vy < 0) { // Landing on ground
                        l.y = Math.floor(l.y + 0.15); // Snap to block top
                        l.vy = -l.vy * RESTITUTION;
                        l.vx *= GROUND_FRICTION_FACTOR;
                        l.vz *= GROUND_FRICTION_FACTOR;
                        l.vRoll *= 0.5f;

                        if (Math.abs(l.vy) < 0.02 && Math.abs(l.vx) < 0.015 && Math.abs(l.vz) < 0.015) {
                            l.vy = 0; l.vx = 0; l.vz = 0;
                            l.sleeping = true;
                            l.currentAlpha = 0.85f;
                        }
                    } else {
                        l.vy = -l.vy * RESTITUTION;
                    }
                } else {
                    l.y = targetY;
                }

                // Voxel collision along X
                double targetX = l.x + l.vx;
                Box xBox = l.getBox(targetX, l.y, l.z);
                if (world != null && collidesNearby(xBox, world, l.x, l.y, l.z)) {
                    l.vx = -l.vx * RESTITUTION;
                } else {
                    l.x = targetX;
                }

                // Voxel collision along Z
                double targetZ = l.z + l.vz;
                Box zBox = l.getBox(l.x, l.y, targetZ);
                if (world != null && collidesNearby(zBox, world, l.x, l.y, l.z)) {
                    l.vz = -l.vz * RESTITUTION;
                } else {
                    l.z = targetZ;
                }
            }
        }

        // 2. Tick legacy bodies (if any)
        if (!bodies.isEmpty()) {
            Iterator<PhysicalWord> it = bodies.iterator();
            while (it.hasNext()) {
                PhysicalWord b = it.next();
                b.ageTicks++;
                if (b.expired || b.ageTicks >= 24) {
                    it.remove();
                    continue;
                }
                b.prevX = b.x; b.prevY = b.y; b.prevZ = b.z;
                b.prevPitch = b.pitch; b.prevYaw = b.yaw; b.prevRoll = b.roll;
                b.vy -= GRAVITY_TICK;
                b.y += b.vy;
            }
        }
    }

    /**
     * Checks voxel collision strictly within radius 2 blocks of the letter position (W6d performance req).
     */
    public static boolean collidesNearby(Box box, World world, double cx, double cy, double cz) {
        if (world == null) return false;
        int minX = Math.max((int) Math.floor(box.minX), (int) Math.floor(cx) - 2);
        int maxX = Math.min((int) Math.ceil(box.maxX), (int) Math.floor(cx) + 2);
        int minY = Math.max((int) Math.floor(box.minY), (int) Math.floor(cy) - 2);
        int maxY = Math.min((int) Math.ceil(box.maxY), (int) Math.floor(cy) + 2);
        int minZ = Math.max((int) Math.floor(box.minZ), (int) Math.floor(cz) - 2);
        int maxZ = Math.min((int) Math.ceil(box.maxZ), (int) Math.floor(cz) + 2);

        BlockPos.Mutable pos = new BlockPos.Mutable();
        for (int y = minY; y < maxY; y++) {
            for (int x = minX; x < maxX; x++) {
                for (int z = minZ; z < maxZ; z++) {
                    pos.set(x, y, z);
                    BlockState state = world.getBlockState(pos);
                    if (!state.isAir()) {
                        VoxelShape shape = state.getCollisionShape(world, pos, ShapeContext.absent());
                        if (!shape.isEmpty()) {
                            for (Box b : shape.getBoundingBoxes()) {
                                if (box.intersects(b.offset(pos))) {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }
        return false;
    }
}
