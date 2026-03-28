using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using CommunityToolkit.Mvvm.Messaging;

namespace SkiTelemetry_Analyst
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
        {
            if (keyCode == Keycode.Headsethook || keyCode == Keycode.MediaPlayPause)
            {
                // Se questo non logga nulla nel debug, Android 15 sta bloccando il segnale a livello kernel
                System.Diagnostics.Debug.WriteLine("TASTO CUFFIE RILEVATO");
                WeakReferenceMessenger.Default.Send("HeadsetHook");
                return true;
            }
            return base.OnKeyDown(keyCode, e);
        }
    }

}
