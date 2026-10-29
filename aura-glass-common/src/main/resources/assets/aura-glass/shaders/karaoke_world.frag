#version 150
uniform sampler2D Font;

in vec2 uv;
in vec4 fillColor;
in vec4 outlineColor;
in vec2 params;
in vec4 uvBounds;

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

    vec2 clampedUV = clamp(uv, uvBounds.xy, uvBounds.zw);
    vec3 msdf = texture(Font, clampedUV).rgb;
    float sd = median3(msdf) - 0.5;

    // Pure, crisp MSDF glyph coverage with zero shadow, zero outline, zero halo
    bool insideQuad = (uv.x >= uvBounds.x && uv.x <= uvBounds.z && uv.y >= uvBounds.y && uv.y <= uvBounds.w);
    float textCoverage = insideQuad ? clamp(range * sd + 0.5, 0.0, 1.0) : 0.0;

    vec4 col = vec4(fillColor.rgb, fillColor.a * textCoverage);

    if (col.a < 0.01) {
        discard;
    }

    fragColor = col;
}
