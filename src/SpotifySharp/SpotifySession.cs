﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace SpotifySharp
{
    // Maintenance note:
    //     Public methods of public classes should not use prefixes
    //     for argument names. These may appear in client code using
    //     named arguments.


    /// <summary>
    /// The Spotify Session.
    /// </summary>
    /// <remarks>
    /// All other interaction with Spotify requires first creating a session.
    /// 
    /// Currently, only one session may be created per process, but it is possible
    /// that future versions of the API will allow multiple sessions.
    /// </remarks>
    public sealed partial class SpotifySession : IDisposable
    {
        // Most of the SpotifySession methods are generated automatically and reside in
        // "GeneratedCode.cs". The remainder here involve extra work that is not as
        // easy to automate.

        /// <summary>
        /// Global table of session instances. Used to find the managed wrapper for
        /// a session when we enter a callback knowing only the native pointer.
        /// </summary>
        internal static readonly ManagedWrapperTable<SpotifySession> SessionTable = new ManagedWrapperTable<SpotifySession>(x=>new SpotifySession(x));

        /// <summary>
        /// Global table of session listeners. Used to find the managed listener
        /// when we enter a callback. Keyed by integer tokens that we store in
        /// the native session's userdata.
        /// </summary>
        internal static readonly ManagedListenerTable<SpotifySessionListener> ListenerTable = new ManagedListenerTable<SpotifySessionListener>();

        SpotifySessionListener iListener = new NullSessionListener();
        internal SpotifySessionListener Listener
        {
            get { return iListener; }
            set { iListener = value; }
        }
        internal IntPtr ListenerToken { get; set; }

        /// <summary>
        /// Create a SpotifySession, as per sp_session_create.
        /// </summary>
        /// <param name="config"></param>
        /// <returns>
        /// The SpotifySession. The caller is responsible for calling Dispose on
        /// the session.
        /// </returns>
        public static SpotifySession Create(SpotifySessionConfig config)
        {
            IntPtr sessionPtr = IntPtr.Zero;
            IntPtr listenerToken;
            using (var cacheLocation = SpotifyMarshalling.StringToUtf8(config.CacheLocation))
            using (var settingsLocation = SpotifyMarshalling.StringToUtf8(config.SettingsLocation))
            using (var userAgent = SpotifyMarshalling.StringToUtf8(config.UserAgent))
            using (var deviceId = SpotifyMarshalling.StringToUtf8(config.DeviceId))
            using (var proxy = SpotifyMarshalling.StringToUtf8(config.Proxy))
            using (var proxyUsername = SpotifyMarshalling.StringToUtf8(config.ProxyUsername))
            using (var proxyPassword = SpotifyMarshalling.StringToUtf8(config.ProxyPassword))
            using (var caCertsFilename = SpotifyMarshalling.StringToUtf8(config.CACertsFilename))
            using (var traceFile = SpotifyMarshalling.StringToUtf8(config.TraceFile))
            {
                IntPtr appKeyPtr = IntPtr.Zero;
                listenerToken = ListenerTable.PutUniqueObject(config.Listener, config.UserData);
                try
                {
                    NativeCallbackAllocation.AddRef();
                    byte[] appkey = config.ApplicationKey;
                    appKeyPtr = Marshal.AllocHGlobal(appkey.Length);
                    Marshal.Copy(config.ApplicationKey, 0, appKeyPtr, appkey.Length);
                    sp_session_config nativeConfig = new sp_session_config {
                        api_version = config.ApiVersion,
                        cache_location = cacheLocation.IntPtr,
                        settings_location = settingsLocation.IntPtr,
                        application_key = appKeyPtr,
                        application_key_size = (UIntPtr)appkey.Length,
                        user_agent = userAgent.IntPtr,
                        callbacks = SessionDelegates.CallbacksPtr,
                        userdata = listenerToken,
                        compress_playlists = config.CompressPlaylists,
                        dont_save_metadata_for_playlists = config.DontSaveMetadataForPlaylists,
                        initially_unload_playlists = config.InitiallyUnloadPlaylists,
                        device_id = deviceId.IntPtr,
                        proxy = proxy.IntPtr,
                        proxy_username = proxyUsername.IntPtr,
                        proxy_password = proxyPassword.IntPtr,
                        ca_certs_filename = caCertsFilename.IntPtr,
                        tracefile = traceFile.IntPtr,
                    };
                    // Note: sp_session_create will invoke a callback, so it's important that
                    // we have already done ListenerTable.PutUniqueObject before this point.
                    var error = NativeMethods.sp_session_create(ref nativeConfig, ref sessionPtr);
                    SpotifyMarshalling.CheckError(error);
                }
                catch
                {
                    ListenerTable.ReleaseObject(listenerToken);
                    NativeCallbackAllocation.ReleaseRef();
                    throw;
                }
                finally
                {
                    if (appKeyPtr != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(appKeyPtr);
                    }
                }
            }
            
            SpotifySession session = SessionTable.GetUniqueObject(sessionPtr);
            session.Listener = config.Listener;
            session.UserData = config.UserData;
            session.ListenerToken = listenerToken;
            return session;
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero) return;
            var error = NativeMethods.sp_session_release(_handle);
            SessionTable.ReleaseObject(_handle);
            ListenerTable.ReleaseObject(ListenerToken);
            NativeCallbackAllocation.ReleaseRef();
            _handle = IntPtr.Zero;
            SpotifyMarshalling.CheckError(error);
        }

        public object UserData { get; internal set; }
        public int OfflineTracksToSync()
        {
            return NativeMethods.sp_offline_tracks_to_sync(_handle);
        }
        public int OfflineNumPlaylists()
        {
            return NativeMethods.sp_offline_num_playlists(_handle);
        }
        public bool OfflineSyncGetStatus(ref OfflineSyncStatus status)
        {
            if (NativeMethods.sp_offline_sync_get_status(_handle, ref status))
            {
                return true;
            }
            status = new OfflineSyncStatus();
            return false;
        }
        public int OfflineTimeLeft()
        {
            return NativeMethods.sp_offline_time_left(_handle);
        }
    }


    /// <summary>
    /// Static methods to use as callbacks from native code.
    /// </summary>
    /// <remarks>
    /// We use static methods to remain compatible with MonoTouch, which
    /// disallows instance methods or closures as delegates.
    /// </remarks>
    static class SessionDelegates
    {
        static readonly sp_session_callbacks Callbacks = CreateCallbacks();
        public static IntPtr CallbacksPtr;

        public static void AllocNativeCallbacks()
        {
            int bytes = Marshal.SizeOf(typeof(sp_session_callbacks));
            IntPtr structPtr = Marshal.AllocHGlobal(bytes);
            Marshal.StructureToPtr(Callbacks, structPtr, false);
            CallbacksPtr = structPtr;
        }

        public static void FreeNativeCallbacks()
        {
            Marshal.FreeHGlobal(CallbacksPtr);
            CallbacksPtr = IntPtr.Zero;
        }

        static sp_session_callbacks CreateCallbacks()
        {
            return new sp_session_callbacks
            {
                logged_in = logged_in,
                logged_out = logged_out,
                metadata_updated = metadata_updated,
                connection_error = connection_error,
                message_to_user = MessageToUser,
                notify_main_thread = NotifyMainThread,
                music_delivery = MusicDelivery,
                play_token_lost = PlayTokenLost,
                log_message = LogMessage,
                end_of_track = EndOfTrack,
                streaming_error = StreamingError,
                userinfo_updated = UserinfoUpdated,
                start_playback = StartPlayback,
                stop_playback = StopPlayback,
                get_audio_buffer_stats = GetAudioBufferStats,
                offline_status_updated = OfflineStatusUpdated,
                offline_error = OfflineError,
                credentials_blob_updated = CredentialsBlobUpdated,
                connectionstate_updated = ConnectionstateUpdated,
                scrobble_error = ScrobbleError,
                private_session_mode_changed = PrivateSessionModeChanged
            };
        }

        struct SessionAndListener
        {
            public SpotifySession Session;
            public SpotifySessionListener Listener;
        }
        static SessionAndListener GetListener(IntPtr nativeSession)
        {
            SessionAndListener retVal = new SessionAndListener();
            retVal.Session = SpotifySession.SessionTable.GetUniqueObject(nativeSession); //  SpotifyMarshalling.GetManagedSession(nativeSession);
            IntPtr userdata = NativeMethods.sp_session_userdata(nativeSession);
            object managedUserdata;
            if (SpotifySession.ListenerTable.TryGetListener(userdata, out retVal.Listener, out managedUserdata))
            {
                return retVal;
            }
            retVal.Listener = null;
            return retVal;
        }
        
        static void logged_in(IntPtr @session, SpotifyError @error)
        {
            var context = GetListener(session);
            context.Listener.LoggedIn(context.Session, error);
        }
        static void logged_out(IntPtr @session)
        {
            var context = GetListener(session);
            context.Listener.LoggedOut(context.Session);
        }
        static void metadata_updated(IntPtr @session)
        {
            var context = GetListener(session);
            context.Listener.MetadataUpdated(context.Session);
        }
        static void connection_error(IntPtr @session, SpotifyError @error)
        {
            var context = GetListener(session);
            context.Listener.ConnectionError(context.Session,error);
        }
        static void MessageToUser(IntPtr @session, IntPtr @message)
        {
            var context = GetListener(session);
            context.Listener.MessageToUser(context.Session,SpotifyMarshalling.Utf8ToString(message));
        }
        static void NotifyMainThread(IntPtr @session)
        {
            var context = GetListener(session);
            context.Listener.NotifyMainThread(context.Session);
        }
        static int MusicDelivery(IntPtr @session, ref AudioFormat @format, IntPtr @frames, int @num_frames)
        {
            var context = GetListener(session);
            return context.Listener.MusicDelivery(context.Session,format, frames, num_frames);
        }
        static void PlayTokenLost(IntPtr @session)
        {
            var context = GetListener(session);
            context.Listener.PlayTokenLost(context.Session);
        }
        static void LogMessage(IntPtr @session, IntPtr @data)
        {
            var context = GetListener(session);
            context.Listener.LogMessage(context.Session,SpotifyMarshalling.Utf8ToString(data));
        }
        static void EndOfTrack(IntPtr @session)
        {
            var context = GetListener(session);
            context.Listener.EndOfTrack(context.Session);
        }
        static void StreamingError(IntPtr @session, SpotifyError @error)
        {
            var context = GetListener(session);
            context.Listener.StreamingError(context.Session,error);
        }
        static void UserinfoUpdated(IntPtr @session)
        {
            var context = GetListener(session);
            context.Listener.UserinfoUpdated(context.Session);
        }
        static void StartPlayback(IntPtr @session)
        {
            var context = GetListener(session);
            context.Listener.StartPlayback(context.Session);
        }
        static void StopPlayback(IntPtr @session)
        {
            var context = GetListener(session);
            context.Listener.StopPlayback(context.Session);
        }
        static void GetAudioBufferStats(IntPtr @session, ref AudioBufferStats @stats)
        {
            var context = GetListener(session);
            context.Listener.GetAudioBufferStats(context.Session,out stats);
        }
        static void OfflineStatusUpdated(IntPtr @session)
        {
            var context = GetListener(session);
            context.Listener.OfflineStatusUpdated(context.Session);
        }
        static void OfflineError(IntPtr @session, SpotifyError @error)
        {
            var context = GetListener(session);
            context.Listener.OfflineError(context.Session,error);
        }
        static void CredentialsBlobUpdated(IntPtr @session, IntPtr @blob)
        {
            var context = GetListener(session);
            context.Listener.CredentialsBlobUpdated(context.Session,SpotifyMarshalling.Utf8ToString(blob));
        }
        static void ConnectionstateUpdated(IntPtr @session)
        {
            var context = GetListener(session);
            context.Listener.ConnectionstateUpdated(context.Session);
        }
        static void ScrobbleError(IntPtr @session, SpotifyError @error)
        {
            var context = GetListener(session);
            context.Listener.ScrobbleError(context.Session,error);
        }
        static void PrivateSessionModeChanged(IntPtr @session, [MarshalAs(UnmanagedType.I1)]bool @is_private)
        {
            var context = GetListener(session);
            context.Listener.PrivateSessionModeChanged(context.Session, is_private);
        }
    }

    /// <summary>
    /// Responds to events produced by a SpotifySession.
    /// </summary>
    /// <remarks>
    /// Default behaviour is to ignore all events - subclasses should
    /// override the methods they are interested in. Subclasses should
    /// not call the overridden base-class method in their
    /// implementations.
    /// </remarks>
    public abstract class SpotifySessionListener
    {
        public virtual void LoggedIn(SpotifySession @session, SpotifyError @error) { }
        public virtual void LoggedOut(SpotifySession @session) { }
        public virtual void MetadataUpdated(SpotifySession @session) { }
        public virtual void ConnectionError(SpotifySession @session, SpotifyError @error) { }
        public virtual void MessageToUser(SpotifySession @session, string @message) { }
        public virtual void NotifyMainThread(SpotifySession @session) { }
        public virtual int MusicDelivery(SpotifySession @session, AudioFormat @format, IntPtr @frames, int @num_frames)
        {
            return 0;
        }
        public virtual void PlayTokenLost(SpotifySession @session) { }
        public virtual void LogMessage(SpotifySession @session, string @data) { }
        public virtual void EndOfTrack(SpotifySession @session) { }
        public virtual void StreamingError(SpotifySession @session, SpotifyError @error) { }
        public virtual void UserinfoUpdated(SpotifySession @session) { }
        public virtual void StartPlayback(SpotifySession @session) { }
        public virtual void StopPlayback(SpotifySession @session) { }
        public virtual void GetAudioBufferStats(SpotifySession @session, out AudioBufferStats @stats)
        {
            stats.samples = 0;
            stats.stutter = 0;
        }
        public virtual void OfflineStatusUpdated(SpotifySession @session) { }
        public virtual void OfflineError(SpotifySession @session, SpotifyError @error) { }
        public virtual void CredentialsBlobUpdated(SpotifySession @session, string @blob) { }
        public virtual void ConnectionstateUpdated(SpotifySession @session) { }
        public virtual void ScrobbleError(SpotifySession @session, SpotifyError @error) { }
        public virtual void PrivateSessionModeChanged(SpotifySession @session, bool @is_private) { }
    }

    public sealed class NullSessionListener : SpotifySessionListener
    {
        static SpotifySessionListener iInstance = new NullSessionListener();
        public static SpotifySessionListener Instance { get { return iInstance; } }
    }


    public class SpotifySessionConfig
    {
        public int ApiVersion { get; set; }
        public string CacheLocation { get; set; }
        public string SettingsLocation { get; set; }
        public byte[] ApplicationKey { get; set; }
        public string UserAgent { get; set; }
        public SpotifySessionListener Listener { get; set; }
        public object UserData { get; set; }
        //public IntPtr @userdata;
        public bool CompressPlaylists { get; set; }
        public bool DontSaveMetadataForPlaylists { get; set; }
        public bool InitiallyUnloadPlaylists { get; set; }
        public string DeviceId { get; set; }
        public string Proxy { get; set; }
        public string ProxyUsername { get; set; }
        public string ProxyPassword { get; set; }
        public string CACertsFilename { get; set; }
        public string TraceFile { get; set; }
    }

    /// <summary>
    /// Manage allocation and deallocation of structs containing callback pointers.
    /// </summary>
    /// <remarks>
    /// For each of the three struct-of-callbacks types used in libspotify, we have
    /// a single global instance that points to our static callback methods. In a
    /// C program we'd declare it as a global variable with simple static initialization
    /// for its contained function pointers, but in .NET we can't do that, so we
    /// need to allocate it on the native heap. To future-proof against libspotify
    /// supporting multiple sessions, we call AddRef whenever we create a session,
    /// and ReleaseRef whenever we destroy a session, so that our callback structures
    /// remain allocated whenever there is at least one spotify session extant.
    /// </remarks>
    static class NativeCallbackAllocation
    {
        static readonly object Monitor = new object();
        static int RefCount;
        public static void AddRef()
        {
            lock (Monitor)
            {
                if (RefCount == 0)
                {
                    PlaylistDelegates.AllocNativeCallbacks();
                    SessionDelegates.AllocNativeCallbacks();
                    PlaylistContainerDelegates.AllocNativeCallbacks();
                }
                RefCount++;
            }
        }
        public static void ReleaseRef()
        {
            lock (Monitor)
            {
                RefCount--;
                if (RefCount == 0)
                {
                    PlaylistDelegates.FreeNativeCallbacks();
                    SessionDelegates.FreeNativeCallbacks();
                    PlaylistContainerDelegates.FreeNativeCallbacks();
                }
            }
        }

    }

}