package com.godot.game;

import org.godotengine.godot.Godot;
import org.godotengine.godot.GodotActivity;

import android.os.Build;
import android.os.Bundle;
import android.util.Log;
import android.view.Display;
import android.view.WindowManager;

import androidx.activity.EdgeToEdge;
import androidx.core.splashscreen.SplashScreen;

/**
 * GameNight's activity: Godot 4.7's template activity, plus one thing Godot doesn't do on its
 * own: ask Android for the display's fastest refresh rate. Without it many phones (Xiaomi's
 * HyperOS among them) run a game's window at 60 Hz, and Godot can never draw faster than the
 * screen refreshes. The CI workflow copies this file over the template's before building.
 */
public class GodotApp extends GodotActivity {
	static {
		// .NET libraries.
		if (BuildConfig.FLAVOR.equals("mono")) {
			try {
				Log.v("GODOT", "Loading System.Security.Cryptography.Native.Android library");
				System.loadLibrary("System.Security.Cryptography.Native.Android");
			} catch (UnsatisfiedLinkError e) {
				Log.e("GODOT", "Unable to load System.Security.Cryptography.Native.Android library");
			}
		}
	}

	private final Runnable updateWindowAppearance = () -> {
		Godot godot = getGodot();
		if (godot != null) {
			godot.enableImmersiveMode(godot.isInImmersiveMode(), true);
			godot.enableEdgeToEdge(godot.isInEdgeToEdgeMode(), true);
			godot.setSystemBarsAppearance();
		}
	};

	@Override
	public void onCreate(Bundle savedInstanceState) {
		SplashScreen splashScreen = SplashScreen.installSplashScreen(this);
		EdgeToEdge.enable(this);
		super.onCreate(savedInstanceState);
		requestHighestRefreshRate();

		Godot godot = getGodot();
		if (godot != null && godot.getDisableGodotSplash()) {
			splashScreen.setKeepOnScreenCondition(() -> godot.getRunStatus() != Godot.RunStatus.STARTED);
		}
	}

	@Override
	public void onResume() {
		super.onResume();
		requestHighestRefreshRate();
		updateWindowAppearance.run();
	}

	@Override
	public void onGodotMainLoopStarted() {
		super.onGodotMainLoopStarted();
		runOnUiThread(updateWindowAppearance);
	}

	@Override
	public void onGodotForceQuit(Godot instance) {
		if (!BuildConfig.FLAVOR.equals("instrumented")) {
			super.onGodotForceQuit(instance);
		}
	}

	@Override
	protected boolean isPiPEnabled() {
		return true;
	}

	/** Pick the display mode with the highest refresh rate at the current resolution. */
	@SuppressWarnings("deprecation")
	private void requestHighestRefreshRate() {
		try {
			Display display = Build.VERSION.SDK_INT >= Build.VERSION_CODES.R ? getDisplay() : getWindowManager().getDefaultDisplay();
			if (display == null) {
				return;
			}
			Display.Mode current = display.getMode();
			Display.Mode best = current;
			for (Display.Mode mode : display.getSupportedModes()) {
				if (mode.getPhysicalWidth() == current.getPhysicalWidth()
						&& mode.getPhysicalHeight() == current.getPhysicalHeight()
						&& mode.getRefreshRate() > best.getRefreshRate()) {
					best = mode;
				}
			}
			WindowManager.LayoutParams params = getWindow().getAttributes();
			params.preferredDisplayModeId = best.getModeId();
			params.preferredRefreshRate = best.getRefreshRate();
			getWindow().setAttributes(params);
			Log.i("GameNight", "Requested display mode " + best.getModeId() + " at " + best.getRefreshRate() + " Hz");
		} catch (Exception e) {
			Log.w("GameNight", "Could not request a high refresh rate", e);
		}
	}
}
