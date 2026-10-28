#version 150
in vec4 Rect;
in vec4 Lens;
in vec4 Optical;
in vec4 Lighting;
in vec4 Tint;
in vec4 Finish;
in vec4 Extra;
in vec4 Opacity;
uniform mat4 Projection;
uniform int Instanced;
out vec2 point;
flat out vec2 extent;
flat out vec4 lens;
flat out vec4 optical;
flat out vec4 lighting;
flat out vec4 tint;
flat out vec4 finish;
flat out vec4 extra;
flat out float opacity;
flat out vec3 profileShape;
void main() {
    int index = gl_VertexID;
    if (Instanced == 0) {
        index = index % 6;
        index = (index == 2 || index == 4) ? 3 : index == 3 ? 0 : index == 5 ? 2 : index;
    }
    vec2 corner = vec2(index & 1, index >> 1);
    point = corner * (Rect.zw + 2.0) - 1.0;
    extent = Rect.zw;
    lens = Lens; optical = Optical; lighting = Lighting;
    tint = Tint; finish = Finish; extra = Extra; opacity = Opacity.x; profileShape = Opacity.yzw;
    gl_Position = Projection * vec4(Rect.xy + point, 0.0, 1.0);
}
