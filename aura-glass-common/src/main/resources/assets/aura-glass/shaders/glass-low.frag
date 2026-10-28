#version 150
in vec2 point;
flat in vec2 extent;
flat in vec4 lighting;
flat in vec4 tint;
flat in vec4 finish;
flat in float opacity;
out vec4 fragColor;
void main() {
    float radius = clamp(finish.z, 0.0, min(extent.x, extent.y) * .5);
    vec2 q = abs(point - extent * .5) - extent * .5 + radius;
    vec2 v = max(q, 0.0);
    float d = pow(pow(v.x, finish.w) + pow(v.y, finish.w), 1.0 / finish.w) + min(max(q.x, q.y), 0.0) - radius;
    float aa = max(fwidth(d), .0001);
    float coverage = 1.0 - smoothstep(-aa * .5, aa * .5, d);
    float rim = (1.0 - smoothstep(.5, 1.5 + aa, max(-d, 0.0))) * lighting.z;
    float alpha = coverage * opacity * max(tint.a, clamp(rim, 0.0, 1.0));
    fragColor = vec4(mix(tint.rgb, vec3(1), clamp(rim, 0.0, 1.0)) * alpha, alpha);
}
