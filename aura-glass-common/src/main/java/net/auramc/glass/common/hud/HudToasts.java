package net.auramc.glass.common.hud;

import java.util.concurrent.ConcurrentLinkedQueue;
import java.util.concurrent.atomic.AtomicInteger;

public final class HudToasts {
    public enum Icon { INFO,SUCCESS,WARNING }
    public static final class Toast {
        public final String text;public final Icon icon;public final long duration;
        public long started;public float width,fontSize=13;public String display;
        private Toast(String text,Icon icon,long duration){this.text=text;this.icon=icon;this.duration=duration;}
        public float opacity(long now){return Math.max(0,Math.min(1,Math.min((now-started)/200_000_000f,(started+duration-now)/200_000_000f)));}
    }
    private static final ConcurrentLinkedQueue<Toast> QUEUE=new ConcurrentLinkedQueue<>();
    private static final AtomicInteger QUEUED=new AtomicInteger();
    private HudToasts(){}
    public static boolean show(String text,Icon icon,long durationMillis){
        if(text==null||text.isBlank())return false;
        if(QUEUED.incrementAndGet()>16){QUEUED.decrementAndGet();return false;}
        String clean=text.replace('\n',' ').replace('\r',' ');
        String label=clean.length()>160?clean.substring(0,157)+"...":clean;
        QUEUE.offer(new Toast(label,icon==null?Icon.INFO:icon,Math.max(500,Math.min(300000,durationMillis))*1_000_000L));return true;
    }
    static Toast poll(){Toast value=QUEUE.poll();if(value!=null)QUEUED.decrementAndGet();return value;}
}
