package net.auramc.glass.client.quickmenu.lyrics;

import java.util.ArrayList;
import java.util.List;
import java.util.Random;
import java.util.function.LongSupplier;

public final class KaraokeEngine {
    public static final int MAX_BODIES = 32;
    public static final long SCHEDULE_NANOS = 100_000_000L, PHYSICS_NANOS = 50_000_000L;
    public static final long LEAD_MS = 180;
    private final LongSupplier audioClock;
    private final CollisionWorld world;
    private final Random random;
    private final List<Body> bodies = new ArrayList<>(MAX_BODIES);
    private LyricsTrack track = LyricsTrack.parse("");
    private long lastSchedule = Long.MIN_VALUE, lastPhysics = Long.MIN_VALUE, lastAudio = Long.MIN_VALUE;
    private int cursor, outputLatency, userOffset, groupSize;
    private long lastUpdate;
    private ListenerState listener;
    private boolean gate;
    private double spawnDistance = 5;
    private int density = 1;
    private boolean physicsEnabled = true;
    public record Vec3(double x, double y, double z) {
        public Vec3 add(double x, double y, double z) { return new Vec3(this.x + x, this.y + y, this.z + z); }
    }
    public record Aabb(double minX, double minY, double minZ, double maxX, double maxY, double maxZ) {}
    public interface CollisionWorld {
        boolean blocked(Aabb box);
        default boolean visible(Vec3 from, Vec3 to) { return true; }
    }
    public record ListenerState(double x, double y, double z, double yawDegrees,
                                double speedBlocksPerTick, double yawDegreesPerSecond,
                                boolean sprinting, boolean jumping, boolean flying, boolean falling,
                                boolean guiOpen, boolean submerged, boolean inLava,
                                double health, long lastCombatNanos, boolean playing) {}
    public enum Phase { FLOATING, FALLING, RESTING, FADING }
    public record BodySnapshot(LyricsTrack.Word word, Vec3 position, Vec3 previousPosition,
                               double scale, double alpha, double yawDegrees, double rotationDegrees,
                               int revealedLetters, boolean sung, Phase phase) {}
    private static final class Body {
        LyricsTrack.Word word; Vec3 position, previous; double vx, vy, vz, rotation, spin, yaw;
        long releaseAtMs, restAtNanos, fadeAtNanos; double ttl; Phase phase = Phase.FLOATING;
    }
    public KaraokeEngine(LongSupplier audioClock, CollisionWorld world, long seed) {
        this.audioClock = java.util.Objects.requireNonNull(audioClock);
        this.world = java.util.Objects.requireNonNull(world); random = new Random(seed);
    }
    public void setTrack(LyricsTrack track) { this.track = java.util.Objects.requireNonNull(track); clear(); }
    public void configure(int outputLatencyMs, int userOffsetMs) {
        outputLatency = Math.max(0, Math.min(2000, outputLatencyMs)); userOffset = Math.max(-500, Math.min(500, userOffsetMs));
    }
    public void configureSpatial(double distance, int density, boolean physics) {
        spawnDistance = Double.isFinite(distance) ? Math.max(3, Math.min(7, distance)) : 5;
        this.density = Math.max(0, Math.min(2, density)); physicsEnabled = physics;
    }
    public long audioTimeMs() { return audioClock.getAsLong() - outputLatency + userOffset; }
    public int groupSize() { return groupSize; }
    public boolean spawningAllowed() { return gate; }
    public void clear() { bodies.clear(); cursor = 0; lastSchedule = lastPhysics = lastAudio = Long.MIN_VALUE; }
    public void update(long now, ListenerState state) {
        listener = state; lastUpdate = now;
        if (!state.playing()) { bodies.clear(); lastSchedule = lastPhysics = Long.MIN_VALUE; lastAudio = audioTimeMs(); return; }
        long audio = audioTimeMs();
        if (lastAudio != Long.MIN_VALUE && (audio < lastAudio - 250 || audio - lastAudio > 2000)) {
            bodies.clear(); cursor = lowerBound(audio - 60); lastSchedule = Long.MIN_VALUE;
        }
        lastAudio = audio;
        if (lastSchedule == Long.MIN_VALUE || now - lastSchedule >= SCHEDULE_NANOS) {
            lastSchedule = now; gate = allowed(now, state); groupSize = budget(state);
            schedule(audio, now, state);
        }
        if (lastPhysics == Long.MIN_VALUE) lastPhysics = now;
        int steps = 0;
        while (now - lastPhysics >= PHYSICS_NANOS && steps++ < 5) {
            lastPhysics += PHYSICS_NANOS; physics(audio, lastPhysics);
        }
        if (steps > 5) lastPhysics = now;
    }
    private boolean allowed(long now, ListenerState state) {
        return !state.guiOpen() && !state.submerged() && !state.inLava() && state.health() > 6
                && Math.abs(state.yawDegreesPerSecond()) <= 200
                && (state.lastCombatNanos() <= 0 || now - state.lastCombatNanos() >= 3_000_000_000L);
    }
    private int budget(ListenerState s) {
        if (!gate) return 0;
        if (s.flying() || s.falling()) return 1;
        if (Math.abs(s.yawDegreesPerSecond()) > 90 || s.sprinting() || s.jumping() || s.speedBlocksPerTick() > .14) return densityBudget(1, 2);
        if (s.speedBlocksPerTick() < .05 && Math.abs(s.yawDegreesPerSecond()) < 30) return densityBudget(3, 6);
        return densityBudget(2, 3);
    }
    private int densityBudget(int minimum, int maximum) {
        if (density == 0) return minimum;
        if (density == 2) return maximum;
        return minimum + Math.floorMod(currentPhrase(), maximum - minimum + 1);
    }
    private int currentPhrase() { return cursor < track.words().size() ? track.words().get(cursor).phrase() : 0; }
    private void schedule(long audio, long now, ListenerState state) {
        var words = track.words();
        while (cursor < words.size() && words.get(cursor).startMs() < audio - 60) cursor++;
        int floating = (int) bodies.stream().filter(body -> body.phase == Phase.FLOATING && body.word.endMs() > audio).count();
        while (cursor < words.size() && words.get(cursor).startMs() <= audio + LEAD_MS) {
            var word = words.get(cursor++);
            if (!gate || floating >= groupSize) continue;
            Body body = spawn(word, state);
            if (body == null) continue;
            long phraseEnd = word.endMs();
            for (int i = word.index() + 1; i < words.size() && words.get(i).phrase() == word.phrase(); i++) phraseEnd = Math.max(phraseEnd, words.get(i).endMs());
            body.releaseAtMs = phraseEnd + 300 + random.nextInt(301);
            body.ttl = 2 + random.nextDouble() * 2;
            if (bodies.size() >= MAX_BODIES - 4) {
                for (Body old : bodies) if (old.phase != Phase.FADING) { fade(old, now); break; }
            }
            if (bodies.size() == MAX_BODIES) bodies.remove(0);
            bodies.add(body); floating++;
        }
        if (!gate) for (Body body : bodies) if (body.phase == Phase.FLOATING) fade(body, now);
    }
    private Body spawn(LyricsTrack.Word word, ListenerState s) {
        Vec3 eye = new Vec3(s.x(), s.y(), s.z());
        for (int attempt = 0; attempt < 12; attempt++) {
            double angle = 4 + random.nextDouble() * (Math.abs(s.yawDegreesPerSecond()) > 90 ? 2 : 8);
            angle *= random.nextBoolean() ? 1 : -1;
            double yaw = Math.toRadians(s.yawDegrees() + angle), distance = Math.max(3, Math.min(7, spawnDistance + (random.nextDouble() - .5) * 1.5));
            Vec3 pos = eye.add(-Math.sin(yaw) * distance, -.12 + random.nextDouble() * .24, Math.cos(yaw) * distance);
            double halfAngle = Math.toDegrees(Math.atan((box(pos, word).maxX() - box(pos, word).minX()) * .5 / distance));
            if (Math.abs(angle) - halfAngle < 3 || world.blocked(box(pos, word)) || overlapsBody(box(pos, word)) || !world.visible(eye, pos)) continue;
            Body body = new Body(); body.word = word; body.position = body.previous = pos; body.yaw = s.yawDegrees();
            body.vx = (random.nextDouble() - .5) * .5; body.vz = (random.nextDouble() - .5) * .5;
            body.spin = (random.nextDouble() - .5) * 45;
            return body;
        }
        return null;
    }
    private void physics(long audio, long now) {
        for (Body body : bodies) {
            body.previous = body.position;
            if (body.phase == Phase.FLOATING) {
                if (audio >= body.releaseAtMs) {
                    if (!physicsEnabled) { fade(body, now); continue; }
                    body.phase = Phase.FALLING; body.vy = .3 + random.nextDouble() * .5;
                }
                else continue;
            }
            if (body.phase == Phase.FADING) continue;
            if (!physicsEnabled) { fade(body, now); continue; }
            if (body.phase == Phase.RESTING) {
                if (now - body.restAtNanos >= (long) (body.ttl * 1_000_000_000L)) fade(body, now);
                continue;
            }
            body.vy -= 9.8 * .05;
            double dx = body.vx * .05, dy = body.vy * .05, dz = body.vz * .05;
            int substeps = Math.max(1, (int) Math.ceil(Math.max(Math.abs(dy), Math.max(Math.abs(dx), Math.abs(dz))) / .08));
            boolean grounded = false;
            for (int step = 0; step < substeps; step++) {
                Vec3 x = body.position.add(dx / substeps, 0, 0);
                if (world.blocked(box(x, body.word))) { body.vx *= -.28; dx = 0; } else body.position = x;
                Vec3 y = body.position.add(0, dy / substeps, 0);
                if (world.blocked(box(y, body.word))) {
                    grounded |= body.vy < 0; body.vy *= -.28; dy = 0;
                    body.vx *= .72; body.vz *= .72;
                } else body.position = y;
                Vec3 z = body.position.add(0, 0, dz / substeps);
                if (world.blocked(box(z, body.word))) { body.vz *= -.28; dz = 0; } else body.position = z;
            }
            body.rotation += body.spin * .05; body.spin *= .98;
            if (grounded && Math.abs(body.vy) < .16 && Math.hypot(body.vx, body.vz) < .08) {
                body.phase = Phase.RESTING; body.restAtNanos = now; body.vx = body.vy = body.vz = body.spin = 0;
            }
            if (listener != null && body.position.y() < listener.y() - 30) fade(body, now);
        }
        bodies.removeIf(body -> body.phase == Phase.FADING && now - body.fadeAtNanos >= 400_000_000L);
    }
    private boolean overlapsBody(Aabb candidate) {
        for (Body body : bodies) {
            Aabb other = box(body.position, body.word);
            if (candidate.minX() < other.maxX() && candidate.maxX() > other.minX()
                    && candidate.minY() < other.maxY() && candidate.maxY() > other.minY()
                    && candidate.minZ() < other.maxZ() && candidate.maxZ() > other.minZ()) return true;
        }
        return false;
    }
    private static Aabb box(Vec3 p, LyricsTrack.Word word) {
        double width = Math.min(.9, Math.max(.12, word.text().codePointCount(0, word.text().length()) * .035));
        return new Aabb(p.x() - width * .5, p.y() - .07, p.z() - .045, p.x() + width * .5, p.y() + .07, p.z() + .045);
    }
    private static void fade(Body body, long now) { body.phase = Phase.FADING; body.fadeAtNanos = now; }
    private int lowerBound(long time) {
        int lo = 0, hi = track.words().size();
        while (lo < hi) { int mid = (lo + hi) >>> 1; if (track.words().get(mid).startMs() < time) lo = mid + 1; else hi = mid; }
        return lo;
    }
    public List<BodySnapshot> bodies() {
        if (listener == null || !listener.playing() || bodies.isEmpty()) return List.of();
        long audio = lastAudio, now = lastUpdate;
        double interpolation = lastSchedule == Long.MIN_VALUE ? 1 : Math.max(0, Math.min(1, (lastUpdate - lastPhysics) / (double) PHYSICS_NANOS));
        var snapshots = new ArrayList<BodySnapshot>(bodies.size());
        for (Body body : bodies) {
            double age = Math.max(0, audio - (body.word.startMs() - LEAD_MS)) / 1000.0;
            // Response .18s is the natural period; dampingRatio.8 preserves a brief physical overshoot.
            double omega = 2 * Math.PI / .18, damping = .8, damped = omega * Math.sqrt(1 - damping * damping);
            double response = 1 - Math.exp(-damping * omega * age) * (Math.cos(damped * age) + damping * omega / damped * Math.sin(damped * age));
            double alpha = body.phase == Phase.FADING ? Math.max(0, 1 - (now - body.fadeAtNanos) / 400_000_000.0) : 1;
            int letters = body.word.text().codePointCount(0, body.word.text().length());
            int revealed = Math.min(letters, (int) Math.max(0, (audio - body.word.startMs() + LEAD_MS) / 20));
            Vec3 p = body.phase == Phase.FLOATING ? body.position : new Vec3(body.previous.x() + (body.position.x() - body.previous.x()) * interpolation, body.previous.y() + (body.position.y() - body.previous.y()) * interpolation, body.previous.z() + (body.position.z() - body.previous.z()) * interpolation);
            snapshots.add(new BodySnapshot(body.word, p, body.previous, (.6 + .4 * response) * alpha, alpha, Math.toDegrees(Math.atan2(listener.x() - p.x(), listener.z() - p.z())), body.rotation, revealed, audio >= body.word.endMs(), body.phase));
        }
        return List.copyOf(snapshots);
    }
}