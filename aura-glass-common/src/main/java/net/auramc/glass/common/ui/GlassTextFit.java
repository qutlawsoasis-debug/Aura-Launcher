package net.auramc.glass.common.ui;
import java.util.function.ToDoubleFunction;
public final class GlassTextFit {
    public record Fitted(String text,float size,float width){}
    private GlassTextFit(){}
    public static Fitted fit(String text,float size,float available,ToDoubleFunction<String> measure){
        float units=(float)measure.applyAsDouble(text);
        float factor=units<=0?1:Math.max(.85f,Math.min(1,available/(units*size)));
        float fitted=size*factor;
        if(units*fitted<=available)return new Fitted(text,fitted,units*fitted);
        String ellipsis="…";
        if(measure.applyAsDouble(ellipsis)*fitted>available)return new Fitted("",fitted,0);
        int lo=0,hi=text.codePointCount(0,text.length());
        while(lo<hi){int mid=(lo+hi+1)/2;String prefix=text.substring(0,text.offsetByCodePoints(0,mid))+ellipsis;if(measure.applyAsDouble(prefix)*fitted<=available)lo=mid;else hi=mid-1;}
        String result=text.substring(0,text.offsetByCodePoints(0,lo))+ellipsis;
        return new Fitted(result,fitted,(float)measure.applyAsDouble(result)*fitted);
    }
}
