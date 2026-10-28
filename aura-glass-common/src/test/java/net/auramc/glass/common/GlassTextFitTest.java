package net.auramc.glass.common;
import net.auramc.glass.common.ui.GlassTextFit;
public final class GlassTextFitTest {
    public static void main(String[] args){
        java.util.function.ToDoubleFunction<String> widths=s->s.codePointCount(0,s.length());
        var normal=GlassTextFit.fit("Short",10,60,widths);
        check(normal.text().equals("Short") && normal.size()==10,"short text unchanged");
        var scaled=GlassTextFit.fit("1234567890",10,90,widths);
        check(scaled.size()==9 && scaled.text().equals("1234567890"),"modest shrink without ellipsis");
        var clipped=GlassTextFit.fit("1234567890",10,50,widths);
        check(clipped.size()==8.5f && clipped.width()<=50 && clipped.text().endsWith("…"),"floor and ellipsis");
        var unicode=GlassTextFit.fit("AB😀ΓΔЖ",10,34,widths);
        check(unicode.width()<=34 && !unicode.text().contains("\uFFFD"),"code-point boundaries preserved");
        check(GlassTextFit.fit("too small",10,1,widths).text().isEmpty(),"no overflowing ellipsis");
        System.out.println("Text fitting: bounded scale, truncation, Unicode boundaries PASS");
    }
    private static void check(boolean passed,String message){if(!passed)throw new AssertionError(message);}
}
