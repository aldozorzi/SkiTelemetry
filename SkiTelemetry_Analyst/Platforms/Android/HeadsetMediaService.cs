#if ANDROID
using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Support.V4.Media;
using Android.Support.V4.Media.Session;
using AndroidX.Core.App;
using AndroidX.Media;
using CommunityToolkit.Mvvm.Messaging;



[Service(Exported = true, ForegroundServiceType = Android.Content.PM.ForegroundService.TypeMediaPlayback)]
[IntentFilter(new[] { "android.media.browse.MediaBrowserService" })]
public class HeadsetMediaService : MediaBrowserServiceCompat
{
    private MediaSessionCompat? _mediaSession;
    private AudioTrack? _silentTrack;

    public override void OnCreate()
    {
        base.OnCreate();
        Android.Util.Log.Debug("HeadsetMediaService", "OnCreate chiamato");
        StartForeground();
        RequestAudioFocus();
        InitMediaSession();
        StartSilentAudio();

    }
    private void StartSilentAudio()
    {
        var sampleRate = 44100;
        var bufferSize = AudioTrack.GetMinBufferSize(sampleRate, ChannelOut.Stereo, Encoding.Pcm16bit);

        var audioAttributesBuilder = new AudioAttributes.Builder();
        audioAttributesBuilder.SetUsage(AudioUsageKind.Media);
        audioAttributesBuilder.SetContentType(AudioContentType.Music);
        var audioAttributes = audioAttributesBuilder.Build();
        if (audioAttributes == null) return;

        var audioFormatBuilder = new AudioFormat.Builder();
        audioFormatBuilder.SetEncoding(Encoding.Pcm16bit);
        audioFormatBuilder.SetSampleRate(sampleRate);
        audioFormatBuilder.SetChannelMask(ChannelOut.Stereo);
        var audioFormat = audioFormatBuilder.Build();
        if (audioFormat == null) return;

        var audioTrackBuilder = new AudioTrack.Builder();
        audioTrackBuilder.SetAudioAttributes(audioAttributes);
        audioTrackBuilder.SetAudioFormat(audioFormat);
        audioTrackBuilder.SetBufferSizeInBytes(bufferSize);
        audioTrackBuilder.SetTransferMode(AudioTrackMode.Stream);
        _silentTrack = audioTrackBuilder.Build();

        var silence = new short[bufferSize];
        _silentTrack.Play();
        _silentTrack.Write(silence, 0, silence.Length);

        Android.Util.Log.Debug("HeadsetMediaService", "Silent audio started");
    }
    
    private void StartForeground()
    {
        const string channelId = "headset_channel";
        const int notificationId = 1001;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(channelId, "Headset Service", NotificationImportance.Low);
            var notificationManager = GetSystemService(NotificationService) as NotificationManager;
            notificationManager?.CreateNotificationChannel(channel);
        }
        var builder = new NotificationCompat.Builder(this, channelId);
        builder.SetContentTitle("SkiTelemetry");
        builder.SetContentText("Controllo remoto");
        builder.SetSmallIcon(SkiTelemetry_Analyst.Resource.Drawable.notification_icon);
        builder.SetOngoing(true);
        var notification = builder.Build();
        if (notification == null) return;

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(notificationId, notification, Android.Content.PM.ForegroundService.TypeMediaPlayback);
        else
            StartForeground(notificationId, notification);
    }
    private void RequestAudioFocus()
    {
        var audioManager = GetSystemService(AudioService) as AudioManager;
        var audioattributesbuilder = new AudioAttributes.Builder();
        audioattributesbuilder.SetUsage(AudioUsageKind.Media);
        audioattributesbuilder.SetContentType(AudioContentType.Music);
        var attributes = audioattributesbuilder.Build();
        if (attributes == null) return;
        var requestclassbuilder = new AudioFocusRequestClass.Builder(AudioFocus.Gain);
        requestclassbuilder.SetAudioAttributes(attributes);
        requestclassbuilder.SetWillPauseWhenDucked(false);

        var focusRequest = requestclassbuilder.Build();
        if (focusRequest == null) return;
        var result = audioManager?.RequestAudioFocus(focusRequest);
        Android.Util.Log.Debug("HeadsetMediaService", $"AudioFocus result: {result}");
    }
    private void InitMediaSession()
    {
        _mediaSession = new MediaSessionCompat(this, "HeadsetMediaService");
        _mediaSession.SetCallback(new HeadsetMediaCallback());

        var stateBuilder = new PlaybackStateCompat.Builder();
        stateBuilder.SetActions(PlaybackStateCompat.ActionPlay | PlaybackStateCompat.ActionPause);
        stateBuilder.SetState(PlaybackStateCompat.StatePlaying, 0, 1.0f);
        var state = stateBuilder.Build();

        _mediaSession.SetPlaybackState(state);
        _mediaSession.Active = true;

        SessionToken = _mediaSession.SessionToken;

        Android.Util.Log.Debug("HeadsetMediaService", $"MediaSession active: {_mediaSession.Active}");
    }

    public override BrowserRoot? OnGetRoot(string clientPackageName, int clientUid, Bundle? rootHints)
    {
        return new BrowserRoot("root", null);
    }

    public override void OnLoadChildren(string parentId, Result result)
    {
        result.SendResult(new JavaList<MediaBrowserCompat.MediaItem>());
    }

    public override void OnDestroy()
    {
        Android.Util.Log.Debug("HeadsetMediaService", "OnDestroy chiamato");
        _silentTrack?.Stop();
        _silentTrack?.Release();
        _silentTrack = null;

        _mediaSession?.Release();
        base.OnDestroy();
    }
}

public class HeadsetMediaCallback : MediaSessionCompat.Callback
{
    public override bool OnMediaButtonEvent(Intent? mediaButtonIntent)
    {
        Android.Util.Log.Debug("HeadsetMediaCB", "OnMediaButtonEvent chiamato");

        var keyEvent = mediaButtonIntent?.GetParcelableExtraCompat<Android.Views.KeyEvent>(Intent.ExtraKeyEvent);

        if (keyEvent?.Action == Android.Views.KeyEventActions.Down)
        {
            switch (keyEvent.KeyCode)
            {
                // Il classico tasto cuffie (quello che usi ora)
                case Android.Views.Keycode.Headsethook:
                // Il tasto centrale "Play/Pause" di molti telecomandi BT
                case Android.Views.Keycode.MediaPlayPause:
                // Il tasto "Enter" (molti shutter inviano questo col tasto centrale)
                case Android.Views.Keycode.Enter:
                // Il tasto centrale dei D-Pad (comune in quel modello della foto)
                case Android.Views.Keycode.DpadCenter:
                    WeakReferenceMessenger.Default.Send("HeadsetHook");
                    return true;
            }
        }

        return base.OnMediaButtonEvent(mediaButtonIntent);
    }

    public override void OnPlay()
    {
        Android.Util.Log.Debug("HeadsetMediaCB", "OnPlay chiamato");
        WeakReferenceMessenger.Default.Send("HeadsetHook");
    }

    public override void OnPause()
    {
        Android.Util.Log.Debug("HeadsetMediaCB", "OnPause chiamato");
        WeakReferenceMessenger.Default.Send("HeadsetHook");
    }
}

public static class IntentExtensions
{
    public static T? GetParcelableExtraCompat<T>(this Intent intent, string name) where T : Java.Lang.Object
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return intent.GetParcelableExtra(name, Java.Lang.Class.FromType(typeof(T))) as T;
        return intent.GetParcelableExtra(name) as T;
    }
}
#endif