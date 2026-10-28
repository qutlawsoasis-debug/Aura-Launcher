package net.auramc.glass.common.style;

public final class GlassStyle {
    private transient boolean selectedText,themeTint;
    public boolean isSelectedText(){return selectedText;}
    public boolean isThemeTint(){return themeTint;}
    public GlassStyle selectedText(boolean tint){selectedText=true;themeTint=tint;return this;}

    public float refraction = 12, bevel = 10, splay = 2, depth = .16f;
    public float dispersion = .04f, frost = 2, light = .35f, lightDirX = -.55f, lightDirY = -.83f;
    public float rimOuter = .35f, rimInner = 2, gloss = .2f;
    public float tintR = 1, tintG = 1, tintB = 1, tintA = .12f;
    public float saturation = 1.08f, noise = .5f, radius = 10, squircleK = 4, wobble = .3f;
    public float gradient = 0, innerGlow = 0, scale = 1;
    public float profile = 2.5f, cornerSoften = .6f, specFalloff = 3;

    public GlassStyle copy() {
        GlassStyle copy = new GlassStyle();
        for (var field : GlassStyle.class.getFields()) {
            try { field.setFloat(copy, field.getFloat(this)); }
            catch (IllegalAccessException impossible) { throw new IllegalStateException(impossible); }
        }
        copy.selectedText=selectedText;copy.themeTint=themeTint;
        return copy;
    }

    public void validate() {
        for (var field : GlassStyle.class.getFields()) {
            try {
                float value = field.getFloat(this);
                if (!Float.isFinite(value)) throw new IllegalArgumentException(field.getName() + " must be finite");
                String name = field.getName();
                float min = name.equals("bevel") || name.equals("radius") ? -1 : name.startsWith("lightDir") ? -1 : 0;
                float max = switch (name) {
                    case "frost" -> 3;
                    case "profile" -> 4;
                    case "specFalloff" -> 6;
                    case "squircleK" -> 8;
                    case "scale" -> 2;
                    case "tintR", "tintG", "tintB", "tintA", "dispersion", "noise", "lightDirX", "lightDirY", "cornerSoften" -> 1;
                    case "saturation", "splay", "depth", "light", "rimOuter", "gloss", "innerGlow", "gradient", "wobble" -> 8;
                    default -> 100;
                };
                if (name.equals("squircleK")) min = 2;
                if (name.equals("scale")) min = .5f;
                if (name.equals("profile")) min = 1.5f;
                if (name.equals("specFalloff")) min = 1;
                field.setFloat(this, Math.max(min, Math.min(max, value)));
            } catch (IllegalAccessException impossible) { throw new IllegalStateException(impossible); }
        }
    }
}
