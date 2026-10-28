package net.auramc.glass.common.style;

import com.google.gson.annotations.SerializedName;

public enum GlassQuality {
    @SerializedName(value="LOW", alternate={"low"}) LOW,
    @SerializedName(value="MEDIUM", alternate={"medium"}) MEDIUM,
    @SerializedName(value="HIGH", alternate={"high","FULL","full"}) HIGH,
    @SerializedName(value="OFF", alternate={"off"}) OFF;

    public boolean needsBackdrop() { return this==MEDIUM || this==HIGH; }
    public int shaderLevel() { return this==HIGH?2:this==MEDIUM?1:0; }
}
