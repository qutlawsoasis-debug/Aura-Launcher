#version 150
in vec2 Position;
in vec2 Local;
in vec2 Size;
in vec4 Color;
in vec4 Edge;
in vec4 Shape;
in vec2 UV;
uniform mat4 Projection;
uniform mat4 Model;
out vec2 p;
out vec2 extent;
out vec4 fillColor;
out vec4 edgeColor;
out vec4 shape;
out vec2 uv;
void main() {
    gl_Position = Projection * Model * vec4(Position, 0.0, 1.0);
    p = Local;
    extent = Size;
    fillColor = Color;
    edgeColor = Edge;
    shape = Shape;
    uv = UV;
}
