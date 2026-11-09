package com.media885.helper;

import android.app.Activity;
import android.os.Bundle;
import android.widget.TextView;

// Export requires DUMP (shell/system), and the provider must arm the request first.
public final class ClipboardActivity extends Activity {
    private boolean done;
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        TextView status=new TextView(this); status.setText("885Helper\nChecking copied profile link…");
        status.setTextSize(20); status.setPadding(24,48,24,24); setContentView(status);
        status.postDelayed(()->{if(!isFinishing())finish();},2000);
    }
    @Override public void onWindowFocusChanged(boolean focused) {
        super.onWindowFocusChanged(focused);
        if(focused&&!done) {
            done=true; ClipboardBridge.execute(this,getIntent().getStringExtra("request")); finish();
        }
    }
}
