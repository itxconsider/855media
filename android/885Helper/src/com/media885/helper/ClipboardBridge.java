package com.media885.helper;

import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.net.Uri;
import android.os.SystemClock;
import org.json.JSONObject;

// Shell-protected, bounded in-memory requests. No clipboard history or disk storage.
final class ClipboardBridge {
    private static String session="", request="", operation="", value="";
    private static long sessionAt, requestAt;
    private static boolean ready, success, prepared;
    private static boolean nonce(String s) { return s != null && s.matches("[a-f0-9]{32}"); }
    static synchronized boolean arm(String s, String r, String op) {
        long now=SystemClock.elapsedRealtime();
        if (!nonce(s)||!nonce(r)||!("prepare".equals(op)||"read".equals(op))) return false;
        if ("read".equals(op) && (!prepared||!s.equals(session)||now-sessionAt>180000)) return false;
        if ("prepare".equals(op)) {session=s; sessionAt=now; prepared=false;}
        request=r; operation=op; requestAt=now; value=""; ready=false; success=false; return true;
    }
    static synchronized void execute(Context context, String r) {
        if (!nonce(r)||!r.equals(request)||ready||SystemClock.elapsedRealtime()-requestAt>10000) return;
        try {
            ClipboardManager manager=(ClipboardManager)context.getSystemService(Context.CLIPBOARD_SERVICE);
            String marker="pflow-profile-proof-"+session;
            if ("prepare".equals(operation)) manager.setPrimaryClip(ClipData.newPlainText("885 profile verification",marker));
            ClipData clip=manager.getPrimaryClip();
            CharSequence text=clip!=null&&clip.getItemCount()==1?clip.getItemAt(0).getText():null;
            String raw=text==null?"":text.toString().trim();
            success="prepare".equals(operation)?marker.equals(raw):facebookLink(raw);
            if("prepare".equals(operation)) prepared=success;
            value=success&&"read".equals(operation)?raw:"";
        } catch (Exception ignored) {success=false; value="";}
        ready=true;
    }
    private static boolean facebookLink(String text) {
        if (text.length()>2048||text.contains("\n")||text.contains("\r")) return false;
        Uri uri=Uri.parse(text); String host=uri.getHost();
        return "https".equalsIgnoreCase(uri.getScheme())&&host!=null
            && (host.equalsIgnoreCase("facebook.com")||host.toLowerCase(java.util.Locale.ROOT).endsWith(".facebook.com"))
            && uri.getUserInfo()==null;
    }
    static synchronized JSONObject result(String r) {
        JSONObject json=new JSONObject();
        try {
            boolean valid=nonce(r)&&r.equals(request)&&SystemClock.elapsedRealtime()-requestAt<=10000;
            json.put("clipboardProtocolVersion",1); json.put("request",valid?r:"");
            json.put("ready",valid&&ready); json.put("success",valid&&ready&&success);
            json.put("operation",valid?operation:""); json.put("value",valid&&ready&&success?value:"");
            if(valid&&ready) value=""; // Consume any copied URL after its one host read.
        } catch(Exception ignored) { }
        return json;
    }
}
