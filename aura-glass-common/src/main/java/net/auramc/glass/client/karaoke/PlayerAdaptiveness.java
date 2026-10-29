package net.auramc.glass.client.karaoke;

/**
 * Evaluates player state every 100 ms with smoothing to dynamically adapt
 * word group size and placement according to player activity.
 *
 * Rules:
 * - Standing (speed < 0.05 bl/tick, turn < 30°/s): 3–6 words, phrase boundaries
 * - Walking (speed >= 0.05): 2–3 words
 * - Running / sprint or jump: 1–2 words
 * - Fast turn (> 90°/s): 1–2 words, closer to gaze center
 * - Extreme turn (> 200°/s): pause new words
 * - Combat (< 3s), GUI open, water/lava, low HP (<= 6 HP): NO words (paused)
 * - Flying / falling: 1 word
 * - Skipped words are never accumulated.
 */
public final class PlayerAdaptiveness {

    public enum ActivityState {
        BLOCKED_COMBAT,
        BLOCKED_GUI,
        BLOCKED_HAZARD,
        BLOCKED_LOW_HP,
        PAUSED_EXTREME_TURN,
        FLYING_FALLING,
        RUNNING_JUMPING,
        FAST_TURN,
        WALKING,
        STANDING
    }

    public static final class EvaluationResult {
        public final ActivityState state;
        public final boolean canSpawn;
        public final int minGroup;
        public final int maxGroup;
        public final float gazeAngleFactor; // 1.0 = normal (4-12°), 0.6 = closer to gaze

        public EvaluationResult(ActivityState state, boolean canSpawn, int minGroup, int maxGroup, float gazeAngleFactor) {
            this.state = state;
            this.canSpawn = canSpawn;
            this.minGroup = minGroup;
            this.maxGroup = maxGroup;
            this.gazeAngleFactor = gazeAngleFactor;
        }

        @Override
        public String toString() {
            return String.format("%s (canSpawn=%b, range=%d..%d, gazeFactor=%.2f)",
                    state, canSpawn, minGroup, maxGroup, gazeAngleFactor);
        }
    }

    private double prevX = 0, prevY = 0, prevZ = 0;
    private float prevYaw = 0, prevPitch = 0;
    private long lastEvalTimeMs = 0L;

    // Smoothed values
    private double smoothedSpeed = 0.0; // blocks/tick
    private double smoothedTurnRate = 0.0; // deg/sec

    // Player status flags
    private boolean isSprinting = false;
    private boolean isJumping = false;
    private boolean isFlying = false;
    private boolean isFalling = false;
    private boolean isInWaterOrLava = false;
    private boolean isGuiOpen = false;
    private float health = 20.0f;
    private long lastCombatTimeMs = -10000L;

    private EvaluationResult currentResult = new EvaluationResult(ActivityState.STANDING, true, 3, 6, 1.0f);

    public PlayerAdaptiveness() {}

    public void recordCombat(long nowMs) {
        this.lastCombatTimeMs = nowMs;
        evaluateState(nowMs);
    }

    public long getLastCombatTimeMs() {
        return lastCombatTimeMs;
    }

    public void setScriptedState(double speed, double turnRateDegSec, boolean sprint, boolean jump,
                                 boolean flying, boolean falling, boolean inWaterOrLava,
                                 boolean guiOpen, float health, long lastCombatTimeMs) {
        setScriptedState(speed, turnRateDegSec, sprint, jump, flying, falling, inWaterOrLava, guiOpen, health, lastCombatTimeMs, System.currentTimeMillis());
    }

    public void setScriptedState(double speed, double turnRateDegSec, boolean sprint, boolean jump,
                                 boolean flying, boolean falling, boolean inWaterOrLava,
                                 boolean guiOpen, float health, long lastCombatTimeMs, long nowMs) {
        this.smoothedSpeed = speed;
        this.smoothedTurnRate = turnRateDegSec;
        this.isSprinting = sprint;
        this.isJumping = jump;
        this.isFlying = flying;
        this.isFalling = falling;
        this.isInWaterOrLava = inWaterOrLava;
        this.isGuiOpen = guiOpen;
        this.health = health;
        this.lastCombatTimeMs = lastCombatTimeMs;
        evaluateState(nowMs);
    }

    /**
     * Updates telemetry from player instance.
     */
    public void updateTelemetry(double x, double y, double z, float yaw, float pitch,
                                boolean sprinting, boolean onGround, boolean flying, double vy,
                                boolean inWater, boolean inLava, boolean guiOpen, float hp, long nowMs) {
        if (lastEvalTimeMs <= 0L) {
            prevX = x; prevY = y; prevZ = z;
            prevYaw = yaw; prevPitch = pitch;
            lastEvalTimeMs = nowMs;
            health = hp;
            return;
        }

        long dtMs = nowMs - lastEvalTimeMs;
        if (dtMs < 80L) {
            return; // Evaluate at ~100 ms rate
        }

        double dtSec = dtMs / 1000.0;
        double dtTicks = dtMs / 50.0;

        double dx = x - prevX;
        double dy = y - prevY;
        double dz = z - prevZ;
        double dist = Math.sqrt(dx * dx + dy * dy + dz * dz);
        double rawSpeed = dist / Math.max(0.01, dtTicks); // blocks/tick

        // Angular change
        float dyaw = Math.abs(wrapDegrees(yaw - prevYaw));
        float dpitch = Math.abs(pitch - prevPitch);
        double angleChange = Math.sqrt(dyaw * dyaw + dpitch * dpitch);
        double rawTurnRate = angleChange / Math.max(0.01, dtSec); // deg/sec

        // Exponential smoothing (alpha = 0.45)
        double alpha = 0.45;
        smoothedSpeed = smoothedSpeed * (1.0 - alpha) + rawSpeed * alpha;
        smoothedTurnRate = smoothedTurnRate * (1.0 - alpha) + rawTurnRate * alpha;

        this.isSprinting = sprinting;
        this.isJumping = !onGround && dy > 0.05;
        this.isFlying = flying;
        this.isFalling = !onGround && vy < -0.3;
        this.isInWaterOrLava = inWater || inLava;
        this.isGuiOpen = guiOpen;
        this.health = hp;

        prevX = x; prevY = y; prevZ = z;
        prevYaw = yaw; prevPitch = pitch;
        lastEvalTimeMs = nowMs;

        evaluateState(nowMs);
    }

    private void evaluateState(long nowMs) {
        // Priority 1: Blocking conditions (Combat, GUI, Water/Lava, Low HP)
        if (nowMs - lastCombatTimeMs < 3000L) {
            currentResult = new EvaluationResult(ActivityState.BLOCKED_COMBAT, false, 0, 0, 1.0f);
            return;
        }
        if (isGuiOpen) {
            currentResult = new EvaluationResult(ActivityState.BLOCKED_GUI, false, 0, 0, 1.0f);
            return;
        }
        if (isInWaterOrLava) {
            currentResult = new EvaluationResult(ActivityState.BLOCKED_HAZARD, false, 0, 0, 1.0f);
            return;
        }
        if (health <= 6.0f) { // 3 hearts
            currentResult = new EvaluationResult(ActivityState.BLOCKED_LOW_HP, false, 0, 0, 1.0f);
            return;
        }

        // Priority 2: Extreme turning (> 200°/s -> pause new words)
        if (smoothedTurnRate > 200.0) {
            currentResult = new EvaluationResult(ActivityState.PAUSED_EXTREME_TURN, false, 0, 0, 1.0f);
            return;
        }

        // Priority 3: Flying or falling (1 word)
        if (isFlying || isFalling) {
            currentResult = new EvaluationResult(ActivityState.FLYING_FALLING, true, 1, 1, 1.0f);
            return;
        }

        // Priority 4: Fast turning (> 90°/s -> 1–2 words, closer to gaze center)
        if (smoothedTurnRate > 90.0) {
            currentResult = new EvaluationResult(ActivityState.FAST_TURN, true, 1, 2, 0.60f);
            return;
        }

        // Priority 5: Running / sprinting / jumping (1–2 words)
        if (isSprinting || isJumping || smoothedSpeed >= 0.22) {
            currentResult = new EvaluationResult(ActivityState.RUNNING_JUMPING, true, 1, 2, 1.0f);
            return;
        }

        // Priority 6: Walking (speed >= 0.05 blocks/tick -> 2–3 words)
        if (smoothedSpeed >= 0.05) {
            currentResult = new EvaluationResult(ActivityState.WALKING, true, 2, 3, 1.0f);
            return;
        }

        // Priority 7: Standing (speed < 0.05 bl/tick, turn < 30°/s -> 3–6 words)
        currentResult = new EvaluationResult(ActivityState.STANDING, true, 3, 6, 1.0f);
    }

    public EvaluationResult getCurrentResult() {
        return currentResult;
    }

    public int clampGroupSize(int desiredWords) {
        if (!currentResult.canSpawn) return 0;
        int min = currentResult.minGroup;
        int max = currentResult.maxGroup;
        // Density modifier
        float mult = KaraokeConfig.get().getDensity().wordGroupMultiplier;
        max = Math.max(min, Math.round(max * mult));
        // Comfort limit: maximum 4 words per group so text stays comfortably centered and legible
        int limit = Math.min(max, 4);
        return Math.max(min, Math.min(limit, desiredWords));
    }

    private static float wrapDegrees(float deg) {
        float wrapped = deg % 360.0f;
        if (wrapped >= 180.0f) wrapped -= 360.0f;
        if (wrapped < -180.0f) wrapped += 360.0f;
        return wrapped;
    }
}
