package net.auramc.glass.common.hud;

public final class HudElement {
    public enum Anchor {
        TOP_LEFT(0,0),TOP_CENTER(.5f,0),TOP_RIGHT(1,0),
        CENTER_LEFT(0,.5f),CENTER(.5f,.5f),CENTER_RIGHT(1,.5f),
        BOTTOM_LEFT(0,1),BOTTOM_CENTER(.5f,1),BOTTOM_RIGHT(1,1);
        public final float x,y;
        Anchor(float x,float y){this.x=x;this.y=y;}
    }
    public String id;
    public Anchor anchor=Anchor.TOP_LEFT;
    public float offsetX,offsetY,scale=1;
    public boolean visible=false;
    private HudElement(){}
    public HudElement(String id,Anchor anchor,float x,float y,boolean visible){this.id=id;this.anchor=anchor;offsetX=x;offsetY=y;this.visible=visible;}
    public void validate(){
        if(id==null||id.isBlank()||anchor==null||!Float.isFinite(offsetX)||!Float.isFinite(offsetY)||!Float.isFinite(scale))throw new IllegalArgumentException("invalid HUD layout");
        scale=Math.max(.5f,Math.min(2,scale));
    }
    public float x(float width,float elementWidth){return clamp((anchor.x+offsetX)*width-anchor.x*elementWidth*scale,width-elementWidth*scale);}
    public float y(float height,float elementHeight){return clamp((anchor.y+offsetY)*height-anchor.y*elementHeight*scale,height-elementHeight*scale);}
    private static float clamp(float value,float max){return Math.max(0,Math.min(Math.max(0,max),value));}
}
