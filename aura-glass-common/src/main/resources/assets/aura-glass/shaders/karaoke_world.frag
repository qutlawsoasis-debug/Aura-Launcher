#version 150
uniform sampler2D Font;

in vec2 uv;
in vec4 fillColor;
in vec4 outlineColor;
in vec2 params;

out vec4 fragColor;

float median3(vec3 c) {
    return max(min(c.r, c.g), min(max(c.r, c.g), c.b));
}

void main() {
    if (params.y < 0.5) {
        fragColor = fillColor;
        return;
    }

    float distanceRange = params.x;
    vec2 unitRange = vec2(distanceRange) / vec2(textureSize(Font, 0));
    vec2 screenSize = vec2(1.0) / max(fwidth(uv), vec2(0.000001));
    float range = max(0.5 * dot(unitRange, screenSize), 1.0);

    vec3 msdf = texture(Font, uv).rgb;
    float sd = median3(msdf) - 0.5;

    // Crisp text coverage via MSDF
    float textCoverage = clamp(range * sd + 0.5, 0.0, 1.0);

    // Soft dark outline / subtle halo for high legibility over arbitrary backgrounds (Apple HIG)
    float outlineDist = sd + 0.16;
    float outlineCoverage = clamp(range * outlineDist + 0.5, 0.0, 1.0);

    vec4 outCol = outlineColor;
    outCol.a *= outlineCoverage;

    vec4 fillCol = fillColor;
    fillCol.a *= textCoverage;

    // Over-composite: text fill on top of soft dark outline
    vec4 col = mix(outCol, fillCol, fillCol.a > 0.001 ? fillCol.a : 0.0);
    col.a = max(outCol.a, fillCol.a);

    if (col.a < 0.01) {
        discard;
    }

    fragColor = col;
}
