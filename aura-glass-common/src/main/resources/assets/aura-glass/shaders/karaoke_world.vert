#version 150
in vec3 Position;
in vec2 UV;
in vec4 Color;
in vec4 Outline;
in vec2 Params;
in vec4 UVBounds;

uniform mat4 Projection;
uniform mat4 ModelView;

out vec2 uv;
out vec4 fillColor;
out vec4 outlineColor;
out vec2 params;
out vec4 uvBounds;

void main() {
    gl_Position = Projection * ModelView * vec4(Position, 1.0);
    uv = UV;
    fillColor = Color;
    outlineColor = Outline;
    params = Params;
    uvBounds = UVBounds;
}
