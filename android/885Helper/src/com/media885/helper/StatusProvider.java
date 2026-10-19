package com.media885.helper;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.Binder;

public final class StatusProvider extends ContentProvider {
    private void requireHost() {
        int uid = Binder.getCallingUid();
        if (uid != 0 && uid != 2000 && uid != android.os.Process.myUid())
            throw new SecurityException("885Helper only accepts local shell/root host commands.");
    }
    private void requireStatus(Uri uri) {
        if (!"com.media885.helper.status".equals(uri.getAuthority()) || !"/status".equals(uri.getPath()))
            throw new IllegalArgumentException("Unknown helper endpoint.");
    }
    @Override public boolean onCreate() { return true; }
    @Override public String getType(Uri uri) { requireHost(); requireStatus(uri); return "vnd.android.cursor.item/vnd.media885.status"; }
    @Override public Cursor query(Uri uri, String[] projection, String selection, String[] args, String sort) {
        requireHost(); requireStatus(uri);
        MatrixCursor cursor = new MatrixCursor(new String[] { "json" });
        cursor.addRow(new Object[] { DeviceStatus.read(getContext()).toString() });
        return cursor;
    }
    @Override public int update(Uri uri, ContentValues values, String selection, String[] args) {
        requireHost(); requireStatus(uri);
        if (values == null || values.size() != 1 || !values.containsKey("deviceLabel"))
            throw new IllegalArgumentException("Only deviceLabel may be written.");
        String label = values.getAsString("deviceLabel");
        if (label == null || label.length() > 80 || label.matches("(?s).*[^\\p{L}\\p{N} _.-].*"))
            throw new IllegalArgumentException("Device label must be at most 80 letters, digits, spaces, dots, underscores or hyphens.");
        return getContext().getSharedPreferences("helper", 0).edit().putString("deviceLabel", label).commit() ? 1 : 0;
    }
    @Override public Uri insert(Uri uri, ContentValues values) { throw new UnsupportedOperationException(); }
    @Override public int delete(Uri uri, String selection, String[] args) { throw new UnsupportedOperationException(); }
}
