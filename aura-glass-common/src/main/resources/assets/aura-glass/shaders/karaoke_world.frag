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

    // Clamp UV to glyph bounds to strictly avoid sampling neighboring glyphs in atlas
    vec2 clampedUV = clamp(uv, uvBounds.xy, uvBounds.zw);
    vec3 msdf = texture(Font, clampedUV).rgb;
    float sd = median3(msdf) - 0.5;

    // Crisp text coverage via MSDF
    bool insideQuad = (uv.x >= uvBounds.x && uv.x <= uvBounds.z && uv.y >= uvBounds.y && uv.y <= uvBounds.w);
    float textCoverage = insideQuad ? clamp(range * sd + 0.5, 0.0, 1.0) : 0.0;

    // Euclidean distance outside glyph bounds in em:
    // Atlas texture size is 2048x2048, font size is 40 pixels/em
    // So 1 unit in UV = 2048 atlas pixels = 2048 / 40 = 51.2 em
    vec2 outsideUV = max(uvBounds.xy - uv, max(vec2(0.0), uv - uvBounds.zw));
    float outsideEm = length(outsideUV * vec2(textureSize(Font, 0))) / 40.0;

    // Signed distance in em from glyph contour:
    // When inside quad: distEm = sd * (distanceRange / 40.0)
    // When outside quad: distEm continues outward into the expanded padding
    float distEm = sd * (distanceRange / 40.0) - outsideEm;

    // Halo (MSDF outline / soft shadow): width 0.10 em, blur 0.08 em, color rgb(0,0,0)
    // Outer edge at -0.10 em, solid core from 0.0 to -0.02 em, smooth blur over 0.08 em
    float haloDist = (distEm + 0.10) / 0.08;
    float haloStep = clamp(haloDist, 0.0, 1.0);
    float haloCoverage = haloStep * haloStep * (3.0 - 2.0 * haloStep);

    vec4 outCol = vec4(0.0, 0.0, 0.0, outlineColor.a * haloCoverage);

    vec4 fillCol = fillColor;
    fillCol.a *= textCoverage;

    // Over-composite: text fill on top of soft dark halo (Apple HIG typography)
    vec4 col = mix(outCol, fillCol, fillCol.a > 0.001 ? fillCol.a : 0.0);
    col.a = max(outCol.a, fillCol.a);

    if (col.a < 0.01) {
        discard;
    }

    fragColor = col;
}
