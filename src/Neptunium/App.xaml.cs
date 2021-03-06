﻿using Crystal3;
using Crystal3.Messaging;
using Crystal3.Navigation;
using Kimono.Controls.SnackBar;
using Microsoft.HockeyApp;
using Neptunium.Core;
using Neptunium.View;
using Neptunium.View.Dialog;
using Neptunium.ViewModel;
using Neptunium.ViewModel.Dialog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.Core;
using Windows.Gaming.Input;
using Windows.Networking.Connectivity;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

// The Blank Application template is documented at http://go.microsoft.com/fwlink/?LinkId=402347&clcid=0x409

namespace Neptunium
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    sealed partial class App : CrystalApplication
    {
        public static BackgroundAccessStatus BackgroundAccess { get; private set; }
        public static Queue<string> MessageQueue { get; private set; } = new Queue<string>();

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            App.Current.UnhandledException += Current_UnhandledException;

            //For Xbox One
            this.RequiresPointerMode = Windows.UI.Xaml.ApplicationRequiresPointerMode.WhenRequested;
            ElementSoundPlayer.State = ElementSoundPlayerState.Auto;

            Windows.System.MemoryManager.AppMemoryUsageLimitChanging += MemoryManager_AppMemoryUsageLimitChanging;
            Windows.System.MemoryManager.AppMemoryUsageIncreased += MemoryManager_AppMemoryUsageIncreased;

            if (!Debugger.IsAttached)
            {
                Microsoft.HockeyApp.HockeyClient.Current.Configure("2f0ab4c93b2341a0a4bbbd5ec98917f9", new TelemetryConfiguration()
                {
                    EnableDiagnostics = true
                }).SetExceptionDescriptionLoader((Exception ex) =>
                {
                    StringBuilder reportBuilder = new StringBuilder();
                    if (ex != null)
                    {
                        reportBuilder.AppendLine("Exception HResult: " + ex.HResult.ToString());
                        if (ex.InnerException != null)
                        {
                            reportBuilder.AppendLine("Inner-Exception: " + ex.InnerException.ToString());
                        }

                        reportBuilder.AppendLine();
                    }

                    reportBuilder.AppendLine("Platform: " + Enum.GetName(typeof(Crystal3.Core.Platform), DeviceInformation.GetDevicePlatform()));
                    reportBuilder.AppendLine("Is Playing?: " + NepApp.MediaPlayer.IsPlaying);
                    reportBuilder.AppendLine("Current Station: " + NepApp.MediaPlayer.CurrentStream != null ? NepApp.MediaPlayer.CurrentStream.ParentStation : "None");

                    if (NepApp.MediaPlayer.CurrentStream != null)
                    {
                        reportBuilder.AppendLine("Station Stream: " + NepApp.MediaPlayer.CurrentStream.ToString());
                    }

                    reportBuilder.AppendLine("Is Casting?: " + NepApp.MediaPlayer.IsCasting);
                    reportBuilder.AppendLine("Is Sleep Timer Running?: " + NepApp.MediaPlayer.SleepTimer.IsSleepTimerRunning);


                    return reportBuilder.ToString();
                });

                TaskScheduler.UnobservedTaskException += (sender, args) =>
                {
                    //https://stackoverflow.com/a/15804433

                    foreach (var ex in args.Exception.InnerExceptions)
                    {
                        Microsoft.HockeyApp.HockeyClient.Current.TrackException(ex);
                    }
                    args.SetObserved();
                };
            }
        }

        private async void Current_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            e.Handled = true;

            var exception = e.Exception;
            if (!System.Diagnostics.Debugger.IsAttached)
            {
                if (exception is NeptuniumException)
                {
                    if (await GetIfPrimaryWindowVisibleAsync())
                    {
                        await NepApp.UI.ShowInfoDialogAsync("Uh-oh! Something went wrong!", e.Message);
                    }
                }

                Dictionary<string, string> dictionary = new Dictionary<string, string>();
                dictionary.Add("Original-Message", e.Message);
                HockeyClient.Current.TrackException(exception, dictionary);
                HockeyClient.Current.Flush();
            }
            else
            {
                System.Diagnostics.Debugger.Break();
            }
        }

        internal static void RegisterUIDialogs()
        {
            if (DeviceInformation.GetDevicePlatform() == Crystal3.Core.Platform.Xbox)
            {
                NepApp.UI.Overlay.RegisterDialogFragment<StationInfoDialogFragment, XboxStationInfoDialog>();
            }
            else
            {
                NepApp.UI.Overlay.RegisterDialogFragment<StationInfoDialogFragment, StationInfoDialog>();
            }

            NepApp.UI.Overlay.RegisterDialogFragment<StationHandoffDialogFragment, StationHandoffDialog>();
            NepApp.UI.Overlay.RegisterDialogFragment<SleepTimerDialogFragment, SleepTimerDialog>();
        }

        private static volatile bool isInBackground = false;
        private static volatile bool isAppVisible = false;
        private static volatile bool isAppFocused = true; //assume true

        protected override Task OnForegroundingAsync()
        {
            isInBackground = false;
            return base.OnForegroundingAsync();
        }
        protected override Task OnBackgroundingAsync()
        {
            isInBackground = true;
            return base.OnBackgroundingAsync();
        }

        #region Memory reduction stuff based on https://msdn.microsoft.com/en-us/windows/uwp/audio-video-camera/background-audio
        private void MemoryManager_AppMemoryUsageLimitChanging(object sender, AppMemoryUsageLimitChangingEventArgs e)
        {
            if (MemoryManager.AppMemoryUsage >= e.NewLimit)
            {
                ReduceMemoryUsage(e.NewLimit);
            }
        }

        private void MemoryManager_AppMemoryUsageIncreased(object sender, object e)
        {
            var level = MemoryManager.AppMemoryUsageLevel;

            if (level == AppMemoryUsageLevel.OverLimit || level == AppMemoryUsageLevel.High)
            {
                ReduceMemoryUsage(MemoryManager.AppMemoryUsageLimit);
            }
        }

        public void ReduceMemoryUsage(ulong limit)
        {
            if (isInBackground)
            {
                GC.Collect();
            }
        }
        #endregion

        protected override void OnConfigure()
        {
            base.OnConfigure();

            if (DeviceInformation.GetDevicePlatform() == Crystal3.Core.Platform.Desktop)
            {
#if DEBUG
                //todo not actually a todo but a PSA: to do Xbox testing on PC, set a breakpoint on Gamepads.Count or uncomment the Debugger.Break code.
                //then however over .Count twice with a breakpoint once to force system to recognize xbox one controller.
                //if (Debugger.IsAttached)
                //    Debugger.Break();

                if (Gamepad.Gamepads.Count > 0)
                {
                    this.Options.OverridePlatform(Crystal3.Core.Platform.Xbox);
                }
#endif
            }

            this.Options.HandleSystemBackNavigation = true;
            this.Options.HandleBackButtonForTopLevelNavigation = true;
        }

        protected override async Task OnApplicationInitializedAsync()
        {
            var coreApplicationView = CoreApplication.GetCurrentView();
            if (coreApplicationView != null)
            {
                coreApplicationView.TitleBar.ExtendViewIntoTitleBar = true;
            }

            ConfigureSnackBar();

            if (DeviceInformation.GetDevicePlatform() == Crystal3.Core.Platform.Xbox && !DeviceInformation.IsPlatformOverridden())
            {
                Windows.UI.ViewManagement.ApplicationView.GetForCurrentView()
                    .SetDesiredBoundsMode(Windows.UI.ViewManagement.ApplicationViewBoundsMode.UseCoreWindow);

                //https://docs.microsoft.com/en-us/windows/uwp/xbox-apps/disable-scaling
                //todo make this a feature. for now, the xbox UI isn't designed to be at 1080p+
                //bool result = Windows.UI.ViewManagement.ApplicationViewScaling.TrySetDisableLayoutScaling(true);
            }

            Window.Current.Activated += Current_Activated;

            await NepApp.InitializeAsync();
        }

        private static void ConfigureSnackBar()
        {
            UISettings uiSettings = new UISettings();

            SnackBarAppearance.Opacity = 1;
            SnackBarAppearance.Transition = new PopupThemeTransition();

            //check for acrylic support
            if (Windows.Foundation.Metadata.ApiInformation.IsTypePresent("Windows.UI.Xaml.Media.XamlCompositionBrushBase")
                && Windows.Foundation.Metadata.ApiInformation.IsTypePresent("Windows.UI.Xaml.Media.AcrylicBrush"))
            {
                //Add acrylic.

                Windows.UI.Xaml.Media.AcrylicBrush myBrush = new Windows.UI.Xaml.Media.AcrylicBrush();
                myBrush.BackgroundSource = Windows.UI.Xaml.Media.AcrylicBackgroundSource.Backdrop;
                myBrush.TintColor = uiSettings.GetColorValue(UIColorType.AccentDark2);
                myBrush.FallbackColor = uiSettings.GetColorValue(UIColorType.AccentDark2);
                myBrush.Opacity = 0.6;
                myBrush.TintOpacity = 0.5;

                SnackBarAppearance.BackgroundBrush = myBrush;
            }
            else
            {
                //fallback to a solid color.
                SnackBarAppearance.BackgroundBrush = new SolidColorBrush(uiSettings.GetColorValue(UIColorType.Accent));
            }
        }

        private async Task PostUIInitAsync()
        {
            if ((BackgroundAccess = BackgroundExecutionManager.GetAccessStatus()) == BackgroundAccessStatus.Unspecified)
            {
                BackgroundAccess = await BackgroundExecutionManager.RequestAccessAsync();
            }

            Window.Current.VisibilityChanged += Current_VisibilityChanged;
            isAppVisible = Window.Current.Visible;
        }

        private void Current_Activated(object sender, WindowActivatedEventArgs e)
        {
            //to deal with the app losing focus on the desktop.
            isAppFocused = e.WindowActivationState != CoreWindowActivationState.Deactivated;
#if DEBUG
            Debug.WriteLine("Window-Activation-State: " + Enum.GetName(typeof(CoreWindowActivationState), e.WindowActivationState));
#endif
        }

        private void Current_VisibilityChanged(object sender, VisibilityChangedEventArgs e)
        {
            isAppVisible = e.Visible; //on desktop, this means the app was minimized using the button. on mobile, this can be when the app is suspending or the phone is locked.
        }

        public override async Task OnFreshLaunchAsync(LaunchActivatedEventArgs args)
        {
            WindowManager.GetNavigationManagerForCurrentView()
                .RootNavigationService.NavigateTo<AppShellViewModel>();
            await PostUIInitAsync();
        }

        public override async Task OnActivationAsync(IActivatedEventArgs args)
        {
            if (args.PreviousExecutionState != ApplicationExecutionState.Running)
            {
                //First, initializes the shell if it isn't already running.
                WindowManager.GetNavigationManagerForCurrentView()
                .RootNavigationService.SafeNavigateTo<AppShellViewModel>();
            }

            if (args.Kind == ActivationKind.Protocol)
            {
                //This handles being launched from a uri
                var pargs = args as ProtocolActivatedEventArgs;

                var uri = pargs.Uri;
                await ExecuteQueryCommandsAsync(uri);
            }
            else if (args.Kind == ActivationKind.Launch && (args is LaunchActivatedEventArgs))
            {
                //tile activation. crystal wouldn't call here otherwise.

                var largs = args as LaunchActivatedEventArgs;
                if (!string.IsNullOrWhiteSpace(largs.Arguments))
                {
                    await ExecuteQueryCommandsAsync(new Uri("nep:" + largs.Arguments));
                }
            }
            else if (args.Kind == ActivationKind.ToastNotification && args.PreviousExecutionState == ApplicationExecutionState.Running)
            {
                //App/Xbox shell does use the overlay at this time.
                await Messenger.SendMessageAsync("ShowNowPlayingOverlay", null);
            }
        }

        private static async Task ExecuteQueryCommandsAsync(Uri uri)
        {
            switch (uri.LocalPath.ToLower())
            {
                case "play-station":
                    {
                        var query = uri.Query
                            .Substring(1)
                            .Split('&')
                            .Select(x =>
                                new KeyValuePair<string, string>(
                                    x.Split('=')[0],
                                    x.Split('=')[1])); //remote the "?"

                        var stationName = query.First(x => x.Key.ToLower() == "station").Value;
                        stationName = stationName.Replace("%20", " ");

                        try
                        {
                            var station = await NepApp.Stations.GetStationByNameAsync(stationName);
                            await NepApp.MediaPlayer.TryStreamStationAsync(station.Streams[0]);
                        }
                        catch (Exception ex)
                        {
                            //todo show error message.
                            await NepApp.UI.ShowInfoDialogAsync("Unable to play station.", "The following error occurred: " + ex.ToString());
                        }
                        break;
                    }
            }
        }

        internal static bool GetIfPrimaryWindowVisible()
        {
            if (DeviceInformation.GetDevicePlatform() == Crystal3.Core.Platform.Desktop)
            {
                return isAppVisible && !isInBackground && isAppFocused;
            }
            else
            {
                return isAppVisible && !isInBackground;
            }
        }
        internal static async Task<bool> GetIfPrimaryWindowVisibleAsync()
        {
            return await App.Dispatcher.RunWhenIdleAsync(() =>
            {
                return App.GetIfPrimaryWindowVisible();
            });
        }

        public static bool IsInternetConnected()
        {
            ConnectionProfile connections = NetworkInformation.GetInternetConnectionProfile();
            bool internet = (connections != null) &&
                (connections.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess);
            return internet;
        }
        public static bool IsUnrestrictiveInternetConnection()
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile != null)
            {
                var cost = profile.GetConnectionCost();

                return cost.NetworkCostType == NetworkCostType.Unrestricted
                    || (cost.NetworkCostType == NetworkCostType.Fixed && cost.ApproachingDataLimit == false);
            }

            return true;
        }

        protected override Task OnSuspendingAsync()
        {
            NepApp.UI.ClearLiveTileAndMediaNotifcation();

            return Task.CompletedTask;
        }

        protected override async Task OnResumingAsync()
        {
            await base.OnResumingAsync();
        }

        public override Task OnBackgroundActivatedAsync(BackgroundActivatedEventArgs args)
        {
            switch (args.TaskInstance.Task.Name)
            {
                default:
                    if (args.TaskInstance.TriggerDetails is AppServiceTriggerDetails)
                    {
                        var asTD = args.TaskInstance.TriggerDetails as AppServiceTriggerDetails;
                        switch (asTD.Name)
                        {
                            case NepAppHandoffManager.ContinuedAppExperienceAppServiceName:
                                NepApp.Handoff.HandleBackgroundActivation(asTD); //handles any messages aimed at the handoff manager from a remote devices
                                break;
                        }
                    }
                    break;
            }
            return Task.CompletedTask;
        }
    }
}
