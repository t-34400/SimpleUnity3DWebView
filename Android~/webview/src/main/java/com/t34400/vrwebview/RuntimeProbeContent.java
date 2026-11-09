package com.t34400.vrwebview;
import android.app.Activity;
import android.util.Base64;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
/** Diagnostic fixture only. Production BrowserHost does not know this page or its assets. */
public final class RuntimeProbeContent {
    private RuntimeProbeContent() {}
    public static String page(Activity activity) throws Exception {
        byte[] video;
        try (InputStream stream = activity.getAssets().open("vrwebview-probe-video.mp4")) {
            ByteArrayOutputStream bytes = new ByteArrayOutputStream(); byte[] buffer = new byte[8192]; int count;
            while ((count = stream.read(buffer)) != -1) bytes.write(buffer, 0, count);
            video = bytes.toByteArray();
        }
        return "<!doctype html><meta name='viewport' content='width=device-width,initial-scale=1'>"
            + "<style>body{background:#fff;color:#111;font:24px sans-serif;margin:20px}input,textarea{font:24px sans-serif;width:90%;margin:8px}video{width:100%}</style>"
            + "<style>#hover{padding:12px;background:#ddd}#hover:hover{background:#4db6ac}#hover-state{font-size:18px}</style>"
            + "<h2>RT1 production backend</h2><div id='hover'>Hover here without pinching</div><p id='hover-state'>Waiting for pointerover</p><label>Text<input id='text'></label><label>Second<input id='second'></label>"
            + "<textarea id='multi'>Japanese IME</textarea><p>Finish editing, then operate native video controls.</p>"
            + "<video id='video' playsinline controls loop preload='metadata' src='data:video/mp4;base64,"
            + Base64.encodeToString(video, Base64.NO_WRAP) + "'></video><p id='state'>Waiting for playback</p>"
            + "<div style='height:800px'>Scroll / スクロール</div><input placeholder='Bottom editor'>"
            + "<script>const v=document.getElementById('video'),s=document.getElementById('state');"
            + "const h=document.getElementById('hover'),hs=document.getElementById('hover-state'),hc={over:0,move:0,out:0};"
            + "for(const [type,key] of [['pointerover','over'],['pointermove','move'],['pointerout','out']])h.addEventListener(type,e=>{"
            + "if(e.pointerType!=='mouse')return;hc[key]++;hs.textContent='Mouse hover: over='+hc.over+' move='+hc.move+' out='+hc.out;});"
            + "setInterval(()=>s.textContent='time='+v.currentTime.toFixed(2)+' paused='+v.paused+' error='+(v.error?v.error.code:0),500);</script>";
    }
}
