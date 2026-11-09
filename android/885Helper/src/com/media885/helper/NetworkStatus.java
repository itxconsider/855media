package com.media885.helper;

import android.os.SystemClock;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.URL;
import java.util.concurrent.*;
import javax.net.ssl.HttpsURLConnection;
import org.json.JSONObject;

final class NetworkStatus {
    private static final ThreadPoolExecutor WORK = new ThreadPoolExecutor(2, 2, 30,
        TimeUnit.SECONDS, new ArrayBlockingQueue<Runnable>(2));

    static JSONObject read() {
        long started = SystemClock.elapsedRealtime();
        Future<JSONObject> work = null;
        JSONObject result;
        try {
            work = WORK.submit(() -> fetch());
            result = work.get(6500, TimeUnit.MILLISECONDS);
        } catch (Exception e) {
            if (work != null) work.cancel(true);
            result = new JSONObject();
            try { result.put("success", false); result.put("error", "Network lookup unavailable"); }
            catch (Exception ignored) { }
        }
        try {
            result.put("networkProtocolVersion", 1);
            result.put("sampleUtcMillis", System.currentTimeMillis());
            result.put("durationMillis", SystemClock.elapsedRealtime() - started);
        } catch (Exception ignored) { }
        return result;
    }

    private static JSONObject fetch() throws Exception {
        // Default Android routing honors this app's VPN selection. No host lookup,
        // network binding, GPS, persistent cache, redirects or arbitrary URLs.
        HttpsURLConnection connection = (HttpsURLConnection)new URL(
            "https://ipwho.is/?fields=success,ip,city,region,country").openConnection();
        connection.setConnectTimeout(2500);
        connection.setReadTimeout(2500);
        connection.setInstanceFollowRedirects(false);
        connection.setUseCaches(false);
        try {
            if (connection.getResponseCode() != 200) throw new Exception("HTTP lookup failed");
            long deadline = SystemClock.elapsedRealtime() + 5000;
            ByteArrayOutputStream bytes = new ByteArrayOutputStream();
            try (InputStream input = connection.getInputStream()) {
                byte[] buffer = new byte[1024];
                int count;
                while ((count = input.read(buffer)) != -1) {
                    if (Thread.currentThread().isInterrupted()
                        || SystemClock.elapsedRealtime() > deadline
                        || bytes.size() + count > 16384) throw new Exception("Response limit");
                    bytes.write(buffer, 0, count);
                }
            }
            JSONObject remote = new JSONObject(bytes.toString("UTF-8"));
            JSONObject result = new JSONObject();
            result.put("success", remote.optBoolean("success", false));
            // Return only the explicitly requested public network fields.
            for (String key : new String[] { "ip", "city", "region", "country" })
                if (remote.opt(key) instanceof String) result.put(key, remote.getString(key));
            return result;
        } finally { connection.disconnect(); }
    }
}
