﻿using Aurora.EffectsEngine;
using Aurora.Settings;
using Aurora.Settings.Layers;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Aurora.Profiles
{
    public class LightEventConfig : NotifyPropertyChangedEx
    {
        //TODO: Add NotifyPropertyChanged to properties
        public string[] ProcessNames { get; set; }

        /// <summary>One or more REGULAR EXPRESSIONS that can be used to match the title of an application</summary>
        public string[] ProcessTitles { get; set; }

        public string Name { get; set; }

        public string ID { get; set; }

        public string AppID { get; set; }

        public Type SettingsType { get; set; } = typeof(ApplicationSettings);

        public Type ProfileType { get; set; } = typeof(ApplicationProfile);

        public Type GameStateType { get; set; }

        public LightEvent Event { get; set; }

        //public int? UpdateInterval { get; set; } = null;

        public string IconURI { get; set; }

        public HashSet<string> ExtraAvailableLayers { get; set; } = new HashSet<string>();

        protected LightEventType? type = LightEventType.Normal;
        public LightEventType? Type
        {
            get { return type; }
            set
            {
                object old = type;
                object newVal = value;
                type = value;
                InvokePropertyChanged(old, newVal);
            }
        }
    }

    public class Application : ObjectSettings<ApplicationSettings>, IInit, ILightEvent, IDisposable
    {
        #region Public Properties
        public bool Initialized { get; protected set; } = false;
        public bool Disposed { get; protected set; } = false;
        public ApplicationProfile Profile { get; set; }
        public ObservableCollection<ApplicationProfile> Profiles { get; set; }
        public Dictionary<string, Tuple<Type, Type>> ParameterLookup { get; set; } //Key = variable path, Value = {Return type, Parameter type}
        public bool HasLayers { get; set; }
        public event EventHandler ProfileChanged;
        public bool ScriptsLoaded { get; protected set; }
        public LightEventConfig Config { get; protected set; }
        public string ID { get { return Config.ID; } }
        public Type GameStateType { get { return Config.GameStateType; } }
        public bool IsEnabled { get { return Settings.IsEnabled; } }
        public event PropertyChangedExEventHandler PropertyChanged;
        protected LightEventType type;
        public LightEventType Type
        {
            get { return type; }
            protected set
            {
                object old = type;
                object newVal = value;
                type = value;
                InvokePropertyChanged(old, newVal);
            }
        }
        #endregion

        #region Internal Properties
        //protected ImageSource icon;
        //public virtual ImageSource Icon => icon ?? (icon = new BitmapImage(new Uri(Config.IconURI, UriKind.Relative)));
        internal Dictionary<string, IEffectScript> EffectScripts { get; set; }
        #endregion

        #region Private Fields/Properties
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        #endregion

        public Application(LightEventConfig config) : base()
        {
            Config = config;
            SettingsSavePath = Path.Combine(GetProfileFolderPath(), "settings.json");
            config.Event.Application = this;
            config.Event.ResetGameState();
            Profiles = new ObservableCollection<ApplicationProfile>();
            Profiles.CollectionChanged += (sender, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    foreach (ApplicationProfile prof in e.NewItems)
                    {
                        prof.SetApplication(this);
                    }
                }
            };
            EffectScripts = new Dictionary<string, IEffectScript>();
            if (config.GameStateType != null)
                ParameterLookup = Utils.GameStateUtils.ReflectGameStateParameters(config.GameStateType);
        }

        public virtual bool Initialize()
        {
            if (Initialized)
                return Initialized;

            LoadSettings(Config.SettingsType);
            LoadProfiles();
            Initialized = true;
            return Initialized;
        }

        protected void InvokePropertyChanged(object oldValue, object newValue, [CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedExEventArgs(propertyName, oldValue, newValue));
        }

        public void SwitchToProfile(ApplicationProfile newProfileSettings)
        {
            if (Disposed)
                return;

            if (newProfileSettings != null && Profile != newProfileSettings)
            {
                if (Profile != null)
                {
                    this.SaveProfile();
                    Profile.PropertyChanged -= Profile_PropertyChanged;
                }

                Profile = newProfileSettings;
                this.Settings.SelectedProfile = Path.GetFileNameWithoutExtension(Profile.ProfileFilepath);
                Profile.PropertyChanged += Profile_PropertyChanged;

                ProfileChanged?.Invoke(this, new EventArgs());
            }
        }

        protected virtual ApplicationProfile CreateNewProfile(string profileName)
        {
            ApplicationProfile profile = (ApplicationProfile)Activator.CreateInstance(Config.ProfileType);
            profile.ProfileName = profileName;
            profile.ProfileFilepath = Path.Combine(GetProfileFolderPath(), GetUnusedFilename(GetProfileFolderPath(), profile.ProfileName) + ".json");
            return profile;
        }

        public void AddProfile(ApplicationProfile profile)
        {
            if (Disposed)
                return;

            profile.ProfileFilepath = Path.Combine(GetProfileFolderPath(), GetUnusedFilename(GetProfileFolderPath(), profile.ProfileName) + ".json");
            this.Profiles.Add(profile);
        }

        protected ApplicationProfile CreateDefaultProfile()
        {
            return AddNewProfile($"default");
        }

        public ApplicationProfile NewDefaultProfile()
        {
            return AddNewProfile($"Profile {Profiles.Count + 1}");
        }

        public ApplicationProfile AddNewProfile(String profileName)
        {
            if (Disposed)
                return null;

            ApplicationProfile _newProfile = CreateNewProfile(profileName);

            Profiles.Add(_newProfile);

            SaveProfiles();

            SwitchToProfile(_newProfile);

            return _newProfile;
        }

        public void DeleteProfile(ApplicationProfile profile)
        {
            if (Disposed)
                return;

            if (Profiles.Count == 1)
                return;

            if (profile != null && !String.IsNullOrWhiteSpace(profile.ProfileFilepath))
            {
                int profileIndex = Profiles.IndexOf(profile);

                if (Profiles.Contains(profile))
                    Profiles.Remove(profile);

                if (Profile.Equals(profile))
                    SwitchToProfile(Profiles[Math.Min(profileIndex, Profiles.Count - 1)]);

                if (File.Exists(profile.ProfileFilepath))
                {
                    try
                    {
                        File.Delete(profile.ProfileFilepath);
                    }
                    catch (Exception exc)
                    {
                        logger.Error($"Could not delete profile with path \"{profile.ProfileFilepath}\"");
                        logger.Error($"Exception: {exc}");
                    }
                }

                SaveProfiles();
            }
        }

        protected string GetValidFilename(string filename)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                filename = filename.Replace(c, '_');

            return filename;
        }

        protected string GetUnusedFilename(string dir, string filename)
        {
            var safeName = GetValidFilename(filename);
            if (!File.Exists(Path.Combine(dir, safeName + ".json"))) return safeName;
            var i = 0;
            while (File.Exists(Path.Combine(dir, safeName + "-" + ++i + ".json"))) ;
            return safeName + "-" + i;
        }

        public virtual string GetProfileFolderPath()
        {
            return Path.Combine(Const.AppDataDirectory, "Profiles", Config.ID);
        }

        public void ResetProfile()
        {
            if (Disposed)
                return;

            try
            {
                Profile.Reset();
                //Profile.PropertyChanged += Profile_PropertyChanged;

                //this.InitalizeScriptSettings(Profile, true);

                ProfileChanged?.Invoke(this, new EventArgs());
            }
            catch (Exception exc)
            {
                logger.Error(string.Format("Exception Resetting Profile, Exception: {0}", exc));
            }
        }

        //hacky fix to sort out MoD profile type change
        protected ISerializationBinder binder = Utils.JSONUtils.SerializationBinder;
        internal ApplicationProfile LoadProfile(string path)
        {
            if (Disposed)
                return null;

            try
            {
                if (File.Exists(path))
                {
                    string profile_content = File.ReadAllText(path, Encoding.UTF8);

                    if (!String.IsNullOrWhiteSpace(profile_content))
                    {
                        ApplicationProfile prof = (ApplicationProfile)JsonConvert.DeserializeObject(profile_content, Config.ProfileType, new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace, TypeNameHandling = TypeNameHandling.All, SerializationBinder = binder, Error = new EventHandler<Newtonsoft.Json.Serialization.ErrorEventArgs>(LoadProfilesError) });
                        prof.ProfileFilepath = path;

                        if (String.IsNullOrWhiteSpace(prof.ProfileName))
                            prof.ProfileName = Path.GetFileNameWithoutExtension(path);

                        // Initalises a collection, setting the layers' profile/application property and adding events to them and the collections to save to disk.
                        void InitialiseLayerCollection(ObservableCollection<Layer> collection)
                        {
                            foreach (Layer lyr in collection.ToList())
                            {
                                //Remove any Layers that have non-functional handlers
                                if (lyr.Handler == null || !AuroraCore.Instance.LightingStateManager.LayerHandlers.ContainsKey(lyr.Handler.ID))
                                {
                                    prof.Layers.Remove(lyr);
                                    continue;
                                }

                                lyr.AnythingChanged += SaveProfilesEvent;
                            }

                            collection.CollectionChanged += (_, e) =>
                            {
                                if (e.NewItems != null)
                                    foreach (Layer lyr in e.NewItems)
                                        if (lyr != null)
                                            lyr.AnythingChanged += SaveProfilesEvent;
                                SaveProfiles();
                            };
                        }

                        // Call the above setup method on the regular layers and the overlay layers.
                        InitialiseLayerCollection(prof.Layers);
                        InitialiseLayerCollection(prof.OverlayLayers);

                        prof.PropertyChanged += Profile_PropertyChanged;
                        return prof;
                    }
                }
            }
            catch (Exception exc)
            {
                logger.Error(string.Format("Exception Loading Profile: {0}, Exception: {1}", path, exc));
                if (Path.GetFileNameWithoutExtension(path).Equals("default"))
                {
                    string newPath = path + ".corrupted";

                    int _copy = 1;
                    while (File.Exists(newPath))
                    {
                        newPath = path + $"({_copy++}).corrupted";
                    }

                    File.Move(path, newPath);
                    logger.Error($"Default profile for {this.Config.Name} could not be loaded.\nMoved to {newPath}, reset to default settings.\nException={exc.Message}");
                    //MessageBox.Show($"Default profile for {this.Config.Name} could not be loaded.\nMoved to {newPath}, reset to default settings.\nException={exc.Message}", "Error loading default profile", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            return null;
        }

        protected virtual void LoadProfilesError(object sender, Newtonsoft.Json.Serialization.ErrorEventArgs e)
        {
            if (e.CurrentObject != null)
            {
                if (e.CurrentObject.GetType().Equals(typeof(ObservableCollection<Layer>)))
                    e.ErrorContext.Handled = true;

                if (e.CurrentObject.GetType() == typeof(Layer) && e.ErrorContext.Member.Equals("Handler"))
                {
                    ((Layer)e.ErrorContext.OriginalObject).Handler = null;
                    e.ErrorContext.Handled = true;
                }
            }
            else if (e.ErrorContext.Path.Equals("$type") && e.ErrorContext.Member == null)
            {
                logger.Error($"The profile type for {this.Config.Name} has been changed, your profile will be reset and your old one moved to have the extension '.corrupted', ignore the following error");
                //MessageBox.Show($"The profile type for {this.Config.Name} has been changed, your profile will be reset and your old one moved to have the extension '.corrupted', ignore the following error", "Profile type changed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Profile_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is ApplicationProfile)
                SaveProfile(sender as ApplicationProfile);
        }

        public bool RegisterEffect(string key, IEffectScript obj)
        {
            if (Disposed)
                return false;

            if (this.EffectScripts.ContainsKey(key))
            {
                logger.Warn(string.Format("Effect script with key {0} already exists!", key));
                return false;
            }

            this.EffectScripts.Add(key, obj);

            return true;
        }

        public virtual void UpdateLights(EffectFrame frame)
        {
            if (Disposed)
                return;

            this.Config.Event.UpdateLights(frame);
        }

        public virtual void UpdateOverlayLights(EffectFrame frame)
        {
            if (Disposed) return;
            Config.Event.UpdateOverlayLights(frame);
        }

        public virtual void SetGameState(IGameState state)
        {
            if (Disposed)
                return;

            this.Config.Event.SetGameState(state);
        }

        public virtual void ResetGameState()
        {
            if (Disposed)
                return;

            Config.Event.ResetGameState();
        }

        public virtual void OnStart()
        {
            if (Disposed)
                return;

            Config.Event.OnStart();
        }

        public virtual void OnStop()
        {
            if (Disposed)
                return;

            Config.Event.OnStop();
        }

        public virtual void UpdateEffectScripts(Queue<EffectLayer> layers, IGameState state = null)
        {
            /*var _scripts = new Dictionary<string, ScriptSettings>(this.Settings.ScriptSettings).Where(s => s.Value.Enabled);

            foreach (KeyValuePair<string, ScriptSettings> scr in _scripts)
            {
                try
                {
                    dynamic script = this.EffectScripts[scr.Key];
                    dynamic script_layers = script.UpdateLights(scr.Value, state);
                    if (layers != null)
                    {
                        if (script_layers is EffectLayer)
                            layers.Enqueue(script_layers as EffectLayer);
                        else if (script_layers is EffectLayer[])
                        {
                            foreach (var layer in (script_layers as EffectLayer[]))
                                layers.Enqueue(layer);
                        }
                    }

                }
                catch (Exception exc)
                {
                    Global.logger.LogLine(string.Format("Script disabled! Effect script with key {0} encountered an error. Exception: {1}", scr.Key, exc), Logging_Level.External);
                    scr.Value.Enabled = false;
                    scr.Value.ExceptionHit = true;
                    scr.Value.Exception = exc;
                }
            }*/
        }

        /*protected void LoadScripts(string profiles_path, bool force = false)
        {
            if (!force && ScriptsLoaded)
                return;

            this.EffectScripts.Clear();

            string scripts_path = Path.Combine(profiles_path, Const.ScriptDirectory);
            if (!Directory.Exists(scripts_path))
                Directory.CreateDirectory(scripts_path);

            foreach (string script in Directory.EnumerateFiles(scripts_path, "*.*"))
            {
                try
                {
                    string ext = Path.GetExtension(script);
                    bool anyLoaded = false;
                    switch (ext)
                    {
                        case ".py":
                            var scope = AuroraCore.Instance.PythonEngine.ExecuteFile(script);
                            foreach (var v in scope.GetItems())
                            {
                                if (v.Value is IronPython.Runtime.Types.PythonType)
                                {
                                    Type typ = ((IronPython.Runtime.Types.PythonType)v.Value).__clrtype__();
                                    if (!typ.IsInterface && typeof(IEffectScript).IsAssignableFrom(typ))
                                    {
                                        IEffectScript obj = AuroraCore.Instance.PythonEngine.Operations.CreateInstance(v.Value) as IEffectScript;
                                        if (obj != null)
                                        {
                                            if (!(obj.ID != null && this.RegisterEffect(obj.ID, obj)))
                                                logger.Warn($"Script \"{script}\" must have a unique string ID variable for the effect {v.Key}");
                                            else
                                                anyLoaded = true;
                                        }
                                        else
                                            logger.Error($"Could not create instance of Effect Script: {v.Key} in script: \"{script}\"");
                                    }
                                }
                            }


                            break;
                        case ".cs":
                            Assembly script_assembly = CSScript.LoadFile(script);
                            Type effectType = typeof(IEffectScript);
                            foreach (Type typ in script_assembly.ExportedTypes)
                            {
                                if (effectType.IsAssignableFrom(typ))
                                {
                                    IEffectScript obj = (IEffectScript)Activator.CreateInstance(typ);
                                    if (!(obj.ID != null && this.RegisterEffect(obj.ID, obj)))
                                        logger.Warn(string.Format("Script \"{0}\" must have a unique string ID variable for the effect {1}", script, typ.FullName));
                                    else
                                        anyLoaded = true;
                                }
                            }

                            break;
                        default:
                            logger.Warn(string.Format("Script with path {0} has an unsupported type/ext! ({1})", script, ext));
                            continue;
                    }

                    if (!anyLoaded)
                        logger.Warn($"Script \"{script}\": No compatible effects found. Does this script need to be updated?");
                }
                catch (Exception exc)
                {
                    logger.Error(string.Format("An error occured while trying to load script {0}. Exception: {1}", script, exc));
                    //Maybe MessageBox info dialog could be included.
                }
            }
        }

        public void ForceScriptReload()
        {
            LoadScripts(GetProfileFolderPath(), true);
        }

        protected void InitalizeScriptSettings(ApplicationProfile profile_settings, bool ignore_removal = false)
        {
            foreach (string id in this.EffectScripts.Keys)
            {
                if (!profile_settings.ScriptSettings.ContainsKey(id))
                    profile_settings.ScriptSettings.Add(id, new ScriptSettings(this.EffectScripts[id]));
            }


            if (!ignore_removal)
            {
                foreach (string key in profile_settings.ScriptSettings.Keys.Where(s => !this.EffectScripts.ContainsKey(s)).ToList())
                {
                    profile_settings.ScriptSettings.Remove(key);
                }
            }
        }*/

        public virtual void LoadProfiles()
        {
            string profilesPath = GetProfileFolderPath();

            if (Directory.Exists(profilesPath))
            {
                //this.LoadScripts(profilesPath);

                foreach (string profile in Directory.EnumerateFiles(profilesPath, "*.json", SearchOption.TopDirectoryOnly))
                {

                    string profileFilename = Path.GetFileNameWithoutExtension(profile);
                    if (profileFilename.Equals(Path.GetFileNameWithoutExtension(SettingsSavePath)))
                        continue;
                    ApplicationProfile profileSettings = LoadProfile(profile);

                    if (profileSettings != null)
                    {
                        //this.InitalizeScriptSettings(profileSettings);

                        if (profileFilename.Equals("default") || profileFilename.Equals(Settings.SelectedProfile))
                            Profile = profileSettings;

                        Profiles.Add(profileSettings);
                    }
                }
            }
            else
            {
                logger.Info(string.Format("Profiles directory for {0} does not exist.", Config.Name));
            }

            if (Profile == null)
                SwitchToProfile(Profiles.FirstOrDefault());
            else
                Settings.SelectedProfile = Path.GetFileNameWithoutExtension(Profile.ProfileFilepath);

            if (Profile == null)
                CreateDefaultProfile();

        }

        public virtual void SaveProfile()
        {
            SaveProfile(this.Profile);
        }

        public virtual void SaveProfile(ApplicationProfile profile, string path = null)
        {
            if (Disposed)
                return;
            try
            {
                path = path ?? Path.Combine(GetProfileFolderPath(), profile.ProfileFilepath);
                JsonSerializerSettings settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All, Binder = Aurora.Utils.JSONUtils.SerializationBinder };
                string content = JsonConvert.SerializeObject(profile, Formatting.Indented, settings);

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, content, Encoding.UTF8);

            }
            catch (Exception exc)
            {
                logger.Error(string.Format("Exception Saving Profile: {0}, Exception: {1}", path, exc));
            }
        }

        public void SaveProfilesEvent(object sender, EventArgs e)
        {
            this.SaveProfiles();
        }

        public virtual void SaveProfiles()
        {
            if (Disposed)
                return;

            try
            {
                string profiles_path = GetProfileFolderPath();

                if (!Directory.Exists(profiles_path))
                    Directory.CreateDirectory(profiles_path);

                //SaveProfile(Path.Combine(profiles_path, Profile.ProfileFilepath), Profile);

                foreach (var profile in Profiles)
                {
                    SaveProfile(profile, Path.Combine(profiles_path, profile.ProfileFilepath));
                }
            }
            catch (Exception exc)
            {
                logger.Error("Exception during SaveProfiles, " + exc);
            }
        }

        public virtual void SaveAll()
        {
            if (Disposed)
                return;

            SaveSettings(Config.SettingsType);
            SaveProfiles();
        }

        protected override void LoadSettings(Type settingsType)
        {
            base.LoadSettings(settingsType);

            this.Settings.PropertyChanged += (sender, e) =>
            {
                SaveSettings(Config.SettingsType);
            };
        }

        public void Dispose()
        {
            if (Disposed)
                return;

            foreach (ApplicationProfile profile in this.Profiles)
                profile.Dispose();

            Disposed = true;
        }
    }
}