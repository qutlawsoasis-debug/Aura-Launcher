package net.auramc.glass.common.render;

public final class GlassSpring {
    private float value, velocity;
    private long previous;

    public float approach(float target, long now) {
        if (previous == 0) { previous = now; return value; }
        float dt = Math.min(.05f, Math.max(0f, (now - previous) / 1_000_000_000f));
        previous = now;
        float omega = 18f;
        float zeta = .72f;
        float damped = omega * (float) Math.sqrt(1f - zeta * zeta);
        float delta = value - target;
        float k = (velocity + zeta * omega * delta) / damped;
        float phase = damped * dt;
        float c = (float) Math.cos(phase), s = (float) Math.sin(phase);
        float decay = (float) Math.exp(-zeta * omega * dt);
        float position = delta * c + k * s;
        value = target + decay * position;
        velocity = decay * ((-delta * damped * s + k * damped * c) - zeta * omega * position);
        return Math.max(-.05f, Math.min(1.12f, value));
    }
}
