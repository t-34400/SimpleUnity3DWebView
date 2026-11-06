package com.t34400.vrwebview;

import android.app.Activity;
import android.os.Bundle;

/** Independent 2D baseline. This deliberately does not prove XR focus coexistence. */
public final class ImeProbeActivity extends Activity {
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        ImeProbe.open(this, getIntent().getBooleanExtra("virtualDisplay", false));
    }
    @Override public void onDestroy() {
        ImeProbe.close(this);
        super.onDestroy();
    }
    @Override public void onStop() {
        ImeProbe.close(this);
        super.onStop();
    }
}
