using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using CommunityToolkit.Mvvm.Messaging;


namespace SkiTelemetry_Analyst
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Avvia il MediaBrowserService
            var serviceIntent = new Intent(this, typeof(HeadsetMediaService));
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                StartForegroundService(serviceIntent);
            else
                StartService(serviceIntent);

            // Richiedi permessi Android 13+
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                if (CheckSelfPermission(Android.Manifest.Permission.PostNotifications)
                    != Android.Content.PM.Permission.Granted)
                {
                    RequestPermissions(
                        new[] { Android.Manifest.Permission.PostNotifications }, 1001);
                }



            }
        }
        public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
        {

            switch (keyCode)
            {
                case Keycode.Headsethook:    // Tasto cuffie standard
                case Keycode.MediaPlayPause: // Tasto Play/Pausa universale
                case Keycode.Enter:          // Spesso inviato dai telecomandi "shutter" (Codice 66)
                case Keycode.DpadCenter:     // Il tasto centrale dei D-Pad (Codice 23)
                    WeakReferenceMessenger.Default.Send("HeadsetHook");
                    return true;
            }
            return base.OnKeyDown(keyCode, e);
        }

    }
    


    }
