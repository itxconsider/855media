package com.media885.helper;

import android.app.Activity;
import android.os.Bundle;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.view.View;
import android.widget.*;
import org.json.JSONObject;
import java.text.DateFormat;
import java.util.Date;
import java.util.Locale;

public final class MainActivity extends Activity {
    private static final int BG=0xfff3f6fb, INK=0xff182437, MUTED=0xff64748b, BLUE=0xff2563eb, GREEN=0xff158057, AMBER=0xffa65e0a;
    private LinearLayout content;
    private TextView summary, detail, updated, internet, vpn, keyboard, keyboardDetail, facebook, device, memory, storage, ip, location, networkTime, diagnostics, phoneTime, phoneZone;
    private ProgressBar memoryBar, storageBar, busy;
    private Button check, details;
    private boolean expanded, checking;
    private Thread worker;
    private int generation;
    private int dp(float n){return Math.round(n*getResources().getDisplayMetrics().density);}
    private GradientDrawable shape(int color,int radius){GradientDrawable d=new GradientDrawable(); d.setColor(color); d.setCornerRadius(dp(radius)); return d;}
    private TextView text(String s,int size,int color,boolean bold){TextView v=new TextView(this); v.setText(s); v.setTextSize(size); v.setTextColor(color); v.setLineSpacing(dp(2),1); if(bold)v.setTypeface(Typeface.create("sans-serif-medium",0)); return v;}
    private void space(LinearLayout p,int n){p.addView(new View(this),new LinearLayout.LayoutParams(1,dp(n)));}
    private LinearLayout card(String title,String subtitle){
        LinearLayout c=new LinearLayout(this); c.setOrientation(1); c.setPadding(dp(18),dp(18),dp(18),dp(18)); c.setBackground(shape(Color.WHITE,18));
        LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2); p.bottomMargin=dp(14); content.addView(c,p);
        c.addView(text(title,18,INK,true)); if(!subtitle.isEmpty()){space(c,3); c.addView(text(subtitle,12,MUTED,false));} space(c,12); return c;
    }
    private TextView row(LinearLayout p,String label){LinearLayout r=new LinearLayout(this); r.setOrientation(1); r.setPadding(0,dp(8),0,dp(8)); r.addView(text(label,12,MUTED,false)); TextView v=text("Checking…",15,INK,true); r.addView(v); p.addView(r); return v;}
    private Button button(LinearLayout p,String title,boolean primary){Button b=new Button(this); b.setAllCaps(false); b.setText(title); b.setTextSize(14); b.setTextColor(primary?Color.WHITE:BLUE); b.setBackground(shape(primary?BLUE:0xffebf2ff,12)); b.setPadding(dp(12),dp(8),dp(12),dp(8)); b.setMinHeight(dp(48)); LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,-2); lp.topMargin=dp(10); p.addView(b,lp); return b;}
    private ProgressBar bar(LinearLayout p){ProgressBar b=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal); b.setMax(100); b.setProgressTintList(android.content.res.ColorStateList.valueOf(BLUE)); p.addView(b,new LinearLayout.LayoutParams(-1,dp(6))); return b;}
    private String version(){try{return getPackageManager().getPackageInfo(getPackageName(),0).versionName;}catch(Exception e){return "";}}
    @Override public void onCreate(Bundle saved){
        super.onCreate(saved); getWindow().setStatusBarColor(BG); getWindow().setNavigationBarColor(BG); getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR | (android.os.Build.VERSION.SDK_INT >= 26 ? View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR : 0));
        ScrollView scroll=new ScrollView(this); scroll.setBackgroundColor(BG); scroll.setFillViewport(true);
        content=new LinearLayout(this); content.setOrientation(1); content.setPadding(dp(18),dp(24),dp(18),dp(28)); scroll.addView(content);
        content.addView(text("885Helper",28,INK,true)); content.addView(text("Your device, at a glance",14,MUTED,false)); space(content,22);
        LinearLayout hero=card("DEVICE STATUS",""); hero.setBackground(shape(0xffe7f2ff,18));
        summary=text("Checking device…",24,INK,true); hero.addView(summary); detail=text("",13,MUTED,false); hero.addView(detail); space(hero,12); updated=text("",11,MUTED,false); hero.addView(updated);
        button(hero,"Refresh device status",true).setOnClickListener(v->refresh());
        LinearLayout net=card("Connection","The network available to this helper."); internet=row(net,"Internet"); vpn=row(net,"VPN connection");
        View line=new View(this); line.setBackgroundColor(0xffe0e7f0); net.addView(line,new LinearLayout.LayoutParams(-1,dp(1)));
        ip=row(net,"Public IP"); ip.setText("Not checked yet"); ip.setTextIsSelectable(true); location=text("Location appears after a connection check.",13,MUTED,false); net.addView(location); networkTime=text("",11,MUTED,false); net.addView(networkTime);
        busy=new ProgressBar(this); busy.setVisibility(View.GONE); net.addView(busy,new LinearLayout.LayoutParams(dp(28),dp(28)));
        check=button(net,"Check IP & location",false); check.setOnClickListener(v->checkNetwork()); space(net,12);
        net.addView(text("Location is approximate. If your VPN uses app selection, include 885Helper to check the same route as Facebook.",12,MUTED,false));
        LinearLayout clock=card("Date & time","Phone time used for Facebook scheduling."); phoneTime=row(clock,"Current date & time"); phoneZone=row(clock,"Time zone");
        LinearLayout apps=card("Apps & keyboard","Ready for the desktop app to connect."); facebook=row(apps,"Facebook"); keyboard=row(apps,"Text input helper"); keyboardDetail=text("",12,MUTED,false); apps.addView(keyboardDetail);
        LinearLayout info=card("This device",""); device=text("",14,INK,true); info.addView(device); memory=row(info,"Available memory"); memoryBar=bar(info); storage=row(info,"Available storage"); storageBar=bar(info);
        LinearLayout advanced=card("Diagnostics","Detailed status for troubleshooting."); details=button(advanced,"Show technical details",false);
        diagnostics=text("",12,MUTED,false); diagnostics.setTypeface(Typeface.MONOSPACE); diagnostics.setTextIsSelectable(true); diagnostics.setVisibility(View.GONE); advanced.addView(diagnostics);
        details.setOnClickListener(v->{expanded=!expanded; diagnostics.setVisibility(expanded?View.VISIBLE:View.GONE); details.setText(expanded?"Hide technical details":"Show technical details");});
        button(advanced,"Copy device report",false).setOnClickListener(v->{((ClipboardManager)getSystemService(CLIPBOARD_SERVICE)).setPrimaryClip(ClipData.newPlainText("885Helper device report",diagnostics.getText())); Toast.makeText(this,"Device report copied",Toast.LENGTH_SHORT).show();});
        space(content,8); content.addView(text("885Helper · "+version()+"\nDevice checks only. Your accounts and posts are unchanged.",12,MUTED,false)); setContentView(scroll); refresh();
    }
    @Override protected void onResume(){super.onResume(); if(summary!=null)refresh();}
    private void state(TextView v,String label,boolean ok){v.setText(label); v.setTextColor(ok?GREEN:AMBER);}
    private String size(long n){return String.format(Locale.US,"%.1f GB",n/1073741824.0);}
    private int percent(long n,long total){return total<=0?0:(int)Math.max(0,Math.min(100,100.0*n/total));}
    private String time(){return DateFormat.getTimeInstance(DateFormat.SHORT).format(new Date());}
    private void refresh(){
        JSONObject s=DeviceStatus.read(this); boolean online=s.optBoolean("internetValidated"), fb=s.optBoolean("facebookInstalled"), installed=s.optBoolean("adbKeyboardInstalled"), active=s.optBoolean("adbKeyboardActive"), complete=!s.has("statusError"), ready=online&&fb&&installed&&complete;
        summary.setText(ready?"Device checks passed":"Needs attention"); summary.setTextColor(ready?GREEN:AMBER);
        detail.setText(!complete?"Some details could not be read. Refresh to try again.":ready?"Internet and required apps are available.":"Review the items below before starting a task."); updated.setText("Updated "+time());
        state(internet,online?"Connected":"Not confirmed",online); vpn.setText(s.optBoolean("activeNetworkVpn")?"Detected on current network":"Not detected on current network"); state(facebook,fb?"Installed":"Not installed",fb);
        state(keyboard,installed?(active?"Automation keyboard active":"Installed · ready when needed"):"Not installed",installed); keyboardDetail.setText(installed?(active?"The desktop app restores your normal keyboard when its task finishes.":"Text input helper is installed and available when needed."):"Install the text input helper from the desktop app.");
        device.setText("Android "+s.optString("androidVersion","—")+" · "+s.optString("model","Unknown device"));
        JSONObject c=s.optJSONObject("clock");
        phoneTime.setText(c==null?"Unavailable":c.optString("localDateTime","Unavailable"));
        int offset=c==null?0:c.optInt("utcOffsetSeconds");
        phoneZone.setText(c==null?"Unavailable":c.optString("timeZoneId","Unavailable")+String.format(Locale.US," (UTC%s%02d:%02d)",offset<0?"-":"+",Math.abs(offset)/3600,Math.abs(offset)%3600/60));
        long ma=s.optLong("availableMemoryBytes"), mt=s.optLong("totalMemoryBytes"), sa=s.optLong("availableStorageBytes"), st=s.optLong("totalStorageBytes");
        memory.setText(mt>0?size(ma)+" of "+size(mt)+" free":"Unavailable"); memoryBar.setProgress(percent(ma,mt)); storage.setText(st>0?size(sa)+" of "+size(st)+" free":"Unavailable"); storageBar.setProgress(percent(sa,st));
        try{diagnostics.setText(s.toString(2));}catch(Exception e){diagnostics.setText("Status unavailable");}
    }
    private void checkNetwork(){
        if(checking)return; checking=true; final int id=++generation; check.setEnabled(false); check.setText("Checking connection…"); busy.setVisibility(View.VISIBLE); ip.setText("Checking…"); location.setText("Looking up approximate location"); networkTime.setText("");
        worker=new Thread(()->{JSONObject result=NetworkStatus.read(); runOnUiThread(()->{
            if(isDestroyed()||id!=generation)return; checking=false; check.setEnabled(true); check.setText("Check IP & location"); busy.setVisibility(View.GONE);
            boolean ok=result.optBoolean("success")&&!result.optString("ip").isEmpty(); ip.setText(ok?result.optString("ip"):"Could not check connection"); String city=result.optString("city"),country=result.optString("country");
            location.setText(ok?((city.isEmpty()?"":city+", ")+country):"Check your connection, then try again."); networkTime.setText("Checked "+time());
        });},"885Helper-connection"); worker.start();
    }
    @Override protected void onDestroy(){generation++; if(worker!=null)worker.interrupt(); super.onDestroy();}
}
