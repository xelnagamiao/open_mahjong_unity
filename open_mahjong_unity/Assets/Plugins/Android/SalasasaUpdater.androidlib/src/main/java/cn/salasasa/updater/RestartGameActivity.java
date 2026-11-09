package cn.salasasa.updater;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.Process;
import android.widget.TextView;
import android.view.Gravity;
import java.io.File;

/** Keeps a foreground window while replacing the game's loaded managed assemblies. */
public final class RestartGameActivity extends Activity {
    private final Handler handler = new Handler(Looper.getMainLooper());
    private int oldPid;
    private Intent launch;
    private int attempts;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        TextView text = new TextView(this);
        text.setText("\u66f4\u65b0\u5b8c\u6210\uff0c\u6b63\u5728\u8fdb\u5165\u6e38\u620f\u2026");
        text.setTextSize(20);
        text.setGravity(Gravity.CENTER);
        setContentView(text);
        oldPid = getIntent().getIntExtra("gamePid", -1);
        launch = getIntent().getParcelableExtra("gameLaunch");
        if (launch == null || launch.getComponent() == null || !getPackageName().equals(launch.getComponent().getPackageName())
                || oldPid <= 0 || oldPid == Process.myPid()) {
            finish(); return;
        }
        // This component is non-exported; the PID and explicit intent originate in our game.
        Process.killProcess(oldPid);
        handler.postDelayed(this::startGame, 200);
    }

    private void startGame() {
        if (new File("/proc/" + oldPid).exists() && attempts++ < 50) {
            handler.postDelayed(this::startGame, 100);
            return;
        }
        launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        startActivity(launch);
        finish();
    }

    @Override protected void onDestroy() {
        handler.removeCallbacksAndMessages(null);
        super.onDestroy();
    }
}
