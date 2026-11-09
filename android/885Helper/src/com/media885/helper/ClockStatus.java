package com.media885.helper;

import android.os.SystemClock;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.TimeZone;
import org.json.JSONObject;

final class ClockStatus {
    static JSONObject read() {
        JSONObject data = new JSONObject();
        try {
            long sample = System.currentTimeMillis();
            TimeZone zone = TimeZone.getDefault();
            SimpleDateFormat local = new SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US);
            local.setTimeZone(zone);
            data.put("clockProtocolVersion", 1);
            data.put("sampleUtcMillis", sample);
            data.put("elapsedRealtimeMillis", SystemClock.elapsedRealtime());
            data.put("timeZoneId", zone.getID());
            data.put("utcOffsetSeconds", zone.getOffset(sample) / 1000);
            data.put("localDateTime", local.format(new Date(sample)));
        } catch (Exception e) {
            try { data.put("clockError", e.getClass().getSimpleName()); } catch (Exception ignored) { }
        }
        return data;
    }
}
