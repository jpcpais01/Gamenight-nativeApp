package com.godot.game;

import org.godotengine.godot.Godot;
import org.godotengine.godot.GodotActivity;

import android.app.Activity;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.PackageInstaller;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.util.Log;
import android.view.Display;
import android.view.WindowManager;

import androidx.activity.EdgeToEdge;
import androidx.core.splashscreen.SplashScreen;

import java.io.File;
import java.io.FileInputStream;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * GameNight's activity: Godot 4.7's template activity, plus one thing Godot doesn't do on its
 * own: ask Android for the display's fastest refresh rate. Without it many phones (Xiaomi's
 * HyperOS among them) run a game's window at 60 Hz, and Godot can never draw faster than the
 * screen refreshes. It also installs the app's own updates (see {@link #installApk}). The CI
 * workflow copies this file over the template's before building.
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
		instance = this;
		IntentFilter filter = new IntentFilter(INSTALLED);
		if (Build.VERSION.SDK_INT >= 26) {
			registerReceiver(installReceiver, filter, Build.VERSION.SDK_INT >= 33 ? Context.RECEIVER_NOT_EXPORTED : 0);
		} else {
			registerReceiver(installReceiver, filter);
		}

		Godot godot = getGodot();
		if (godot != null && godot.getDisableGodotSplash()) {
			splashScreen.setKeepOnScreenCondition(() -> godot.getRunStatus() != Godot.RunStatus.STARTED);
		}
	}

	@Override
	public void onDestroy() {
		try {
			unregisterReceiver(installReceiver);
		} catch (Exception ignored) {
		}
		if (instance == this) {
			instance = null;
		}
		super.onDestroy();
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

	// ------------------------------------------------------------------ self-update
	// Called from C# through JavaClassWrapper (game/Scripts/Update/Updater.cs). The app hands its
	// own new APK to Android's package installer, which asks the player to confirm and installs
	// it over this version (same id, same key, higher version code).

	private static final String INSTALLED = "com.jpcpais.gamenight.INSTALLED";
	private static volatile GodotApp instance;
	private static volatile String installStatus = "";

	/** Whether Android lets this app install APKs ("Install unknown apps" for GameNight). */
	public static boolean canInstall() {
		Activity a = instance;
		return a != null && (Build.VERSION.SDK_INT < 26 || a.getPackageManager().canRequestPackageInstalls());
	}

	/** Where the last install got to: installing, confirm, done, cancelled or error:... */
	public static String installStatus() {
		return installStatus;
	}

	/**
	 * Install the APK at path as an update. Returns "permission" (the settings page to allow it
	 * was opened; call again once {@link #canInstall} is true), "started" or "error:...".
	 */
	public static String installApk(final String path) {
		final GodotApp a = instance;
		if (a == null) {
			return "error:no activity";
		}
		if (!canInstall()) {
			a.runOnUiThread(() -> {
				try {
					a.startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + a.getPackageName())));
				} catch (Exception e) {
					Log.w("GameNight", "Could not open the install permission page", e);
				}
			});
			return "permission";
		}
		installStatus = "installing";
		new Thread(() -> {
			try {
				PackageInstaller installer = a.getPackageManager().getPackageInstaller();
				PackageInstaller.SessionParams params = new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);
				params.setAppPackageName(a.getPackageName());
				int id = installer.createSession(params);
				try (PackageInstaller.Session session = installer.openSession(id)) {
					File apk = new File(path);
					try (InputStream in = new FileInputStream(apk); OutputStream out = session.openWrite("GameNight.apk", 0, apk.length())) {
						byte[] buffer = new byte[1 << 16];
						int n;
						while ((n = in.read(buffer)) > 0) {
							out.write(buffer, 0, n);
						}
						session.fsync(out);
					}
					Intent done = new Intent(INSTALLED).setPackage(a.getPackageName());
					int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 31 ? PendingIntent.FLAG_MUTABLE : 0);
					session.commit(PendingIntent.getBroadcast(a, id, done, flags).getIntentSender());
				}
			} catch (Exception e) {
				Log.w("GameNight", "Update install failed", e);
				installStatus = "error:" + e.getMessage();
			}
		}).start();
		return "started";
	}

	/** The installer's answers: first "needs the player's OK" (show its dialog), then the outcome. */
	private final BroadcastReceiver installReceiver = new BroadcastReceiver() {
		@Override
		@SuppressWarnings("deprecation")
		public void onReceive(Context context, Intent intent) {
			int status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE);
			if (status == PackageInstaller.STATUS_PENDING_USER_ACTION) {
				Intent confirm = intent.getParcelableExtra(Intent.EXTRA_INTENT);
				if (confirm != null) {
					confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
					startActivity(confirm);
				}
				installStatus = "confirm";
			} else if (status == PackageInstaller.STATUS_SUCCESS) {
				installStatus = "done";
			} else if (status == PackageInstaller.STATUS_FAILURE_ABORTED) {
				installStatus = "cancelled";
			} else {
				String message = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE);
				installStatus = "error:" + (message != null ? message : "install failed (" + status + ")");
			}
		}
	};
}
