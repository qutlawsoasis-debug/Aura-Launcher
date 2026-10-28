#version 150
uniform sampler2D Font;
uniform sampler2D Contrast;
uniform int ContrastSlot;
in vec2 p;
in vec2 extent;
in vec4 fillColor;
in vec4 edgeColor;
in vec4 shape;
in vec2 uv;
out vec4 fragColor;
float median3(vec3 c) { return max(min(c.r, c.g), min(max(c.r, c.g), c.b)); }
void main() {
    if (shape.z > .5) {
        vec2 unitRange = vec2(shape.w) / vec2(textureSize(Font, 0));
        vec2 screenSize = vec2(1.0) / max(fwidth(uv), vec2(.000001));
        float range = max(.5 * dot(unitRange, screenSize), 1.0);
        float coverage = clamp(range * (median3(texture(Font, uv).rgb) - .5) + .5, 0.0, 1.0);
        if (ContrastSlot >= 0) {
            float dark = texelFetch(Contrast, ivec2(ContrastSlot,0),0).r;
            if (shape.z > 1.1) {
                float soft = smoothstep(-.65,.65,range*(median3(texture(Font,uv).rgb)-.5));
                fragColor = vec4(0,0,0,fillColor.a*soft*.28*(1.0-dark));
            } else fragColor = vec4(mix(vec3(1),vec3(26.0/255.0),dark),fillColor.a*coverage*mix(1.0,.9,dark));
        } else fragColor = vec4(fillColor.rgb, fillColor.a * coverage);
    } else {
        vec2 q = abs(p - extent * .5) - extent * .5 + shape.x;
        float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - shape.x;
        if (shape.z < -.5) {
            float outside = max(d, 0.0) / 6.0;
            fragColor = vec4(fillColor.rgb, fillColor.a * exp(-.5 * outside * outside));
            return;
        }
        float aa = max(fwidth(d), .0001);
        float coverage = 1.0 - smoothstep(-aa * .5, aa * .5, d);
        float rim = smoothstep(-shape.y - aa * .5, -shape.y + aa * .5, d);
        vec4 c = mix(fillColor, edgeColor, shape.y > 0.0 ? rim : 0.0);
        fragColor = vec4(c.rgb, c.a * coverage);
    }
}
