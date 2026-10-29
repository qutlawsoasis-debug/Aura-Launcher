#version 150
uniform sampler2D Font;
uniform vec2 HaloConfig; // x = width (0.10 em dark, 0.08 em light), y = blur (0.03 em)

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

    // Crisp sharp text coverage via MSDF (without extra glyph blur)
    bool insideQuad = (uv.x >= uvBounds.x && uv.x <= uvBounds.z && uv.y >= uvBounds.y && uv.y <= uvBounds.w);
    float textCoverage = insideQuad ? clamp(range * sd + 0.5, 0.0, 1.0) : 0.0;

    // Euclidean distance outside glyph bounds in em:
    // Atlas texture size is 2048x2048, font size is 40 pixels/em
    // So 1 unit in UV = 2048 atlas pixels = 2048 / 40 = 51.2 em
    vec2 outsideUV = max(uvBounds.xy - uv, max(vec2(0.0), uv - uvBounds.zw));
    float outsideEm = length(outsideUV * vec2(textureSize(Font, 0))) / 40.0;

    // Signed distance in em from glyph contour:
    float distEm = sd * (distanceRange / 40.0) - outsideEm;

    // Halo (MSDF outline / crisp shadow):
    // Width: 0.10 em (dark) / 0.08 em (light)
    // Blur: 0.03 em (sharp edge, not blurry/cloudy)
    float haloWidth = HaloConfig.x > 0.001 ? HaloConfig.x : 0.10;
    float haloBlur = HaloConfig.y > 0.001 ? HaloConfig.y : 0.03;

    float haloDist = (distEm + haloWidth) / haloBlur;
    float haloStep = clamp(haloDist, 0.0, 1.0);
    float haloCoverage = haloStep * haloStep * (3.0 - 2.0 * haloStep);

    // Halo color comes from vertex outlineColor (white on light bg, black on dark bg)
    vec4 outCol = vec4(outlineColor.rgb, outlineColor.a * haloCoverage);

    vec4 fillCol = fillColor;
    fillCol.a *= textCoverage;

    // True Porter-Duff Over composite: crisp glyph fill over soft/crisp halo
    float outAlpha = outCol.a * (1.0 - fillCol.a);
    float finalAlpha = fillCol.a + outAlpha;
    vec3 finalRgb = finalAlpha > 0.0001 ? (fillCol.rgb * fillCol.a + outCol.rgb * outAlpha) / finalAlpha : vec3(0.0);
    vec4 col = vec4(finalRgb, finalAlpha);

    if (col.a < 0.01) {
        discard;
    }

    fragColor = col;
}
