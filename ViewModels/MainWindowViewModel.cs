using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.OpenGL;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Simple_Doomsday_Engine_Launcher.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Simple_Doomsday_Engine_Launcher.ViewModels
{

    // Simple Doomsday Engine Launcher
    // Created by: Ron Goode ~aka~ Mr.Rocket
    // 2026-08-03



    public class MainViewModel : ViewModelBase
    {
        // ================= FIELDS =================


        public bool IsLaunchConfigReady
        {
            get
            {
               
                if (IsGameRunning) return false;

                return !string.IsNullOrWhiteSpace(EngineLocation) &&
                       System.IO.File.Exists(EngineLocation) &&
                       !string.IsNullOrWhiteSpace(IWADFolder) &&
                       System.IO.Directory.Exists(IWADFolder);
            }
        }




        public bool NeedsConfiguration
        {
            get
            {
                
                return !IsLaunchConfigReady;
            }
        }

        private bool _isGameRunning = false;
        public bool IsGameRunning
        {
            get => _isGameRunning;
            set
            {
                if (SetProperty(ref _isGameRunning, value))
                {
                    // Update dependent properties so the button instantly changes color/state
                    OnPropertyChanged(nameof(IsLaunchConfigReady));
                    OnPropertyChanged(nameof(NeedsConfiguration));
                }
            }
        }



        // send heartbeat to the database
        private Timer? _heartbeatTimer;
        private object _heartbeatLock = new object();

        private string _activeDatabaseKey = "";


        // updateer
        public ICommand CheckForUpdatesCommand => new AsyncRelayCommand(CheckForUpdates);

        private bool _isUpdating = false;

        public bool CanCheckForUpdates =>
    !string.IsNullOrWhiteSpace(EngineLocation);

      
        public bool IsUpdating
        {
            get => _isUpdating;
            set => SetProperty(ref _isUpdating, value);
        }

        private string _installedVersion;
        public string InstalledVersion
        {
            get => _installedVersion;
            set => SetProperty(ref _installedVersion, value);
        }

        private bool _isDoomsdayInstalled;
        public bool IsDoomsdayInstalled
        {
            get => _isDoomsdayInstalled;
            set
            {
                if (SetProperty(ref _isDoomsdayInstalled, value))
                    OnPropertyChanged(nameof(ShowInstallButton));
            }
        }

        public bool ShowInstallButton => !IsDoomsdayInstalled;


        public class DoomsdayBuild
        {
            [JsonProperty("version")]
            public string Version { get; set; }

            [JsonProperty("filename")]
            public string FileName { get; set; }

            [JsonProperty("url")]
            public string Url { get; set; }
        }


        


        private LogWindow? _logWindow;
        private LogWindowViewModel? _logVM;

        private void Log(string message)
        {
            UpdateStatus = message;

            LogText += message + Environment.NewLine;
        }




        private Process _currentServerProcess = null;

        private readonly string SettingsPath = Path.Combine(
    AppContext.BaseDirectory, "launcher_settings.json");


        // ================= PROPERTIES =================

        public ICommand BrowseEngineCommand { get; }
        public ICommand BrowseIwadFolderCommand { get; }
        public ICommand BrowseServerCommand { get; }
        public ICommand BrowsePwadCommand { get; }
        public ICommand ClearPwadCommand { get; }

        public ICommand OpenLogCommand => new RelayCommand(OpenLog);

        public ICommand InstallDoomsdayCommand { get; }

        private string _engineLocation;
        public string EngineLocation
        {
            get => _engineLocation;
            set
            {
                if (SetProperty(ref _engineLocation, value))
                {
                    // Recalculate install state
                    IsDoomsdayInstalled =
                        !string.IsNullOrWhiteSpace(value) &&
                        File.Exists(value);

                    // Update button visibility
                    OnPropertyChanged(nameof(ShowInstallButton));
                    OnPropertyChanged(nameof(CanCheckForUpdates));

                    // Is the path valid yet?
                    OnPropertyChanged(nameof(NeedsConfiguration));


                    // re-evaluates the glow state when paths update 
                    OnPropertyChanged(nameof(IsLaunchConfigReady));

                    // Auto-detect server exe
                    TryAutoDetectServer();
                }
            }
        }

        public class RelayCommand : ICommand
        {
            private readonly Action _execute;
            private readonly Func<bool>? _canExecute;

            public RelayCommand(Action execute, Func<bool>? canExecute = null)
            {
                _execute = execute;
                _canExecute = canExecute;
            }

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

            public void Execute(object? parameter) => _execute();
        }


        private string _serverLocation;
        public string ServerLocation
        {
            get => _serverLocation;
            set => SetProperty(ref _serverLocation, value);
        }



        private string _iwadFolder;
        public string IWADFolder
        {
            get => _iwadFolder;
            set
            {
                if (SetProperty(ref _iwadFolder, value))
                {
                    if (!string.IsNullOrWhiteSpace(_iwadFolder))
                        PopulateIWADs(_iwadFolder);
                    else
                    {
                        // Handle empty case too
                        IWADs.Clear();
                        SelectedIWAD = null;
                        IwadPreviewImage = null;
                    }

                    // Has the location been validated yet?
                    OnPropertyChanged(nameof(NeedsConfiguration));

                    // Only raise when something actually changed
                    OnPropertyChanged(nameof(HasPreviewImage));
                    OnPropertyChanged(nameof(HasNoIWADs));

                    // re-evaluates the glow state 
                    OnPropertyChanged(nameof(IsLaunchConfigReady));

                    SaveSettings();
                }
            }
        }
        public bool HasPreviewImage => IwadPreviewImage != null;

        public bool HasNoIWADs => IWADs.Count == 0;


        private string _updateStatus;
        public string UpdateStatus
        {
            get => _updateStatus;
            set => SetProperty(ref _updateStatus, value);
        }

        private int _updateProgress;
        public int UpdateProgress
        {
            get => _updateProgress;
            set => SetProperty(ref _updateProgress, value);
        }

        public bool AreFreeIwadsInstalled
        {
            get
            {
                // True if the ComboBox collection contains any selectable IWAD packages ---
                return IWADs != null && IWADs.Count > 0;
            }
        }

        public string FreeIwadButtonText
        {
            get
            {
                return AreFreeIwadsInstalled
                    ? "Detected IWAD Contents Already Installed!"
                    : "NO IWAD? ~ Download (Doom Shareware + Freedoom 1 & 2)";
            }
        }

        public bool ShouldFreeDownloadButtonGlow
        {
            get
            {
                return NeedsConfiguration && !AreFreeIwadsInstalled;
            }
        }



        private string _pwadLocation;
        public string PwadLocation
        {
            get => _pwadLocation;
            set
            {
                if (SetProperty(ref _pwadLocation, value))
                {
                    OnPropertyChanged(nameof(FinalLaunchCommand));
                    SaveSettings();
                }
            }
        }

        private string _clientParameters;
        public string ClientParameters
        {
            get => _clientParameters;
            set => SetProperty(ref _clientParameters, value);
        }

        private string _serverParameters;
        public string ServerParameters
        {
            get => _serverParameters;
            set => SetProperty(ref _serverParameters, value);
        }

        private string _serverCfg;
        public string ServerCfg
        {
            get => _serverCfg;
            set => SetProperty(ref _serverCfg, value);
        }

        private string _serverName;
        public string ServerName
        {
            get => _serverName;
            set
            {
                if (SetProperty(ref _serverName, value))
                {
                    if (!_isUpdating)
                        GenerateServerAndClientParameters();

                    SaveSettings(); 
                }
            }
        }
        private string _selectedIWAD;
        public string SelectedIWAD
        {
            get => _selectedIWAD;
            set
            {
                if (SetProperty(ref _selectedIWAD, value))
                {
                    if (!string.IsNullOrEmpty(_selectedIWAD))
                    {
                        _isUpdating = true;
                        PopulateMaps(_selectedIWAD);
                        GenerateServerAndClientParameters();
                        _isUpdating = false;
                    }

                    switch (value?.ToLower())
                    {
                        case "doom1.wad":
                            IwadPreviewImage = LoadImage("Assets/shareware_doom.bmp");
                            break;

                        case "doom.wad":
                            IwadPreviewImage = LoadImage("Assets/doom.png");
                            break;

                        case "doom2.wad":
                            IwadPreviewImage = LoadImage("Assets/doom2.png");
                            break;

                        case "freedoom1.wad":
                            IwadPreviewImage = LoadImage("Assets/freedoom1.bmp");
                            break;

                        case "freedoom2.wad":
                            IwadPreviewImage = LoadImage("Assets/freedoom2.bmp");
                            break;

                        case "plutonia.wad":
                            IwadPreviewImage = LoadImage("Assets/plutonia.png");
                            break;

                        case "tnt.wad":
                            IwadPreviewImage = LoadImage("Assets/tnt.png");
                            break;

                        case "chex.wad":
                            IwadPreviewImage = LoadImage("Assets/chex.png");
                            break;

                        case "hacx.wad":
                            IwadPreviewImage = LoadImage("Assets/hacx.png");
                            break;

                        case "hexen.wad":
                            IwadPreviewImage = LoadImage("Assets/hexen.png");
                            break;

                        case "heretic.wad":
                            IwadPreviewImage = LoadImage("Assets/heretic.png");
                            break;

                        default:
                            IwadPreviewImage = null;
                            break;

                            
                    }
                    SaveSettings();
                }
            }
        }
        private Bitmap LoadImage(string path)
        {
            try
            {
                var uri = new Uri($"avares://Simple_Doomsday_Engine_Launcher/{path}");
                return new Bitmap(AssetLoader.Open(uri));
            }
            catch
            {
                return null; // fail silently instead of crashing
            }
        }

        private Bitmap _iwadPreviewImage;
        public Bitmap IwadPreviewImage
        {
            get => _iwadPreviewImage;
            set => SetProperty(ref _iwadPreviewImage, value);
        }



        private string _selectedMap;
        public string SelectedMap
        {
            get => _selectedMap;
            set
            {
                if (SetProperty(ref _selectedMap, value))
                {
                    if (!_isUpdating)
                        GenerateServerAndClientParameters();
                    SaveSettings();
                }
            }
        }




        private bool _hostServer;
        public bool HostServer
        {
            get => _hostServer;
            set
            {
                if (SetProperty(ref _hostServer, value))
                {
                    if (!_isUpdating)
                        GenerateServerAndClientParameters();
                    SaveSettings();
                }
            }
        }

        private bool _publicServer;
        public bool PublicServer
        {
            get => _publicServer;
            set
            {
                if (SetProperty(ref _publicServer, value))
                {
                    if (!_isUpdating)
                        GenerateServerAndClientParameters();
                    SaveSettings();
                }
            }
        }

        private bool _noMonsters;
        public bool NoMonsters
        {
            get => _noMonsters;
            set
            {
                if (SetProperty(ref _noMonsters, value))
                {
                    if (!_isUpdating)
                        GenerateServerAndClientParameters();
                    SaveSettings();
                }
            }
        }

        private bool _enableReverb;
        public bool EnableReverb
        {
            get => _enableReverb;
            set
            {
                if (SetProperty(ref _enableReverb, value))
                {
                    if (!_isUpdating)
                        GenerateServerAndClientParameters();
                    SaveSettings();
                }
            }
        }

        private bool _disableCD;
        public bool DisableCD
        {
            get => _disableCD;
            set
            {
                if (SetProperty(ref _disableCD, value))
                {
                    if (!_isUpdating)
                        GenerateServerAndClientParameters();
                    SaveSettings();
                }
            }
        }

        private bool _enableJumping;
        public bool EnableJumping
        {
            get => _enableJumping;
            set
            {
                if (SetProperty(ref _enableJumping, value))
                {
                    if (!_isUpdating)
                        GenerateServerAndClientParameters();
                    SaveSettings();
                }
            }
        }

        private bool _disableMouseLook;
        public bool DisableMouseLook
        {
            get => _disableMouseLook;
            set
            {
                if (SetProperty(ref _disableMouseLook, value))
                {
                    if (!_isUpdating)
                        GenerateServerAndClientParameters();
                    SaveSettings();
                }
            }
        }


        private string _selectedResolution = "640x480";
        public string SelectedResolution
        {
            get => _selectedResolution;
            set
            {
                if (SetProperty(ref _selectedResolution, value))
                {
                    if (!_isUpdating)
                        GenerateServerAndClientParameters();

                    // update event
                    OnPropertyChanged(nameof(ShowScaleLabel));
                    SaveSettings();
                }
            }
        }

        private bool _checkedFullScreen;
        public bool CheckedFullScreen
        {
            get => _checkedFullScreen;
            set
            {
                if (SetProperty(ref _checkedFullScreen, value))
                {
                    if (!_isUpdating)
                        GenerateServerAndClientParameters();

                    // update event 
                    OnPropertyChanged(nameof(ShowScaleLabel));
                    SaveSettings();
                }
            }
        }


        public bool ShowScaleLabel
        {
            get
            {
                // return true when the 502x560 string is selected AND Fullscreen is NOT checked
                return SelectedResolution == "502x560" && !CheckedFullScreen;
            }
        }


        private string _selectedSkill = "3";
        public string SelectedSkill
        {
            get => _selectedSkill;
            set
            {
                if (SetProperty(ref _selectedSkill, value))
                {
                    if (!_isUpdating)
                        GenerateServerAndClientParameters();
                    SaveSettings();
                }
            }
        }


        private string _selectedGameType = "Co-op"; // or "Deathmatch"
        public string SelectedGameType
        {
            get => _selectedGameType;
            set
            {
                if (SetProperty(ref _selectedGameType, value))
                {
                    if (!_isUpdating)
                        GenerateServerAndClientParameters();
                    SaveSettings();
                }
            }
        }

        private string _selectedMaxPlayers = "4"; // 1-8
        public string SelectedMaxPlayers
        {
            get => _selectedMaxPlayers;
            set
            {
                if (SetProperty(ref _selectedMaxPlayers, value))
                {
                    if (!_isUpdating)
                        GenerateServerAndClientParameters();
                    SaveSettings();
                }
            }
        }

        // server name taken warning:
        private bool _nameIsTaken = false;
        public bool NameIsTaken
        {
            get => _nameIsTaken;
            set => SetProperty(ref _nameIsTaken, value);
        }


        // ================= COLLECTIONS =================

        public ObservableCollection<string> IWADs { get; } = new();
        public ObservableCollection<string> Maps { get; } = new();
        public ObservableCollection<DoomsdayServerInfo> Servers { get; } = new();



        public ObservableCollection<DoomsdayServerInfo> ServerList => Servers;

        // ================= SERVER BROWSER STATE =================

        private DoomsdayServerInfo _selectedServer;
        public DoomsdayServerInfo SelectedServer
        {
            get => _selectedServer;
            set => SetProperty(ref _selectedServer, value);
        }

        private string _manualServerAddress;
        public string ManualServerAddress
        {
            get => _manualServerAddress;
            set => SetProperty(ref _manualServerAddress, value);
        }

        private string _masterStatus;
        public string MasterStatus
        {
            get => _masterStatus;
            set => SetProperty(ref _masterStatus, value);
        }

        public class LauncherSettings
        {
            public string EngineLocation { get; set; }
            public string ServerLocation { get; set; }
            public string IWADFolder { get; set; }
            public string PwadLocation { get; set; }
            public string SelectedIWAD { get; set; }
            public string SelectedMap { get; set; }
            public string SelectedSkill { get; set; }
            public string SelectedGameType { get; set; }
            public string SelectedMaxPlayers { get; set; }
            public bool NoMonsters { get; set; }
            public bool DisableCD { get; set; }
            public bool EnableReverb { get; set; }
            public bool EnableJumping { get; set; }
            public bool DisableMouseLook { get; set; }
            public string SelectedResolution { get; set; }
            public bool CheckedFullScreen { get; set; }
            public bool HostServer { get; set; }
            public bool PublicServer { get; set; }
            public string ServerName { get; set; }
        }
        private void SaveSettings()
        {
            var settings = new LauncherSettings
            {
                EngineLocation = EngineLocation,
                ServerLocation = ServerLocation,
                IWADFolder = IWADFolder,
                PwadLocation = PwadLocation,
                SelectedIWAD = SelectedIWAD,
                SelectedMap = SelectedMap,
                SelectedSkill = SelectedSkill,
                SelectedGameType = SelectedGameType,
                SelectedMaxPlayers = SelectedMaxPlayers,
                NoMonsters = NoMonsters,
                DisableCD = DisableCD,
                EnableReverb = EnableReverb,
                EnableJumping = EnableJumping,
                DisableMouseLook = DisableMouseLook,
                SelectedResolution = SelectedResolution,
                CheckedFullScreen = CheckedFullScreen,
                HostServer = HostServer,
                PublicServer = PublicServer,
                ServerName = ServerName
            };

            File.WriteAllText(SettingsPath,
                System.Text.Json.JsonSerializer.Serialize(settings,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        private void LoadSettings()
        {
            if (!File.Exists(SettingsPath))
                return;

            try
            {
                var settings = System.Text.Json.JsonSerializer.Deserialize<LauncherSettings>(
                    File.ReadAllText(SettingsPath));

                if (settings == null)
                    return;

                // _isUpdating to prevent auto-overwriting
                _isUpdating = true;

                // Load paths first (these don't trigger cascading updates when _isUpdating = true)
                EngineLocation = settings.EngineLocation;
                ServerLocation = settings.ServerLocation;
                IWADFolder = settings.IWADFolder;
                PwadLocation = settings.PwadLocation;

                // Load game settings BEFORE setting SelectedIWAD
                SelectedSkill = settings.SelectedSkill ?? "3";
                SelectedGameType = settings.SelectedGameType ?? "Co-op";
                SelectedMaxPlayers = settings.SelectedMaxPlayers ?? "4";
                SelectedResolution = settings.SelectedResolution ?? "640x480";
               

                // Load checkboxes
                NoMonsters = settings.NoMonsters;
                DisableCD = settings.DisableCD;
                EnableReverb = settings.EnableReverb;
                EnableJumping = settings.EnableJumping;
                DisableMouseLook = settings.DisableMouseLook;
                CheckedFullScreen = settings.CheckedFullScreen;

                // Network settings
                HostServer = settings.HostServer;
                PublicServer = settings.PublicServer;
                ServerName = settings.ServerName ?? "Simple Doomsday Server";

                // Restore skill combobox selection
                SelectedSkillOption = SkillOptions.FirstOrDefault(x => x.Value == SelectedSkill);

                // Now load IWAD and Map - these need to happen LAST
                SelectedIWAD = settings.SelectedIWAD;
                SelectedMap = settings.SelectedMap;

                // Turn off update blocking
                _isUpdating = false;

                // Force preview update AFTER settings load
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!string.IsNullOrEmpty(SelectedIWAD))
                    {
                        var temp = SelectedIWAD;
                        _selectedIWAD = null;
                        SelectedIWAD = temp;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load settings: {ex.Message}");
                _isUpdating = false; // Make sure this gets reset even on error
            }
        }



        // ================= COMMANDS =================

        public ICommand LaunchCommand { get; }
        public ICommand RefreshServersCommand { get; }
        public ICommand ConnectManuallyCommand { get; }
        public ICommand ConnectSelectedServerCommand { get; }

        //   public ReactiveCommand<Unit, Unit> InstallDoomsdayCommand { get; }




        // ================= CONSTRUCTOR =================

        public MainViewModel()
        {
            
            InstallDoomsdayCommand = new RelayCommand(InstallDoomsday);

            DetectDoomsday();

            LoadSettings();

            // After settings load, EngineLocation is now known
            InstalledVersion = GetInstalledVersion();

            LaunchCommand = new AsyncRelayCommand(LaunchGameAsync);
            RefreshServersCommand = new RelayCommand(async () => await RefreshServers());
            ConnectManuallyCommand = new RelayCommand(ConnectManual);
            // 
            ConnectSelectedServerCommand = new AsyncRelayCommand(ConnectSelectedServer);

            BrowseEngineCommand = new AsyncRelayCommand(BrowseEngine);
            BrowseIwadFolderCommand = new AsyncRelayCommand(BrowseIwadFolder);
            BrowseServerCommand = new AsyncRelayCommand(BrowseServer);
            BrowsePwadCommand = new AsyncRelayCommand(BrowsePwad);
            ClearPwadCommand = new RelayCommand(ClearPwad);

            DownloadAllFreeContentCommand = new AsyncRelayCommand(DownloadAllFreeContentAsync);



            // Apply default server name if nothing was loaded out-of-the-box 
            if (string.IsNullOrEmpty(ServerName))
            {
                ServerName = "Simple Doomsday Server";
            }

            // Only set default if not loaded from settings
            if (string.IsNullOrEmpty(SelectedGameType))
            {
                SelectedGameType = "Co-op";
            }

            if (string.IsNullOrEmpty(SelectedMaxPlayers))
            {
                SelectedMaxPlayers = "4";
            }

            // apply defaults if nothing was loaded
            if (string.IsNullOrEmpty(SelectedSkill))
            {
                SelectedSkill = "3";
                SelectedSkillOption = SkillOptions.FirstOrDefault(x => x.Value == "3");
            }
            else
            {
                // Restore ComboBox selection from loaded value
                SelectedSkillOption = SkillOptions.FirstOrDefault(x => x.Value == SelectedSkill);
            }

            MasterStatus = "Status: Waiting...";

            // clean textboxes
            GenerateServerAndClientParameters();

            // force preview update AFTER ALL constructor logic
            if (!string.IsNullOrEmpty(SelectedIWAD))
            {
                var temp = SelectedIWAD;
                _selectedIWAD = null;
                SelectedIWAD = temp;
            }

            OnPropertyChanged(nameof(IsLaunchConfigReady));

            // Are the locations valid?
            OnPropertyChanged(nameof(NeedsConfiguration));

            // have IWADS been installed?
            OnPropertyChanged(nameof(AreFreeIwadsInstalled));
            OnPropertyChanged(nameof(FreeIwadButtonText));
            OnPropertyChanged(nameof(ShouldFreeDownloadButtonGlow));

        }


        private SkillOption _selectedSkillOption;
        public SkillOption SelectedSkillOption
        {
            get => _selectedSkillOption;
            set
            {
                if (SetProperty(ref _selectedSkillOption, value))
                {
                    if (value != null)
                        SelectedSkill = value.Value;
                    
                }
            }
        }


        // ================= TOPLEVEL HELPER =================

        private TopLevel GetTopLevel()
        {
            if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return TopLevel.GetTopLevel(desktop.MainWindow);

            return null;
        }

        // ================= BROWSE COMMANDS =================

        private async Task BrowseEngine()
        {
            var topLevel = GetTopLevel();
            if (topLevel == null)
                return;

            var file = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Doomsday Engine Executable",
                AllowMultiple = false,
                FileTypeFilter = new[]
                
                {
                    new FilePickerFileType("Executable")
                    {
                        Patterns = new[] { "Doomsday.exe" }
                    }
                }
            });

            if (file != null && file.Count > 0)
                EngineLocation = file[0].Path.LocalPath;
        }

        private void DetectDoomsday()
        {
            // Assume not installed until proven otherwise
            IsDoomsdayInstalled = false;

            // If EngineLocation is set and valid 
            if (!string.IsNullOrWhiteSpace(EngineLocation) && File.Exists(EngineLocation))
            {
                IsDoomsdayInstalled = true;
                return;
            }

            // Try known paths
            string[] possiblePaths =
            {
            @"C:\Program Files\Doomsday\doomsday.exe",
            @"C:\Program Files (x86)\Doomsday\doomsday.exe",
            Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Doomsday",
            "doomsday_2.3.2-build3869_x86",
            "bin",
            "doomsday.exe"),
            Path.Combine(AppContext.BaseDirectory, "Doomsday", "doomsday.exe"),
            Path.Combine(AppContext.BaseDirectory, "doomsday.exe"),
            Path.Combine(
                AppContext.BaseDirectory,
                "Engines",
                "Doomsday",
                "doomsday_2.3.2-build3869_x86",
                "bin",
                "doomsday.exe")

            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    EngineLocation = path;
                    IsDoomsdayInstalled = true;
                    return;
                }
            }

            // Nothing found, stays false
            IsDoomsdayInstalled = false;
        }



        private async Task BrowseIwadFolder()
        {
            var topLevel = GetTopLevel();
            if (topLevel == null)
                return;

            var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select IWAD Folder",
                AllowMultiple = false
            });

            if (folder != null && folder.Count > 0)
            {
                var localPath = folder[0].TryGetLocalPath();

                if (!string.IsNullOrWhiteSpace(localPath) && Directory.Exists(localPath))
                {
                    IWADFolder = CleanPath(localPath);
                }
                else
                {
                    // handle invalid folder case
                    IWADFolder = string.Empty;
                }
            }
        }
 

        private string CleanPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            return path.TrimEnd('\\', '/');
        }


        private async Task BrowseServer()
        {
            var topLevel = GetTopLevel();
            if (topLevel == null)
                return;

            var file = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Doomsday-Server Executable",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Doomsday Server")
                    {
                        Patterns = new[] { "doomsday-server.exe", "*.exe" }
                    }
                }
            });

            if (file != null && file.Count > 0)
                ServerLocation = file[0].Path.LocalPath;
        }

        private async Task BrowsePwad()
        {
            var topLevel = GetTopLevel();
            if (topLevel == null)
                return;

            var file = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select PWAD",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("PWAD Files")
                    {
                        Patterns = new[] { "*.wad" }
                    }
                }
            });

            if (file != null && file.Count > 0)
                PwadLocation = file[0].Path.LocalPath;
        }

        private void ClearPwad()
        {
            PwadLocation = string.Empty;
        }



        public ICommand DownloadAllFreeContentCommand { get; }




        // ================= IWAD / MAP LOGIC =================

        private void PopulateIWADs(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                return;

            // SAVE current selection BEFORE clearing
            string currentSelection = SelectedIWAD;

            IWADs.Clear();

            string[] allowed =
            {
                "doom.wad", "doom1.wad", "doom2.wad", "plutonia.wad", "tnt.wad",
                "chex.wad", "hacx.wad", "freedoom1.wad", "freedoom2.wad",
                "heretic1.wad", "heretic.wad", "hexen.wad"
            };

            foreach (var file in allowed)
            {
                string full = Path.Combine(folderPath, file);
                if (File.Exists(full))
                    IWADs.Add(file);
            }

            // nothing found clear everything
            if (IWADs.Count == 0)
            {
                SelectedIWAD = null;
                Maps.Clear();
                IwadPreviewImage = null;
                return;
            }

            // RESTORE previous selection if it's still in the list
            if (!string.IsNullOrEmpty(currentSelection) && IWADs.Contains(currentSelection))
            {
                // Re-trigger to update preview image
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var temp = currentSelection;
                    _selectedIWAD = null;
                    SelectedIWAD = temp;
                });
            }
            else
            {
                // Only set default if there was no previous selection or it's not available
                SelectedIWAD = IWADs[0];
            }
        }


        private void PopulateMaps(string iwad)
        {
            // SAVE current selection BEFORE clearing
            string currentSelection = SelectedMap;

            Maps.Clear();
            string lower = iwad.ToLower();

            if (lower == "doom.wad" || lower == "doom1.wad" || lower == "freedoom1.wad")
            {
                int episodes = lower.Contains("doom1") ? 1 : 4;
                for (int e = 1; e <= episodes; e++)
                    for (int m = 1; m <= 9; m++)
                        Maps.Add($"E{e}M{m}");
            }
            else if (lower == "heretic.wad" || lower == "heretic1.wad")
            {
                int episodes = lower.Contains("heretic1") ? 1 : 5;
                for (int e = 1; e <= episodes; e++)
                    for (int m = 1; m <= 9; m++)
                        Maps.Add($"E{e}M{m}");
            }
            else if (lower == "doom2.wad" || lower == "plutonia.wad" ||
                     lower == "tnt.wad" || lower == "freedoom2.wad" ||
                     lower == "hacx.wad")
            {
                for (int i = 1; i <= 32; i++)
                    Maps.Add($"MAP{i:D2}");
            }
            else if (lower == "hexen.wad")
            {
                for (int i = 1; i <= 31; i++)
                    Maps.Add($"MAP{i:D2}");
            }
            else if (lower == "chex.wad")
            {
                for (int i = 1; i <= 5; i++)
                    Maps.Add($"E1M{i}");
            }

            // RESTORE previous selection if it's valid for this IWAD
            if (!string.IsNullOrEmpty(currentSelection) && Maps.Contains(currentSelection))
            {
                SelectedMap = currentSelection;
            }
            else if (Maps.Count > 0)
            {
                // Only set default if there was no previous selection or it's not valid
                SelectedMap = Maps[0];
            }
        }

        private string GetGameId(string iwad)
        {
            return iwad.ToLower() switch
            {
                "doom.wad" => "doom1-ultimate",
                "doom1.wad" => "doom1-share",
                "doom2.wad" => "doom2",
                "plutonia.wad" => "doom2-plut",
                "tnt.wad" => "doom2-tnt",
                "chex.wad" => "chex",
                "hacx.wad" => "hacx",
                "freedoom1.wad" => "doom1-freedoom",
                "freedoom2.wad" => "doom2-freedoom",
                "heretic1.wad" => "heretic-share",
                "heretic.wad" => "heretic-ext",
                "hexen.wad" => "hexen",
                _ => "doom2"
            };
        }

        private Bitmap GetGameImage(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId))
                return null;

            string imagePath = gameId.ToLower() switch
            {
                "doom1-share" => "Assets/shareware_doom.bmp",
                "doom1-ultimate" => "Assets/doom.png",
                "doom2" => "Assets/doom2.png",
                "doom2-plut" => "Assets/plutonia.png",
                "doom2-tnt" => "Assets/tnt.png",
                "doom1-freedoom" => "Assets/freedoom1.bmp",
                "doom2-freedoom" => "Assets/freedoom2.bmp",
                "chex" => "Assets/chex.png",
                "hacx" => "Assets/hacx.png",
                "hexen" => "Assets/hexen.png",
                "heretic-share" => "Assets/heretic.png",
                "heretic-ext" => "Assets/heretic.png",
                _ => null
            };

            if (imagePath == null)
                return null;

            return LoadImage(imagePath);
        }




        private string GetLocalIPAddress()
        {
            try
            {
                // Loop through all network interfaces on your computer
                foreach (var netInterface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    // Only look at active Ethernet or Wi-Fi adapters
                    if (netInterface.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                        (netInterface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 ||
                         netInterface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet))
                    {
                        // Look through the unicast IP addresses assigned to this card
                        foreach (var ip in netInterface.GetIPProperties().UnicastAddresses)
                        {
                            // Ensure it is an IPv4 address and not a local loopback (127.0.0.1)
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                                !IPAddress.IsLoopback(ip.Address))
                            {
                                return ip.Address.ToString();
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback to localhost if network discovery completely fails
            }

            return "127.0.0.1";
        }




        // ================= SERVER CFG / PARAM LOGIC =================

        private void GenerateServerAndClientParameters()
        {
            if (string.IsNullOrEmpty(SelectedIWAD) || string.IsNullOrEmpty(SelectedMap) || string.IsNullOrEmpty(IWADFolder))
                return;

            string lowerIWAD = SelectedIWAD.ToLower();
            string gameID = GetGameId(SelectedIWAD);

            string currentMap = SelectedMap ?? "MAP01";
            string cfgEpisode = "1";
            string cfgMap = currentMap.ToLower();

            string publicValue = PublicServer ? "1" : "0";
            string dmValue = SelectedGameType == "Deathmatch" ? "1" : "0";
            string nomonstersValue = NoMonsters ? "1" : "0";
            string skillValue = SelectedSkill ?? "3";
            string passwordValue = "\"doom\"";
            string playerlimit = SelectedMaxPlayers ?? "4";
           
            ServerCfg =
                "iwad " + SelectedIWAD + Environment.NewLine +
                "net-ip-port 13209" + Environment.NewLine +
                "server-public " + publicValue + Environment.NewLine +
                "server-password " + passwordValue + Environment.NewLine +
                "server-name \"" + (ServerName ?? "") + "\"" + Environment.NewLine +
                "server-player-limit " + playerlimit + Environment.NewLine +
                "server-game-episode " + cfgEpisode + Environment.NewLine +
                "server-game-map " + cfgMap + Environment.NewLine +
                "server-game-deathmatch " + dmValue + Environment.NewLine +
                "server-game-nomonsters " + nomonstersValue + Environment.NewLine +
                "server-game-skill " + skillValue;

            string localIP = GetLocalIPAddress();

            if (HostServer)
            {
                ClientParameters =
                    $"-iwad \"{CleanPath(IWADFolder)}\" -game {gameID} -connect {localIP}";
            }
            else
            {
                // DO NOT include warp or skill here.
                // Those are added later in LaunchGame().
                ClientParameters =
                    $"-iwad \"{CleanPath(IWADFolder)}\" -game {gameID}";
            }

            ServerParameters =
                $"-game {gameID} -iwad \"{CleanPath(IWADFolder)}\" -p server.cfg";

            OnPropertyChanged(nameof(FinalLaunchCommand));
        }

        public class SkillOption
        {
            public string Display { get; set; }
            public string Value { get; set; }
        }

        public ObservableCollection<SkillOption> SkillOptions { get; } = new()
        {
            new SkillOption { Display = "Skill 1", Value = "1" },
            new SkillOption { Display = "Skill 2", Value = "2" },
            new SkillOption { Display = "Skill 3", Value = "3" },
            new SkillOption { Display = "Skill 4", Value = "4" },
            new SkillOption { Display = "Skill 5", Value = "5" }
        };

        // ================= LAUNCH LOGIC =================

        private async Task LaunchGameAsync()
        {
            NameIsTaken = false;

            if (HostServer)
            {
                MasterStatus = "Checking server name availability...";
                bool isTaken = await IsServerNameTakenAsync(this.ServerName);

                if (isTaken)
                {
                    NameIsTaken = true;
                    MasterStatus = "Launch aborted: Server name already in use.";
                    return;
                }
            }

            // flip the state flag on the UI thread to lock down the button 
            IsGameRunning = true;

            string currentServerName = this.ServerName;
            string currentIwad = !string.IsNullOrEmpty(SelectedIWAD) ? SelectedIWAD : "doom2.wad";
            string currentPwad = !string.IsNullOrEmpty(PwadLocation) ? Path.GetFileName(PwadLocation) : "";
            string resolvedGameId = GetGameId(currentIwad);
            string localIP = GetLocalIPAddress();

            // Clean out forbidden characters from the raw string FIRST
            string safeRawName = (currentServerName ?? "Host")
                .Replace(".", "")
                .Replace("$", "")
                .Replace("#", "")
                .Replace("[", "")
                .Replace("]", "");

            // Apply URL escaping to clean up spaces 
            string cleanStorageName = Uri.EscapeDataString(safeRawName);

            string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);

            // Maps the storage path slot to prevent overwrites
            _activeDatabaseKey = $"{cleanStorageName}_{uniqueId}";

            await Task.Run(() =>
            {
                LaunchGameInternal();
            });

            if (HostServer)
            {
                // display 'currentServerName' 
                await PublishLanServerToWebDatabaseDirect(currentServerName, localIP, resolvedGameId, currentPwad, _activeDatabaseKey);

                lock (_heartbeatLock)
                {
                    _heartbeatTimer?.Dispose();
                    _heartbeatTimer = new Timer(async _ =>
                    {
                        if (_currentServerProcess != null && !_currentServerProcess.HasExited)
                        {
                            await PublishLanServerToWebDatabaseDirect(currentServerName, localIP, resolvedGameId, currentPwad, _activeDatabaseKey);
                        }
                    }, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
                }
            }
        }



        private void LaunchGameInternal()
        {
            SaveSettings();

            if (string.IsNullOrEmpty(ServerLocation) ||
                string.IsNullOrEmpty(EngineLocation) ||
                string.IsNullOrEmpty(IWADFolder))
            {
                // Unlock UI if validation drops before launching
                Avalonia.Threading.Dispatcher.UIThread.Post(() => IsGameRunning = false);
                return;
            }

            if (!File.Exists(ServerLocation) ||
                !File.Exists(EngineLocation) ||
                !Directory.Exists(IWADFolder))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => IsGameRunning = false);
                return;
            }

            if (!string.IsNullOrEmpty(PwadLocation) && !File.Exists(PwadLocation))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => IsGameRunning = false);
                return;
            }

            string pwadArg = !string.IsNullOrEmpty(PwadLocation)
                ? $" -file \"{PwadLocation}\""
                : "";

            string cdArg = DisableCD
                ? " -icd dummy -command \"con_alert_level 0\""
                : "";

            string reverbArg = EnableReverb
                ? " -command \"sound-3d 1\""
                : " -command \"sound-3d 0\"";

            string jumpValue = EnableJumping ? "1" : "0";
            string jumpArg = $" -command \"player-jump {jumpValue}\"";

            string mouseLookArg = DisableMouseLook
                ? " -command \"input-mouse-y-flags 1\""
                : " -command \"input-mouse-y-flags 0\"";

            string screenModeModeFlag = CheckedFullScreen ? "-fullscreen" : "-wnd -center";
            string resArg = $" {screenModeModeFlag} -wh {SelectedResolution.Replace("x", " ")}";

            if (HostServer)
            {
                if (!_isUpdating)
                    GenerateServerAndClientParameters();

                Environment.SetEnvironmentVariable("DOOMWADDIR", IWADFolder, EnvironmentVariableTarget.Process);

                try
                {
                    if (!string.IsNullOrEmpty(EngineLocation))
                    {
                        string engineFolder = Path.GetDirectoryName(EngineLocation);
                        string configPath = Path.Combine(engineFolder ?? "", "server.cfg");
                        File.WriteAllText(configPath, ServerCfg ?? "");
                    }
                }
                catch
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => IsGameRunning = false);
                    return;
                }

                string serverDir = Path.GetDirectoryName(ServerLocation) ?? "";
                string serverCfgPath = Path.Combine(serverDir, "server.cfg");

                try
                {
                    File.WriteAllText(serverCfgPath, ServerCfg ?? "");
                }
                catch
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => IsGameRunning = false);
                    return;
                }

                // Uses GetGameId strings to ensure server parameters match 
                string cleanIwadFilename = !string.IsNullOrEmpty(SelectedIWAD) ? SelectedIWAD : "doom2.wad";
                string shortEngineGameId = GetGameId(cleanIwadFilename);

                string serverParameters = $"-game {shortEngineGameId} -iwad \"{CleanPath(IWADFolder)}\" -p server.cfg" + pwadArg + jumpArg;

                var serverStart = new ProcessStartInfo
                {
                    FileName = ServerLocation,
                    Arguments = serverParameters,
                    WorkingDirectory = serverDir,
                    WindowStyle = ProcessWindowStyle.Minimized
                };
                Debug.WriteLine("Launching Server with arguments: " + serverParameters);

                _currentServerProcess = Process.Start(serverStart);

                // Monitor the server exit handler regardless of Public/LAN state
                if (_currentServerProcess != null)
                {
                    _currentServerProcess.EnableRaisingEvents = true;
                    _currentServerProcess.Exited += ServerProcess_Exited;
                }

                // Sleep briefly to allow the server to initialize before the client attempts to connect
                Thread.Sleep(1000);

                string clientArgs = (ClientParameters ?? "") + pwadArg + cdArg + resArg + reverbArg + jumpArg + mouseLookArg;
                Debug.WriteLine("Launching Client with arguments: " + clientArgs);

                var engineStart = new ProcessStartInfo
                {
                    FileName = EngineLocation,
                    Arguments = clientArgs,
                    WorkingDirectory = Path.GetDirectoryName(EngineLocation) ?? ""
                };

                var engineProc = new Process
                {
                    StartInfo = engineStart,
                    EnableRaisingEvents = true
                };

                engineProc.Exited += (s, ev) =>
                {
                    // Always remove the database record when the client closes down
                    _ = UnpublishLanServerFromWebDatabase();

                    if (_currentServerProcess != null && !_currentServerProcess.HasExited)
                    {
                        try
                        {
                            _currentServerProcess.Kill();
                            _currentServerProcess.Dispose();
                        }
                        catch { }
                        _currentServerProcess = null;
                    }

                    //  Unlock the UI thread safely once the multiplayer engine closes 
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        IsGameRunning = false;
                        MasterStatus = "Returned from multiplayer session.";
                    });

                    engineProc.Dispose();
                };

                engineProc.Start();
            }
            else
            {
                string baseArgs = (ClientParameters ?? "").Split(new[] { "-connect" }, StringSplitOptions.None)[0].Trim();

                string currentMap = SelectedMap ?? "MAP01";
                string warpArg;

                if (currentMap.StartsWith("E", StringComparison.OrdinalIgnoreCase))
                {
                    string ep = currentMap.Substring(1, 1);
                    string mp = currentMap.Substring(3);
                    warpArg = $"-warp {ep} {mp}";
                }
                else
                {
                    string mapNum = currentMap.Replace("MAP", "", StringComparison.OrdinalIgnoreCase);
                    warpArg = $"-warp {mapNum}";
                }

                string skillValue = SelectedSkill ?? "3";
                string skillArg = $" -skill {skillValue}";

                string singlePlayerArgs =
                    $"{baseArgs} {warpArg}{skillArg}{pwadArg}{cdArg}{resArg}{reverbArg}{jumpArg}{mouseLookArg}";

                Debug.WriteLine("Launching with args: " + singlePlayerArgs);

                var engineStart = new ProcessStartInfo
                {
                    FileName = EngineLocation,
                    Arguments = singlePlayerArgs,
                    WorkingDirectory = Path.GetDirectoryName(EngineLocation) ?? ""
                };

              
                var engineProc = new Process
                {
                    StartInfo = engineStart,
                    EnableRaisingEvents = true
                };

                engineProc.Exited += (s, ev) =>
                {
                    
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        IsGameRunning = false;
                        MasterStatus = "Returned from single player session.";
                    });

                    engineProc.Dispose();
                };

                engineProc.Start();
            }
        }

        // Note*  This launcher uses Firebase as its database!
        // The primary URL has been removed in code.
        // You must supply your own!
        // eg: where you see "your-firebase-url" in the database link, replace it with your own.
        //...


        private async Task<bool> IsServerNameTakenAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            try
            {
                using (var client = new HttpClient())
                {
                    // Pull the parent bucket containing all active user sessions
                    string dbUrl = "https://your-firebase-url.firebaseio.com/doomsday_lan.json";

                    var response = await client.GetAsync(dbUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResult = await response.Content.ReadAsStringAsync();

                        if (!string.IsNullOrWhiteSpace(jsonResult) && jsonResult.Trim() != "null")
                        {
                            // Parse the folder directory tree map
                            var webServers = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(jsonResult);

                            if (webServers != null)
                            {
                                long maxAgeTicks = TimeSpan.FromMinutes(5).Ticks;
                                long nowTicks = DateTime.UtcNow.Ticks;

                                foreach (var kvp in webServers)
                                {
                                    dynamic lanData = kvp.Value;
                                    if (lanData == null || lanData.ServerName == null) continue;

                                    // Skip expired or ghost allocations so dead servers don't permanently block names
                                    long timestamp = lanData.Timestamp ?? 0;
                                    if (nowTicks - timestamp > maxAgeTicks) continue;

                                    // Scan the inner child object parameters
                                    string activeRoomName = (string)lanData.ServerName;
                                    if (activeRoomName.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
                                    {
                                        return true; // Found an exact matching active name string!
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback to false if database errors out
            }
            return false;
        }


        private void ServerProcess_Exited(object? sender, EventArgs e)
        {
            if (sender is Process proc)
            {
                proc.Exited -= ServerProcess_Exited;
            }

            _currentServerProcess = null;

            // removal fallback if server is shut down out-of-order 
            _ = UnpublishLanServerFromWebDatabase();
        }


        public string FinalLaunchCommand
        {
            get
            {
                if (string.IsNullOrEmpty(EngineLocation) || string.IsNullOrEmpty(IWADFolder))
                    return "";

                string pwadArg = !string.IsNullOrEmpty(PwadLocation)
                    ? $" -file \"{PwadLocation}\""
                    : "";

                string cdArg = DisableCD
                    ? " -icd dummy -command \"con_alert_level 0\""
                    : "";

                string reverbArg = EnableReverb
                    ? " -command \"sound-3d 1\""
                    : " -command \"sound-3d 0\"";

                string jumpValue = EnableJumping ? "1" : "0";
                string jumpArg = $" -command \"player-jump {jumpValue}\"";

                string mouseLookArg = DisableMouseLook
                    ? " -command \"input-mouse-y-flags 1\""
                    : " -command \"input-mouse-y-flags 0\"";

                string screenModeModeFlag = CheckedFullScreen ? "-fullscreen" : "-wnd -center";
                string resArg = $" {screenModeModeFlag} -wh {SelectedResolution.Replace("x", " ")}";


                if (HostServer)
                {
                    string serverParams = (ServerParameters ?? "") + pwadArg + jumpArg;
                    string clientParams = (ClientParameters ?? "") + pwadArg + cdArg + resArg + reverbArg + jumpArg + mouseLookArg;

                    return
                        $"SERVER:\n{ServerLocation} {serverParams}\n\n" +
                        $"CLIENT:\n{EngineLocation} {clientParams}";
                }
                else
                {
                    string baseArgs = (ClientParameters ?? "")
                        .Split(new[] { "-connect" }, StringSplitOptions.None)[0]
                        .Trim();

                    string currentMap = SelectedMap ?? "MAP01";
                    string warpArg;

                    if (currentMap.StartsWith("E", StringComparison.OrdinalIgnoreCase))
                    {
                        string ep = currentMap.Substring(1, 1);
                        string mp = currentMap.Substring(3);
                        warpArg = $"-warp {ep} {mp}";
                    }
                    else
                    {
                        string mapNum = currentMap.Replace("MAP", "", StringComparison.OrdinalIgnoreCase);
                        warpArg = $"-warp {mapNum}";
                    }

                    string skillArg = $" -skill {SelectedSkill ?? "3"}";

                    string finalArgs =
                        $"{baseArgs} {warpArg}{skillArg}{pwadArg}{cdArg}{resArg}{reverbArg}{jumpArg}{mouseLookArg}";

                    return $"{EngineLocation} {finalArgs}";
                }
            }
        }

       
        private async Task PublishLanServerToWebDatabaseDirect(string serverName, string localIP, string gameId, string pwadName, string sessionKey)
        {
            if (string.IsNullOrEmpty(serverName)) return;

            try
            {
                using (var client = new HttpClient())
                {
                    var serverData = new
                    {
                        ServerName = serverName, 
                        IP = localIP,
                        Port = 13209,
                        Game = gameId,
                        Addons = pwadName,
                        Timestamp = DateTime.UtcNow.Ticks
                    };

                    string json = JsonConvert.SerializeObject(serverData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    string dbUrl = $"https://your-firebase-url.firebaseio.com/doomsday_lan/{sessionKey}.json";

                    await client.PutAsync(dbUrl, content);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Firebase Upload Failed] {ex.Message}");
            }
        }


        // ================= SERVER BROWSER =================

        private async Task RefreshServers()
        {
            MasterStatus = "Refreshing servers...";
            Servers.Clear();

            // Fetch whatever is on the public master list
            var list = await DoomsdayMaster.GetServersAsync();
            var localList = list.ToList();

            // Fetch local LAN servers from your Firebase doomsday_lan
            try
            {
                using (var client = new HttpClient())
                {
                    string dbUrl = "https://your-firebase-url.firebaseio.com/doomsday_lan.json";
                    var response = await client.GetAsync(dbUrl);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResult = await response.Content.ReadAsStringAsync();

                        // If database node is empty, it returns the string "null"
                        if (!string.IsNullOrWhiteSpace(jsonResult) && jsonResult.Trim() != "null")
                        {
                            var webServers = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(jsonResult);

                            if (webServers != null)
                            {
                                long maxAgeTicks = TimeSpan.FromMinutes(5).Ticks; // Clear rooms older than 5 mins
                                long nowTicks = DateTime.UtcNow.Ticks;

                                foreach (var kvp in webServers)
                                {
                                    dynamic lanData = kvp.Value;
                                    if (lanData == null || lanData.IP == null || lanData.Port == null) continue;

                                    long timestamp = lanData.Timestamp;
                                    if (nowTicks - timestamp > maxAgeTicks) continue; // Skip expired allocations

                                    string ip = lanData.IP;
                                    string portStr = lanData.Port.ToString();
                                    _ = int.TryParse(portStr, out int portInt);
                                    string incomingServerName = (string)lanData.ServerName;

                                   
                                    var existingPublicServer = localList.FirstOrDefault(s =>
                                        (!string.IsNullOrEmpty(s.ServerName) && s.ServerName.Equals(incomingServerName, StringComparison.OrdinalIgnoreCase) && s.IP == ip) ||
                                        (s.IP == ip && s.Port.ToString() == portStr));

                                    if (existingPublicServer != null)
                                    {
                                        // Inject the PWAD filename directly into the matching public listing row
                                        var addonsProp = existingPublicServer.GetType().GetProperty("Addons");
                                        if (addonsProp != null && lanData.Addons != null)
                                        {
                                            addonsProp.SetValue(existingPublicServer, (string)lanData.Addons);
                                        }
                                    }
                                    else
                                    {
                                      
                                        // Verify if this specific machine endpoint (IP + Port) is already tracked.
                                        // This lets "My Server" from 192.168.1.50 and "My Server" from 192.168.1.60 co-exist
                                        if (!localList.Any(s => s.IP == ip && s.Port.ToString() == portStr))
                                        {
                                            var genericTypes = list.GetType().GetGenericArguments();
                                            if (genericTypes.Length > 0)
                                            {
                                                var lanServer = Activator.CreateInstance(genericTypes[0]);
                                                if (lanServer != null)
                                                {
                                                    var ipProp = lanServer.GetType().GetProperty("IP");
                                                    var portProp = lanServer.GetType().GetProperty("Port");
                                                    var nameProp = lanServer.GetType().GetProperty("ServerName");
                                                    var gameProp = lanServer.GetType().GetProperty("Game");
                                                    var pingProp = lanServer.GetType().GetProperty("Ping");
                                                    var addonsProp = lanServer.GetType().GetProperty("Addons");

                                                    ipProp?.SetValue(lanServer, ip);

                                                    if (portProp != null)
                                                    {
                                                        if (portProp.PropertyType == typeof(int))
                                                            portProp.SetValue(lanServer, portInt);
                                                        else
                                                            portProp.SetValue(lanServer, portStr);
                                                    }

                                                    nameProp?.SetValue(lanServer, incomingServerName);
                                                    gameProp?.SetValue(lanServer, (string)lanData.Game);
                                                    pingProp?.SetValue(lanServer, "LAN");

                                                    if (addonsProp != null && lanData.Addons != null)
                                                    {
                                                        addonsProp.SetValue(lanServer, (string)lanData.Addons);
                                                    }

                                                    dynamic stronglyTypedServer = lanServer;
                                                    stronglyTypedServer.GameImage = GetGameImage(stronglyTypedServer.Game);

                                                    localList.Add(stronglyTypedServer);
                                                }
                                            }
                                        }
                                    }
                                }

                            }
                    
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Firebase Read Failed] {ex.Message}");
            }

            // Sort and render combined list to UI
            var sorted = localList.OrderBy(s =>
            {
                // If the ping is explicitly flagged as "LAN", sort it first
                if (s.Ping == "LAN") return -1;

                // Otherwise, parse the real numerical ping value normally
                if (int.TryParse(s.Ping, out int p)) return p;

                // Dead or untracked servers go to the absolute bottom
                return 9999;
            });

            foreach (var s in sorted)
            {
                try
                {
                    dynamic dynamicServer = s;

                    // Read the string explicitly before passing
                    string gameIdentifier = dynamicServer.Game?.ToString() ?? "doom2";

                    // Set the image 
                    dynamicServer.GameImage = GetGameImage(gameIdentifier);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Image Binding Error] {ex.Message}");
                }

                Servers.Add(s);
            }


            MasterStatus = $"Found {Servers.Count} servers";
        }


        // auto download pwads
        // lan discovery
        // $"https://your-firebase-url.firebaseio.com/doomsday_lan/{safeKey}.json
        private async Task PublishLanServerToWebDatabase()
        {
            if (string.IsNullOrEmpty(ServerName)) return;

            try
            {
                using (var client = new HttpClient())
                {
                    string addonFile = !string.IsNullOrEmpty(PwadLocation)
                        ? Path.GetFileName(PwadLocation)
                        : "";

                    string currentIwad = !string.IsNullOrEmpty(SelectedIWAD) ? SelectedIWAD : "doom2.wad";
                    string resolvedGameId = GetGameId(currentIwad); //  outputs "doom1-ultimate" or "doom2-plut" etc..

                    var serverData = new
                    {
                        ServerName = this.ServerName,
                        IP = GetLocalIPAddress(),
                        Port = 13209,
                        Game = resolvedGameId,
                        Addons = addonFile,
                        Timestamp = DateTime.UtcNow.Ticks
                    };

                    string json = JsonConvert.SerializeObject(serverData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    string safeKey = Uri.EscapeDataString(ServerName);
                    string dbUrl = $"https://your-firebase-url.firebaseio.com/doomsday_lan/{safeKey}.json";

                    await client.PutAsync(dbUrl, content);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Firebase Upload Failed] {ex.Message}");
            }
        }


    
        private async Task UnpublishLanServerFromWebDatabase()
        {
            lock (_heartbeatLock)
            {
                _heartbeatTimer?.Dispose();
                _heartbeatTimer = null;
            }

            // If no active database session key exists, abort
            if (string.IsNullOrEmpty(_activeDatabaseKey)) return;

            try
            {
                using (var client = new HttpClient())
                {
                    // Target session key 
                    string dbUrl = $"https://your-firebase-url.firebaseio.com/doomsday_lan/{_activeDatabaseKey}.json";
                    await client.DeleteAsync(dbUrl);

                    _activeDatabaseKey = ""; // Reset
                }
            }
            catch { /* fail silently on exit */ }
        }




        public async Task ConnectSelectedServer()
        {
            // prevents command button execution if busy
            if (SelectedServer == null || IsGameRunning)
                return;

            string gameId = GetGameIdFromServer(SelectedServer);

            // reflect Addons property
            var addonsProperty = SelectedServer.GetType().GetProperty("Addons");
            string hostedPwad = addonsProperty?.GetValue(SelectedServer)?.ToString() ?? "";

            // clean up 
            hostedPwad = hostedPwad.Trim();

            // filter out public master server list 
            bool isRealPwad = !string.IsNullOrWhiteSpace(hostedPwad) &&
                              hostedPwad != "null" &&
                              hostedPwad != "-" &&
                              hostedPwad != "--" &&
                              !hostedPwad.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                              !hostedPwad.Equals("empty", StringComparison.OrdinalIgnoreCase);

            if (isRealPwad && hostedPwad.Length > 0)
            {
                string pwadTargetFolder = Path.Combine(AppContext.BaseDirectory, "Downloads");
                string localPwadPath = Path.Combine(pwadTargetFolder, hostedPwad);

                // protection against illegal symbols
                if (hostedPwad == "/" || hostedPwad == "\\")
                {
                    PwadLocation = "";
                }
                else if (!File.Exists(localPwadPath))
                {
                    var downloader = new Simple_Doomsday_Engine_Launcher.Models.WadDownloader();

                    downloader.StatusChanged += (statusMessage) => { MasterStatus = statusMessage; };
                    downloader.ProgressChanged += (fileName, bytesRead, totalBytes) =>
                    {
                        double mbRead = (double)bytesRead / 1024 / 1024;
                        if (totalBytes > 0)
                        {
                            double mbTotal = (double)totalBytes / 1024 / 1024;
                            MasterStatus = $"Downloading {fileName}: {mbRead:F2} MB / {mbTotal:F2} MB";
                        }
                        else
                        {
                            MasterStatus = $"Downloading {fileName}: {mbRead:F2} MB (Streaming...)";
                        }
                    };

                    bool success = await downloader.DownloadWadAsync(hostedPwad, pwadTargetFolder, expectedHash: "");

                    if (!success)
                    {
                        MasterStatus = $"Failed to download required addon: {hostedPwad}. Aborting connection.";
                        return;
                    }

                    PwadLocation = localPwadPath;
                }
                else
                {
                    PwadLocation = localPwadPath;
                }
            }
            else
            {
               
                PwadLocation = "";
            }

            
            IsGameRunning = true;

            MasterStatus = $"Connecting to {SelectedServer.ServerName}...";
            LaunchWithAddress($"{SelectedServer.IP}:{SelectedServer.Port}", gameId);

            // timer to revert status message
            _ = Task.Run(async () =>
            {
                await Task.Delay(4000); 

                // Revert back on the UI thread
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // overwrite if a new network status or crash didn't modify it first
                    if (MasterStatus == $"Connecting to {SelectedServer.ServerName}...")
                    {
                        MasterStatus = "Game active. Waiting for exit...";
                    }
                });
            });
        }




        private void ConnectManual()
        {
            if (string.IsNullOrWhiteSpace(ManualServerAddress))
                return;

            string serverGame = GetGameId(SelectedIWAD);
            LaunchWithAddress(ManualServerAddress, serverGame);
        }

        private CancellationTokenSource? _lanBroadcastCts;

        private void StartLanBroadcast()
        {
            _lanBroadcastCts = new CancellationTokenSource();
            var token = _lanBroadcastCts.Token;

            Task.Run(async () =>
            {
                using var udpClient = new UdpClient();
                udpClient.EnableBroadcast = true;

                
                var serverInfo = new
                {
                    ServerName = this.ServerName ?? "Local LAN Game",
                    IP = GetLocalIPAddress(),
                    Port = "13209",
                    Game = GetGameId(SelectedIWAD ?? "doom2.wad"),
                    Ping = "1"
                };

                string jsonPayload = JsonConvert.SerializeObject(serverInfo);
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(jsonPayload);

                IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, 13210);

                while (!token.IsCancellationRequested)
                {
                    await udpClient.SendAsync(bytes, bytes.Length, endPoint);
                    await Task.Delay(2000, token);
                }
            }, token);
        }


        private void StopLanBroadcast()
        {
            _lanBroadcastCts?.Cancel();
        }


        private bool IsLocalServer(string serverIp)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(serverIp))
                    return false;

                // Localhost aliases
                if (serverIp == "127.0.0.1" ||
                    serverIp.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                    return true;

                // Get all local machine IPs
                var host = Dns.GetHostEntry(Dns.GetHostName());

                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        if (ip.ToString() == serverIp)
                            return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
        private void LaunchWithAddress(string address, string gameId)
        {
            if (string.IsNullOrEmpty(EngineLocation) || string.IsNullOrEmpty(IWADFolder))
            {
                // reset the state flag if paths are invalid
                Avalonia.Threading.Dispatcher.UIThread.Post(() => IsGameRunning = false);
                return;
            }

            // Build addon args from server browser Addons column
            string addonArgs = "";

            if (SelectedServer != null &&
                !string.IsNullOrWhiteSpace(SelectedServer.Addons) &&
                SelectedServer.Addons != "--")
            {
                var addons = SelectedServer.Addons
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim());

                foreach (var addon in addons)
                {
                    string pwadFolder = "";

                    if (!string.IsNullOrWhiteSpace(PwadLocation))
                    {
                        pwadFolder = Path.GetDirectoryName(PwadLocation);
                    }

                    if (!string.IsNullOrWhiteSpace(pwadFolder))
                    {
                        string fullPath = Path.Combine(pwadFolder, addon);
                        addonArgs += $" -file \"{fullPath}\"";
                    }
                }
            }

            string cdArg = DisableCD
                ? " -icd dummy -command \"con_alert_level 0\""
                : "";

            string reverbArg = EnableReverb
                ? " -command \"sound-3d 1\""
                : " -command \"sound-3d 0\"";

            string jumpValue = EnableJumping ? "1" : "0";
            string jumpArg = $" -command \"player-jump {jumpValue}\"";

            string mouseLookArg = DisableMouseLook
                ? " -command \"input-mouse-y-flags 1\""
                : " -command \"input-mouse-y-flags 0\"";

            string screenModeModeFlag = CheckedFullScreen ? "-fullscreen" : "-wnd -center";
            string resArg = $" {screenModeModeFlag} -wh {SelectedResolution.Replace("x", " ")}";

            string connectAddress = address;

            // If we're already hosting locally, use localhost for joining
            if (HostServer && SelectedServer != null)
            {
                connectAddress = "localhost:" + SelectedServer.Port;
            }

            string joinArgs = $"-iwad \"{CleanPath(IWADFolder)}\" -game {gameId} {addonArgs}{cdArg}{resArg}{reverbArg}{jumpArg}{mouseLookArg} -connect {connectAddress}";

            ClientParameters = joinArgs;
            Debug.WriteLine("Joining server with the parameters: " + joinArgs);

            try
            {
                
                var engineStart = new ProcessStartInfo
                {
                    FileName = EngineLocation,
                    Arguments = joinArgs,
                    WorkingDirectory = Path.GetDirectoryName(EngineLocation) ?? ""
                };

                var engineProc = new Process
                {
                    StartInfo = engineStart,
                    EnableRaisingEvents = true 
                };

                engineProc.Exited += (s, ev) =>
                {
                   
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        IsGameRunning = false;
                        MasterStatus = "Returned from game server.";
                    });

                    engineProc.Dispose();
                };

                engineProc.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Process Execution Failure] {ex.Message}");
                // avoid deadlocking the UI 
                Avalonia.Threading.Dispatcher.UIThread.Post(() => IsGameRunning = false);
            }

            HostServer = false;
        }


        private string GetGameIdFromServer(DoomsdayServerInfo server)
        {
            if (server == null)
                return "doom2";

            var game = server.Game?.ToLower();

            if (string.IsNullOrWhiteSpace(game))
                return "doom2";

            // If the server already reports a proper game id (doom-ultimate, doom2-plut, etc.)
            if (!game.EndsWith(".wad"))
                return game;

            return GetGameId(game);
        }


        private string ConvertServerGameToIwad(string game)
        {
            if (string.IsNullOrWhiteSpace(game))
                return "doom2.wad";

            game = game.ToLower();

            return game switch
            {
                "doom2" => "doom2.wad",
                "doom1" => "doom1.wad",
                "doom" => "doom.wad",
                "plutonia" => "plutonia.wad",
                "tnt" => "tnt.wad",
                "heretic" => "heretic.wad",
                "hexen" => "hexen.wad",
                _ => "doom2.wad"
            };
        }


        private async Task<DoomsdayBuild?> FetchLatestBuildAsync()
        {
            try
            {
                using var http = new HttpClient();

                string html = await http.GetStringAsync("https://api.dengine.net/1/builds");

                // Find newest build 
                var match = System.Text.RegularExpressions.Regex.Match(
                    html,
                    @"(\d+).*?2\.\d+\.\d+",
                    System.Text.RegularExpressions.RegexOptions.Singleline);

                if (!match.Success)
                    return null;

                string buildNumber = match.Groups[1].Value;

                // Extract version separately
                var versionMatch = System.Text.RegularExpressions.Regex.Match(
                    html,
                    @"2\.\d+\.\d+");

                if (!versionMatch.Success)
                    return null;

                string version = versionMatch.Value;

                string fullVersion = $"{version}-build{buildNumber}";

                string fileName = $"doomsday_{fullVersion}_x86.zip";

                return new DoomsdayBuild
                {
                    Version = fullVersion,
                    FileName = fileName,
                    Url = "https://api.dengine.net/1/builds?dl=" + fileName
                };
            }
            catch (Exception ex)
            {
                Log("Failed to fetch latest build: " + ex.Message);
                return null;
            }
        }


        private async Task CheckForUpdates()
        {
            // clear LogText first
            LogText = string.Empty;
            try
            {
                var latestBuild = await FetchLatestBuildAsync();

                if (latestBuild == null)
                {
                    UpdateStatus = "Failed to fetch latest version.";
                    Log("Could not retrieve latest Doomsday build.");
                    return;
                }

                string latestVersion = latestBuild.Version;
                string fileName = latestBuild.FileName;
                string downloadUrl = latestBuild.Url;

                // Get currently installed version
                string currentVersion = GetInstalledVersion();

                // Compare versions
                if (!string.IsNullOrWhiteSpace(currentVersion))
                {
                    // Parse versions for comparison
                    bool isUpToDate = CompareVersions(currentVersion, latestVersion);
                    if (isUpToDate)
                    {
                        var window = GetWindow();
                        if (window != null)
                        {
                            var dialog = new ConfirmDialog(
                                $"You already have the latest version installed!\n\nInstalled: {currentVersion}\nLatest: {latestVersion}",
                                false);

                            await dialog.ShowDialog<bool>(window);
                        }

                        UpdateStatus = "Already up to date.";
                        Log("Already up to date: " + currentVersion);
                        return;
                    }
                }

                // New version available - ask user
                bool ok = await ConfirmUpdate(latestVersion, currentVersion);
                if (!ok)
                {
                    UpdateStatus = "Update canceled.";
                    Log("Update canceled.");
                    return;
                }

                IsUpdating = true;
                Log("Starting update...");
                UpdateProgress = 0;

                using var http = new HttpClient();
                string tempZip = Path.Combine(Path.GetTempPath(), fileName);

                // ============================
                // DOWNLOAD
                // ============================
                //
               

                UpdateStatus = "Downloading update...";
                LogText = "Downloading update..." + Environment.NewLine;

                using (var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var total = response.Content.Headers.ContentLength ?? -1L;
                    var canReport = total > 0;

                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var fs = File.Create(tempZip);

                    var buffer = new byte[81920];
                    long read = 0;
                    int bytes;
                    int lastPct = -1;

                    while ((bytes = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fs.WriteAsync(buffer, 0, bytes);
                        read += bytes;

                        if (canReport)
                        {
                            int pct = (int)((read * 100L) / total);
                            if (pct != lastPct)
                            {
                                lastPct = pct;
                                UpdateProgress = pct;
                                UpdateStatus = $"Downloading update... {pct}%";

                                if (UpdateStatus == "Downloading update... 100%")
                                {
                                    Log("Download complete.");
                                }
                            }
                        }
                    }
                }

                // ============================
                // EXTRACT
                // ============================
                UpdateStatus = "Extracting... 100%";
                Log("Extracting... 100%");
                UpdateProgress = 0;

                string extractPath = Path.Combine(Path.GetTempPath(), "doomsday_update");

                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, extractPath);

                string[] dirs = Directory.GetDirectories(extractPath);
                if (dirs.Length == 0)
                    throw new Exception("Extracted update folder is empty.");

                string extractedRoot = dirs[0];

                // ============================
                // INSTALL
                // ============================
                UpdateStatus = "Installing... 0%";
                Log("Installing...");

                string binDir = Path.GetDirectoryName(EngineLocation);
                string installRoot = Path.GetDirectoryName(binDir);

                var allFiles = Directory.GetFiles(extractedRoot, "*.*", SearchOption.AllDirectories);
                int totalFiles = allFiles.Length;
                int processed = 0;
                int lastInstallPct = -1;

                foreach (var file in allFiles)
                {
                    string relative = file.Substring(extractedRoot.Length).TrimStart('\\', '/');
                    string targetFile = Path.Combine(installRoot, relative);

                    Directory.CreateDirectory(Path.GetDirectoryName(targetFile));
                    File.Copy(file, targetFile, true);

                    processed++;
                    int pct = (int)((processed * 100.0) / totalFiles);

                    if (pct != lastInstallPct)
                    {
                        lastInstallPct = pct;
                        UpdateProgress = pct;
                        UpdateStatus = $"Installing... {pct}%";

                        if (UpdateStatus == "Installing... 100%")
                        {
                            Log("Installation complete.");
                        }
                    }
                }

                // ============================
                // DONE
                // ============================
                UpdateProgress = 100;
                UpdateStatus = $"Updated to {latestVersion}";
                Log($"Updated to {latestVersion}");

                InstalledVersion = GetInstalledVersion();
                Log("Installed version is now: " + InstalledVersion);
            }
            catch (Exception ex)
            {
                UpdateStatus = "Update failed: " + ex.Message;
                Log("Update failed: " + ex.Message);
                Debug.WriteLine("Update error: " + ex);
            }
            finally
            {
                IsUpdating = false;
            }
        }


        /// <summary>
        /// Compares two Doomsday version strings
        /// Returns true if current >= latest (up to date), false if update needed
        /// </summary>
        /// 

       // We try to grab the latest 32bit version of Doomsday here for compatiblity.. 

        private bool CompareVersions(string current, string latest)
        {
            try
            {
                // Extract build numbers from versions like "2.3.2.3869" or "2.3.2-build3869"
                var currentBuild = ExtractBuildNumber(current);
                var latestBuild = ExtractBuildNumber(latest);

                if (currentBuild.HasValue && latestBuild.HasValue)
                {
                    return currentBuild.Value >= latestBuild.Value;
                }

                // If we can't parse, do simple string comparison
                return string.Equals(current, latest, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false; // Assume update needed if comparison fails
            }
        }

        /// <summary>
        /// Extracts build number from version string
        /// Examples: "2.3.2.3869" -> 3869, "2.3.2-build3869" -> 3869
        /// </summary>
        private int? ExtractBuildNumber(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;

            // Format: 2.3.2-build3869
            int buildIndex = version.IndexOf("build", StringComparison.OrdinalIgnoreCase);
            if (buildIndex >= 0)
            {
                string afterBuild = version.Substring(buildIndex + 5);

                if (int.TryParse(afterBuild, out int buildNum))
                    return buildNum;
            }

            // Format: 2.3.2 [#3869]
            int hashIndex = version.IndexOf("#");
            if (hashIndex >= 0)
            {
                string digits = new string(
                    version.Substring(hashIndex + 1)
                           .TakeWhile(char.IsDigit)
                           .ToArray());

                if (int.TryParse(digits, out int buildNum))
                    return buildNum;
            }

            // Format: 2.3.2.3869
            var parts = version.Split('.');
            if (parts.Length >= 4)
            {
                if (int.TryParse(parts[3], out int buildNum))
                    return buildNum;
            }

            return null;
        }
        private async Task<bool> ConfirmUpdate(string latestVersion, string currentVersion)
        {
            var window = GetWindow();
            if (window == null)
                return false;

            string message = string.IsNullOrWhiteSpace(currentVersion)
                ? $"A new version ({latestVersion}) is available.\nInstall it now?"
                : $"A new version is available!\n\nInstalled: {currentVersion}\nLatest: {latestVersion}\n\nInstall update now?";

            var dialog = new ConfirmDialog(message);
            var result = await dialog.ShowDialog<bool>(window);

            return result;
        }


        private Window? GetWindow()
        {
            return Avalonia.Application.Current?.ApplicationLifetime switch
            {
                IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
                _ => null
            };
        }



        private void CopyDirectory(string source, string target)
        {
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(source, target));

            foreach (var file in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
            {
                var dest = file.Replace(source, target);
                File.Copy(file, dest, true);
            }
        }



        // Look for freeware IWAD's if the user has none set:
        // If the user has already added their IWADs location, make the button disabled

        private async Task DownloadAllFreeContentAsync()
        {
            // Fallback to launcher base folder if no folder is chosen yet
            string targetFolder = !string.IsNullOrWhiteSpace(IWADFolder) && System.IO.Directory.Exists(IWADFolder)
                ? IWADFolder
                : Path.Combine(AppContext.BaseDirectory, "IWADS");

            // Ensure the folder physically exists
            if (!Directory.Exists(targetFolder))
            {
                try { Directory.CreateDirectory(targetFolder); } catch { }
            }

            LogText += $"{DateTime.Now:[HH:mm:ss]} Starting unified free content download manager...\n";

            // Download & Extract Shareware Doom 
            string sharewareUrl = "https://www.jbserver.com/downloads/games/doom/misc/shareware/doom1.wad.zip";
            LogText += $"{DateTime.Now:[HH:mm:ss]} Downloading Shareware Doom...\n";
            bool sharewareSuccess = await DownloadAndExtractMultipleFromZipAsync(sharewareUrl, new[] { "doom1.wad" }, targetFolder, "Shareware Doom");

            // Download & Extract Freedoom 1 and 2
            string freedoomUrl = "https://github.com/freedoom/freedoom/releases/download/v0.13.0/freedoom-0.13.0.zip";
            LogText += $"{DateTime.Now:[HH:mm:ss]} Downloading Freedoom bundle package...\n";
            bool freedoomSuccess = await DownloadAndExtractMultipleFromZipAsync(freedoomUrl, new[] { "freedoom1.wad", "freedoom2.wad" }, targetFolder, "Freedoom Bundle");

            // Refresh UI and Populate Game Selectors 
            if (sharewareSuccess || freedoomSuccess)
            {
                
                IWADFolder = targetFolder;

                PopulateIWADs(targetFolder);

                // pick the first freshly downloaded item so the selection isn't blank
                if (IWADs.Contains("doom2.wad")) SelectedIWAD = "doom2.wad";
                else if (IWADs.Contains("freedoom2.wad")) SelectedIWAD = "freedoom2.wad";
                else if (IWADs.Contains("doom1.wad")) SelectedIWAD = "doom1.wad";
                else SelectedIWAD = IWADs.FirstOrDefault();

                LogText += $"{DateTime.Now:[HH:mm:ss]} All extracted assets are fully integrated and ready to play!\n";
                UpdateStatus = "Free game content downloaded and installed successfully.";

                OnPropertyChanged(nameof(AreFreeIwadsInstalled));
                OnPropertyChanged(nameof(FreeIwadButtonText));
                OnPropertyChanged(nameof(ShouldFreeDownloadButtonGlow));
            }
            else
            {
                UpdateStatus = "Failed to process free content downloads.";
            }
        }



        private async Task<bool> DownloadAndExtractMultipleFromZipAsync(string url, string[] targetWadNames, string destinationFolder, string label)
        {
            string tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");
            bool extractedAny = false;

            try
            {
                IsUpdating = true;
                UpdateProgress = 0;

                using (var client = new HttpClient())
                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!response.IsSuccessStatusCode) return false;

                    long? totalBytes = response.Content.Headers.ContentLength;

                    using (var downloadStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[16384];
                        long totalRead = 0;
                        int read;

                        while ((read = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;

                            if (totalBytes.HasValue && totalBytes.Value > 0)
                            {
                                UpdateProgress = (int)((double)totalRead / totalBytes.Value * 100);
                                double mbRead = (double)totalRead / 1024 / 1024;
                                double mbTotal = (double)totalBytes.Value / 1024 / 1024;
                                UpdateStatus = $"Downloading {label}: {mbRead:F2} MB / {mbTotal:F2} MB";
                            }
                        }
                    }
                }

                UpdateStatus = $"Extracting assets from {label}...";

                using (ZipArchive archive = ZipFile.OpenRead(tempZipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        // Check if the current archive item matches ANY of our filenames
                        foreach (string wadName in targetWadNames)
                        {
                            if (entry.Name.Equals(wadName, StringComparison.OrdinalIgnoreCase))
                            {
                                string targetDestPath = Path.Combine(destinationFolder, wadName);

                                if (File.Exists(targetDestPath)) File.Delete(targetDestPath);

                                entry.ExtractToFile(targetDestPath);
                                LogText += $"{DateTime.Now:[HH:mm:ss]} Successfully extracted: {wadName}\n";
                                extractedAny = true;
                            }
                        }
                    }
                }

                return extractedAny;
            }
            catch (Exception ex)
            {
                LogText += $"{DateTime.Now:[HH:mm:ss]} Error processing {label}: {ex.Message}\n";
                return false;
            }
            finally
            {
                if (File.Exists(tempZipPath)) try { File.Delete(tempZipPath); } catch { }
                IsUpdating = false;
            }
        }




        private string _logText = "";
        public string LogText
        {
            get => _logText;
            set => SetProperty(ref _logText, value);
        }


        // InfoText 

        private string _infoText = "Thank you for using Simple Doomsday Engine Launcher!\n " + Environment.NewLine
          + Environment.NewLine + "Simple Doomsday Engine Launcher is designed to make launching classic Doom-engine " +
            "games easier with the Doomsday Engine: https://dengine.net/ while also providing built-in multiplayer hosting, server browsing, PWAD support, automatic " +
            "engine installation, and updating.\r\n\r\n---\r\n\r\n# Features\r\n\r\n- Modern Avalonia UI interface\r\n- " +
            "Automatic Doomsday Engine installation\r\n- Automatic Doomsday Engine updates\r\n- Multiplayer server browser\r\n- " +
            "Join servers directly from the launcher\r\n- Host public or private multiplayer games\r\n- PWAD support\r\n- " +
            "IWAD auto-detection\r\n- IWAD preview artwork\r\n- Map selection\r\n- Skill selection\r\n- Deathmatch / Co-op support\r\n-" +
            " Max player selection\r\n- Resolution selection\r\n- Jumping toggle\r\n- Reverb toggle\r\n- Mouse look toggle\r\n- " +
            "Live updater progress display\r\n- Auto-generated server configuration\r\n- Local server auto-connect support\r\n\r\n---" +
            "\r\n\r\n# Supported Games\r\n\r\n- Doom Shareware\r\n- Ultimate Doom\r\n- Doom II\r\n- TNT Evilution\r\n- " +
            "The Plutonia Experiment\r\n- Heretic\r\n- Hexen\r\n- Chex Quest\r\n- Hacx\r\n- Freedoom Phase 1\r\n- Freedoom Phase 2\r\n\r\n---\r\n " +
            "\r\n Simple Doomsday Engine Launcher was created by:\r\n Ron Goode ~aka~ Mr.Rocket \r\n 8-3-2026 \r\n View some of my other projects here:\r\n https://github.com/MrRocket \r\n https://doomwiki.org/wiki/Ron_Goode_(Mr.Rocket) \r\n \r\n Happy Fragg'n! \r\n And enjoy using the launcher! ✟\r\n";
        public string InfoText
        {
            get => _infoText;
            set => SetProperty(ref _infoText, value);
        }

        private void LogProgress(string label, int percent)
        {
            LogText += $"{label}... {percent}%{Environment.NewLine}";
            UpdateStatus = $"{label}... {percent}%";
        }

        private string GetInstalledVersion()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(EngineLocation) || !File.Exists(EngineLocation))
                    return null;

                var info = FileVersionInfo.GetVersionInfo(EngineLocation);
                return info.FileVersion;
            }
            catch
            {
                return null;
            }
        }


        private void OpenLog()
        {
            var window = GetWindow();
            if (window == null)
                return;

            // If window was closed create a new one
            if (_logWindow == null || !_logWindow.IsVisible)
            {
                _logVM = new LogWindowViewModel();
                _logWindow = new LogWindow
                {
                    DataContext = _logVM
                };

                // When closed, clear reference so we can recreate it later
                _logWindow.Closed += (_, __) =>
                {
                    _logWindow = null;
                    _logVM = null;
                };
            }

            _logWindow.Show(window);
        }

        public async Task JoinServerFromBrowser(DoomsdayServerInfo server)
        {
            if (server == null)
                return;

            string ip = server.IP;
            int port = server.Port;

            // Build command line
            string args = $"-connect {ip}:{port}";

            // Launch the engine
            await LaunchEngineAsync(args);
        }

        public async Task LaunchEngineAsync(string args)
        {
            if (string.IsNullOrWhiteSpace(EngineLocation) || !File.Exists(EngineLocation))
            {
                Log("Engine location not set.");
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = EngineLocation,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };

                Process.Start(psi);
                Log("Launching engine with args: " + args);
            }
            catch (Exception ex)
            {
                Log("Failed to launch engine: " + ex.Message);
            }
        }


        private async void InstallDoomsday()
        {
            // Kill running Doomsday processes
            foreach (var proc in Process.GetProcessesByName("doomsday"))
            {
                try { proc.Kill(); proc.WaitForExit(); } catch { }
            }
            foreach (var proc in Process.GetProcessesByName("doomsday-server"))
            {
                try { proc.Kill(); proc.WaitForExit(); } catch { }
            }

            try
            {
                string version = "2.3.2-build3869";
                string folderName = "doomsday_2.3.2-build3869_x86";
                string fileName = folderName + ".zip";
                string downloadUrl = "https://api.dengine.net/1/builds?dl=" + fileName;

                IsUpdating = true;
                Log("Starting installation...");
                UpdateProgress = 0;

                using var http = new HttpClient();
                string tempZip = Path.Combine(Path.GetTempPath(), fileName);

                // DOWNLOAD
                UpdateStatus = "Downloading engine...";
                LogText = "Downloading engine..." + Environment.NewLine;

                using (var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var total = response.Content.Headers.ContentLength ?? -1L;
                    var canReport = total > 0;

                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var fs = File.Create(tempZip);

                    var buffer = new byte[81920];
                    long read = 0;
                    int bytes;
                    int lastPct = -1;

                    while ((bytes = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fs.WriteAsync(buffer, 0, bytes);
                        read += bytes;

                        if (canReport)
                        {
                            int pct = (int)((read * 100L) / total);
                            if (pct != lastPct)
                            {
                                lastPct = pct;
                                UpdateProgress = pct;
                                UpdateStatus = $"Downloading engine... {pct}%";
                                if (pct == 100) Log("Download complete.");
                            }
                        }
                    }
                }

                // EXTRACT TO TEMP
                UpdateStatus = "Extracting... 100%";
                Log("Extracting... 100%");
                UpdateProgress = 0;

                string tempExtract = Path.Combine(Path.GetTempPath(), "DoomsdayExtract");
                if (Directory.Exists(tempExtract))
                    Directory.Delete(tempExtract, true);
                Directory.CreateDirectory(tempExtract);

                System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, tempExtract);

                string extractedRoot = Path.Combine(tempExtract, folderName);
                if (!Directory.Exists(extractedRoot))
                    throw new Exception("Extracted folder missing: " + folderName);

                // INSTALL ROOT
                string launcherRoot = AppContext.BaseDirectory;

                string installRoot = Path.Combine(
                    launcherRoot,
                    "Engines",
                    "Doomsday"
                );


                if (Directory.Exists(installRoot))
                    Directory.Delete(installRoot, true);
                Directory.CreateDirectory(installRoot);

                // INSTALL
                UpdateStatus = "Installing... 0%";
                Log("Installing...");

                var allFiles = Directory.GetFiles(extractedRoot, "*.*", SearchOption.AllDirectories);
                int totalFiles = allFiles.Length;
                int processed = 0;
                int lastInstallPct = -1;

                // Create all directories first
                foreach (var dir in Directory.GetDirectories(extractedRoot, "*", SearchOption.AllDirectories))
                {
                    string relative = dir.Substring(extractedRoot.Length).TrimStart('\\', '/');
                    Directory.CreateDirectory(Path.Combine(installRoot, folderName, relative));
                }

                // Copy all files
                foreach (var file in allFiles)
                {
                    string relative = file.Substring(extractedRoot.Length).TrimStart('\\', '/');
                    string targetFile = Path.Combine(installRoot, folderName, relative);

                    Directory.CreateDirectory(Path.GetDirectoryName(targetFile));
                    File.Copy(file, targetFile, true);

                    processed++;
                    int pct = (int)((processed * 100.0) / totalFiles);
                    if (pct != lastInstallPct)
                    {
                        lastInstallPct = pct;
                        UpdateProgress = pct;
                        UpdateStatus = $"Installing... {pct}%";
                        if (pct == 100) Log("Installation complete.");
                    }
                }

                // DONE
                string enginePath = Path.Combine(
                    installRoot,
                    folderName,
                    "bin",
                    "doomsday.exe"
                );

                EngineLocation = enginePath;
                IsDoomsdayInstalled = File.Exists(enginePath);

                UpdateProgress = 100;
                UpdateStatus = $"Installed Doomsday {version}";
                Log($"Installed Doomsday {version}");
            }
            catch (Exception ex)
            {
                UpdateStatus = "Install failed: " + ex.Message;
                Log("Install failed: " + ex.Message);
            }
            finally
            {
                IsUpdating = false;
            }
        }

        private void TryAutoDetectServer()
        {
            if (string.IsNullOrWhiteSpace(EngineLocation))
                return;

            try
            {
                string engineDir = Path.GetDirectoryName(EngineLocation);
                if (string.IsNullOrWhiteSpace(engineDir))
                    return;

                string serverPath = Path.Combine(engineDir, "doomsday-server.exe");

                if (File.Exists(serverPath))
                    ServerLocation = serverPath;
            }
            catch
            {
                // ignore errors silently
            }
        }


    

    }
}
