#version 150
uniform sampler2D Backdrop0;
uniform sampler2D Backdrop1;
uniform sampler2D Backdrop2;
uniform sampler2D Backdrop3;
uniform sampler2D Noise;
uniform vec2 FramebufferSize;
uniform int Quality;
uniform float Modal;
in vec2 point;
flat in vec2 extent;
flat in vec4 lens;
flat in vec4 optical;
flat in vec4 lighting;
flat in vec4 tint;
flat in vec4 finish;
flat in vec4 extra;
flat in float opacity;
flat in vec3 profileShape;
out vec4 fragColor;

float sdf(vec2 p) {
    float radius = clamp(finish.z, 0.0, min(extent.x, extent.y) * .5);
    vec2 q = abs(p - extent * .5) - extent * .5 + radius;
    vec2 v = max(q, 0.0);
    float k = finish.w;
    float norm = pow(pow(v.x, k) + pow(v.y, k), 1.0 / k);
    return norm + min(max(q.x, q.y), 0.0) - radius;
}

float cornerFactor(vec2 p) {
    float radius = clamp(finish.z, 0.0, min(extent.x, extent.y) * .5);
    vec2 q = abs(p - extent * .5) - extent * .5 + radius;
    vec2 arc = max(q, 0.0);
    float entrance = smoothstep(0.0, 1.5, min(arc.x, arc.y));
    float angle = 2.0 * min(arc.x, arc.y) / max(arc.x + arc.y, .0001);
    return entrance * smoothstep(0.0, 1.0, angle);
}

float cornerSdf(vec2 p) {
    const float radius = 1.5;
    return .25 * (sdf(p + vec2(radius,0)) + sdf(p - vec2(radius,0))
                + sdf(p + vec2(0,radius)) + sdf(p - vec2(0,radius)));
}

vec3 backdrop(vec2 uv, float frost) {
    vec2 pixel = .5 / FramebufferSize;
    uv = clamp(uv, pixel, 1.0 - pixel);
    float level = clamp(max(frost,Modal), 0.0, 3.0);
    float dim = 1.0 - .25 * Modal;
    vec3 c0=textureLod(Backdrop0,uv,0.0).rgb;
    vec3 c1=textureLod(Backdrop1,uv,0.0).rgb;
    vec3 c2=textureLod(Backdrop2,uv,0.0).rgb;
    vec3 c3=textureLod(Backdrop3,uv,0.0).rgb;
    vec4 weights=max(vec4(1.0)-abs(vec4(level)-vec4(0,1,2,3)),vec4(0));
    return dim*(c0*weights.x+c1*weights.y+c2*weights.z+c3*weights.w);
}

void main() {
    float d = sdf(point);
    float aa = max(fwidth(d), .0001);
    float coverage = 1.0 - smoothstep(-aa * .5, aa * .5, d);
    if (coverage <= 0.0) discard;
    if (Quality == 0) {
        float rim = (1.0 - smoothstep(.5, 1.5 + aa, max(-d, 0.0))) * lighting.z;
        float alpha = coverage * opacity * max(tint.a, clamp(rim, 0.0, 1.0));
        fragColor = vec4(mix(tint.rgb, vec3(1), clamp(rim, 0.0, 1.0)) * alpha, alpha);
        return;
    }
    float bevel = max(.01, min(lens.y, min(extent.x * .5, extent.y * .35)));
    // A bevel-wide central stencil smooths the rounded-box medial-axis junction.
    float epsilon = max(.25, bevel * .5);
    vec2 gradient = vec2(sdf(point + vec2(epsilon, 0)) - sdf(point - vec2(epsilon, 0)),
                         sdf(point + vec2(0, epsilon)) - sdf(point - vec2(0, epsilon))) / (2.0 * epsilon);
    float corner = cornerFactor(point);
    if (corner > 0.0) {
        vec2 filtered = vec2(cornerSdf(point + vec2(epsilon,0)) - cornerSdf(point - vec2(epsilon,0)),
                             cornerSdf(point + vec2(0,epsilon)) - cornerSdf(point - vec2(0,epsilon))) / (2.0 * epsilon);
        gradient = mix(gradient, filtered, corner);
    }
    float gradientLength = max(length(gradient), .0001);
    vec2 normal = gradient / gradientLength;
    float inside = max(-d, 0.0) / gradientLength;
    float t = clamp(inside / bevel, 0.0, 1.0);
    float profile = clamp(profileShape.x, 1.5, 4.0);
    // h=1-(1-t)^p; (dh/dt)/p is finite and reaches one at the edge.
    float slope = pow(1.0 - t, profile - 1.0);
    float edgeWeight = pow(1.0 - t, max(lens.z, .01));
    float cornerWeight = mix(1.0, 1.0 - clamp(profileShape.y,0.0,1.0), corner);
    vec2 noise = texture(Noise, gl_FragCoord.xy / 64.0).rg * 2.0 - 1.0;
    float channelSpan = Quality == 2 ? min(optical.x * min(bevel, 16.0), 2.0) : 0.0;
    vec2 shift = vec2(0);
    if (Quality == 2) {
        // Reserve the fixed RGB half-span before limiting refraction and wobble.
        float travel = 2.0 * bevel - channelSpan * .5;
        float wobble = min(extra.x, travel * .25 / sqrt(2.0));
        float strength = min(lens.x, max(0.0,travel-sqrt(2.0)*wobble) / bevel);
        shift = (normal * strength * bevel + noise * wobble) * slope * cornerWeight * extra.w;
    }
    vec2 uv = gl_FragCoord.xy / FramebufferSize;
    vec2 offset = vec2(shift.x, -shift.y) / FramebufferSize;
    vec3 color = tint.rgb;
    if (Quality > 0) {
        vec2 split = vec2(normal.x, -normal.y) * (channelSpan * .5 * slope * extra.w) / FramebufferSize;
        if (Quality == 2) color = vec3(backdrop(uv - offset - split, optical.y).r,
                     backdrop(uv - offset, optical.y).g,
                     backdrop(uv - offset + split, optical.y).b);
        else color = backdrop(uv, optical.y);
        // Compact controls share a broad backdrop tone; edge refraction contributes detail.
        if (extent.y <= 26.0 && extent.x <= 400.0) {
            vec3 group = (textureLod(Backdrop3,vec2(.35,.4),0.0).rgb + textureLod(Backdrop3,vec2(.65,.4),0.0).rgb
                        + textureLod(Backdrop3,vec2(.35,.6),0.0).rgb + textureLod(Backdrop3,vec2(.65,.6),0.0).rgb) * (.25*(1.0-.25*Modal));
            color = mix(group,color,.22);
        }
        float luma = dot(color, vec3(.2126, .7152, .0722));
        color = mix(vec3(luma), color, finish.x);
    }
    float vertical = point.y / max(extent.y, 1.0);
    float tintAlpha = clamp(tint.a + extra.y * (vertical - .5), 0.0, 1.0);
    float outerRim = 1.0 - smoothstep(.5, 1.5 + aa, inside);
    float innerRim = 1.0 - smoothstep(.25, .8 + aa, abs(inside - lighting.w));
    vec2 lightDir = lighting.xy / max(length(lighting.xy), .001);
    float bottomFacing = max(normal.y, 0.0);
    float directed = max(dot(normal, lightDir), 0.0);
    float specular = pow(directed, 3.0);
    float specWeight = pow(1.0 - clamp(max(-d, 0.0) / bevel, 0.0, 1.0), clamp(profileShape.z, 1.0, 6.0));
    float light = lighting.z * (outerRim + innerRim * .5) * (.25 + .75 * specular);
    light += optical.z * specular * specWeight;
    light += optical.w * directed * specWeight;
    float shadow = lens.w * bottomFacing * edgeWeight;
    if (Quality > 0) color = mix(color, tint.rgb, tintAlpha);
    color *= 1.0 - clamp(shadow, 0.0, .95);
    float baseLuma = clamp(dot(color, vec3(.2126, .7152, .0722)), 0.0, 1.0);
    light *= mix(1.0, .75, 1.0 - baseLuma);
    // A bounded response avoids hard white plateaus when rim and specular overlap.
    color += (vec3(1) - color) * (1.0 - exp(-1.5 * max(light + extra.z * edgeWeight, 0.0)));
    float darkMaterial = 1.0 - smoothstep(.15, .60, baseLuma);
    float darkRim = .10 * darkMaterial * (1.0 - smoothstep(0.0, 1.25, max(-d, 0.0)));
    color += (vec3(1) - color) * darkRim;
    color += noise.r * .015 * finish.y;
    float alpha = coverage * opacity * (Quality == 0 ? max(tintAlpha, clamp(light, 0.0, 1.0)) : 1.0);
    fragColor = vec4(clamp(color, 0.0, 1.0) * alpha, alpha);
}
