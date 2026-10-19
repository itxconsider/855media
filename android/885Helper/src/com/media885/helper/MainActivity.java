package com.media885.helper;

import android.app.Activity;
import android.os.Bundle;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

public final class MainActivity extends Activity {
    private TextView status;
    private void refresh() {
        try { status.setText(DeviceStatus.read(this).toString(2)); }
        catch (Exception e) { status.setText("Status unavailable"); }
    }
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        LinearLayout panel = new LinearLayout(this); panel.setOrientation(LinearLayout.VERTICAL);
        int padding = (int) (20 * getResources().getDisplayMetrics().density);
        panel.setPadding(padding, padding, padding, padding);
        TextView title = new TextView(this); title.setText("885Helper"); title.setTextSize(26); panel.addView(title);
        TextView description = new TextView(this);
        description.setText("Device diagnostics for 855Media. Refresh reports current state. VPN presence does not prove Surfshark ownership."); panel.addView(description);
        Button button = new Button(this); button.setText("Refresh status"); button.setOnClickListener(v -> refresh()); panel.addView(button);
        status = new TextView(this); status.setTextIsSelectable(true); status.setTextSize(13);
        ScrollView scroll = new ScrollView(this); scroll.addView(status); panel.addView(scroll);
        setContentView(panel); refresh();
    }
}
