package com.media885.helper;

import android.app.ActivityManager;
import android.content.Context;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.Build;
import android.os.StatFs;
import android.os.SystemClock;
import android.provider.Settings;
import org.json.JSONObject;

final class DeviceStatus {
    static boolean installed(Context context, String pkg) {
        try { context.getPackageManager().getPackageInfo(pkg, 0); return true; }
        catch (android.content.pm.PackageManager.NameNotFoundException e) { return false; }
    }
    static JSONObject read(Context context) {
        JSONObject data = new JSONObject();
        try {
            data.put("protocolVersion", 1);
            data.put("helperVersion", "1.0.0");
            data.put("heartbeat", true);
            data.put("sampleUtcMillis", System.currentTimeMillis());
            data.put("elapsedRealtimeMillis", SystemClock.elapsedRealtime());
            data.put("deviceLabel", context.getSharedPreferences("helper", 0).getString("deviceLabel", ""));
            data.put("lastBootBroadcastUtcMillis", context.getSharedPreferences("helper", 0).getLong("lastBoot", 0));
            data.put("manufacturer", Build.MANUFACTURER);
            data.put("model", Build.MODEL);
            data.put("androidVersion", Build.VERSION.RELEASE);
            data.put("apiLevel", Build.VERSION.SDK_INT);
            ActivityManager.MemoryInfo memory = new ActivityManager.MemoryInfo();
            ((ActivityManager) context.getSystemService(Context.ACTIVITY_SERVICE)).getMemoryInfo(memory);
            data.put("totalMemoryBytes", memory.totalMem);
            data.put("availableMemoryBytes", memory.availMem);
            StatFs disk = new StatFs(context.getFilesDir().getAbsolutePath());
            data.put("availableStorageBytes", disk.getAvailableBytes());
            data.put("totalStorageBytes", disk.getTotalBytes());
            ConnectivityManager manager = (ConnectivityManager) context.getSystemService(Context.CONNECTIVITY_SERVICE);
            Network active = manager.getActiveNetwork();
            NetworkCapabilities caps = active == null ? null : manager.getNetworkCapabilities(active);
            data.put("internetValidated", caps != null && caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) && caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED));
            data.put("activeNetworkVpn", caps != null && caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN));
            data.put("surfsharkInstalled", installed(context, "com.surfshark.vpnclient.android"));
            data.put("facebookInstalled", installed(context, "com.facebook.katana"));
            data.put("adbKeyboardInstalled", installed(context, "com.android.adbkeyboard"));
            String ime = Settings.Secure.getString(context.getContentResolver(), Settings.Secure.DEFAULT_INPUT_METHOD);
            data.put("adbKeyboardActive", ime != null && ime.startsWith("com.android.adbkeyboard/"));
        } catch (Exception e) {
            try { data.put("statusError", e.getClass().getSimpleName()); } catch (Exception ignored) { }
        }
        return data;
    }
}
