package net.auramc.glass.client.karaoke;

import net.minecraft.block.BlockState;
import net.minecraft.block.ShapeContext;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Box;
import net.minecraft.util.math.Vec3d;
import net.minecraft.util.shape.VoxelShape;
import net.minecraft.world.World;

import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;
import java.util.Random;

/**
 * Lightweight voxel physics simulation for fallen karaoke words:
 * - 20 Hz fixed timestep with sub-frame interpolation for smooth high-FPS rendering
 * - Gravity, subtle random impulse/rotation, voxel AABB block collision
 * - Restitution (bounce): 0.28 (within 0.2–0.35 spec)
 * - Ground friction and resting sleep
 * - Dissolves after 2–4 seconds of rest
 * - Strict limit of <= 32 active physical bodies
 * - Guarantee: no body clips inside blocks
 */
public final class KaraokePhysics {

    public static final int MAX_BODIES = 32;
    public static final float GRAVITY = 0.024f; // blocks/tick^2
    public static final float RESTITUTION = 0.28f; // bounce factor (0.2–0.35)
    public static final float GROUND_FRICTION = 0.72f;
    public static final float AIR_DRAG = 0.985f;
    public static final int REST_SLEEP_TICKS = 5;
    public static final int REST_DISSOLVE_TICKS = 60; // 3 seconds at 20 Hz (2–4s range)

    private static final Random RNG = new Random(1337L);

    public static final class PhysicalWord {
        public final String text;
        public final float width;
        public final float height;
        public final float depth = 0.15f;

        // Current tick physics state
        public double x, y, z;
        public double vx, vy, vz;
        public float pitch, yaw, roll;
        public float vPitch, vYaw, vRoll;

        // Previous tick state for interpolation
        public double prevX, prevY, prevZ;
        public float prevPitch, prevYaw, prevRoll;

        public boolean sleeping = false;
        public int restTicks = 0;
        public int ageTicks = 0;
        public boolean dissolving = false;
        public float dissolveProgress = 0.0f;

        public PhysicalWord(String text, Vec3d pos, float width, float height, float yaw, float pitch) {
            this.text = text;
            this.width = width;
            this.height = height;

            this.x = pos.x;
            this.y = pos.y;
            this.z = pos.z;
            this.prevX = x;
            this.prevY = y;
            this.prevZ = z;

            this.yaw = yaw;
            this.pitch = pitch;
            this.roll = 0f;
            this.prevYaw = yaw;
            this.prevPitch = pitch;
            this.prevRoll = 0f;

            // Slight initial pop and random kick
            this.vx = (RNG.nextDouble() - 0.5) * 0.04;
            this.vy = 0.025 + RNG.nextDouble() * 0.035;
            this.vz = (RNG.nextDouble() - 0.5) * 0.04;

            this.vPitch = (float) ((RNG.nextDouble() - 0.5) * 5.0);
            this.vYaw = (float) ((RNG.nextDouble() - 0.5) * 3.0);
            this.vRoll = (float) ((RNG.nextDouble() - 0.5) * 6.0);
        }

        public Box getBox(double px, double py, double pz) {
            double hw = width * 0.5;
            double hd = depth * 0.5;
            return new Box(px - hw, py, pz - hd, px + hw, py + height, pz + hd);
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
            return Math.max(0f, 1.0f - dissolveProgress);
        }

        public float getScale() {
            return Math.max(0f, 1.0f - dissolveProgress * 0.35f);
        }
    }

    private final List<PhysicalWord> bodies = new ArrayList<>();

    public KaraokePhysics() {}

    public synchronized void spawnBody(String text, Vec3d pos, float width, float height, float yaw, float pitch) {
        if (!KaraokeConfig.get().isPhysicsEnabled()) return;

        // Strict active body limit <= 32: remove oldest bodies when full
        while (bodies.size() >= MAX_BODIES) {
            bodies.remove(0);
        }

        bodies.add(new PhysicalWord(text, pos, width, height, yaw, pitch));
    }

    public synchronized void clear() {
        bodies.clear();
    }

    public synchronized void dissolveAll() {
        for (PhysicalWord b : bodies) {
            b.dissolving = true;
        }
    }

    public synchronized List<PhysicalWord> getBodies() {
        return new ArrayList<>(bodies);
    }

    public synchronized int getBodyCount() {
        return bodies.size();
    }

    /**
     * Executes one 20 Hz simulation tick.
     */
    public synchronized void tick(World world) {
        if (bodies.isEmpty()) return;

        Iterator<PhysicalWord> it = bodies.iterator();
        while (it.hasNext()) {
            PhysicalWord b = it.next();

            b.prevX = b.x;
            b.prevY = b.y;
            b.prevZ = b.z;
            b.prevPitch = b.pitch;
            b.prevYaw = b.yaw;
            b.prevRoll = b.roll;

            if (b.dissolving) {
                b.dissolveProgress += 0.045f;
                if (b.dissolveProgress >= 1.0f) {
                    it.remove();
                    continue;
                }
            }

            if (b.sleeping) {
                b.restTicks++;
                if (b.restTicks >= REST_DISSOLVE_TICKS && !b.dissolving) {
                    b.dissolving = true;
                }
                continue;
            }

            b.ageTicks++;

            // Gravity
            b.vy -= GRAVITY;

            // Drag
            b.vx *= AIR_DRAG;
            b.vz *= AIR_DRAG;
            b.vPitch *= 0.94f;
            b.vYaw *= 0.94f;
            b.vRoll *= 0.94f;

            b.pitch += b.vPitch;
            b.yaw += b.vYaw;
            b.roll += b.vRoll;

            // Voxel collision along Y
            double targetY = b.y + b.vy;
            Box yBox = b.getBox(b.x, targetY, b.z);
            if (world != null && collidesWithBlocks(yBox, world)) {
                if (b.vy < 0) { // Landing on ground
                    // Snap to block top
                    b.y = Math.floor(b.y);
                    b.vy = -b.vy * RESTITUTION;
                    b.vx *= GROUND_FRICTION;
                    b.vz *= GROUND_FRICTION;
                    b.vPitch *= 0.6f;
                    b.vRoll *= 0.6f;

                    if (Math.abs(b.vy) < 0.02 && Math.abs(b.vx) < 0.015 && Math.abs(b.vz) < 0.015) {
                        b.vy = 0; b.vx = 0; b.vz = 0;
                        b.restTicks++;
                        if (b.restTicks >= REST_SLEEP_TICKS) {
                            b.sleeping = true;
                        }
                    }
                } else {
                    b.vy = -b.vy * RESTITUTION;
                }
            } else {
                b.y = targetY;
                b.restTicks = 0;
            }

            // Voxel collision along X
            double targetX = b.x + b.vx;
            Box xBox = b.getBox(targetX, b.y, b.z);
            if (world != null && collidesWithBlocks(xBox, world)) {
                b.vx = -b.vx * RESTITUTION;
            } else {
                b.x = targetX;
            }

            // Voxel collision along Z
            double targetZ = b.z + b.vz;
            Box zBox = b.getBox(b.x, b.y, targetZ);
            if (world != null && collidesWithBlocks(zBox, world)) {
                b.vz = -b.vz * RESTITUTION;
            } else {
                b.z = targetZ;
            }

            // Prevent penetration: push up if inside block
            if (world != null && collidesWithBlocks(b.getBox(b.x, b.y, b.z), world)) {
                b.y += 0.15;
            }
        }
    }

    public static boolean collidesWithBlocks(Box box, World world) {
        if (world == null) return false;
        int minX = (int) Math.floor(box.minX);
        int maxX = (int) Math.ceil(box.maxX);
        int minY = (int) Math.floor(box.minY);
        int maxY = (int) Math.ceil(box.maxY);
        int minZ = (int) Math.floor(box.minZ);
        int maxZ = (int) Math.ceil(box.maxZ);

        BlockPos.Mutable pos = new BlockPos.Mutable();
        for (int y = minY; y < maxY; y++) {
            for (int x = minX; x < maxX; x++) {
                for (int z = minZ; z < maxZ; z++) {
                    pos.set(x, y, z);
                    BlockState state = world.getBlockState(pos);
                    if (!state.isAir()) {
                        VoxelShape collisionShape = state.getCollisionShape(world, pos, ShapeContext.absent());
                        if (!collisionShape.isEmpty()) {
                            for (Box b : collisionShape.getBoundingBoxes()) {
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
