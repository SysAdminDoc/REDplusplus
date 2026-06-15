using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

using NotBob.Lib;

using RED;
using RED.Config;
using RED.Helper;
using TXT = RED.RedGetText;

namespace NotBob.Config
{
    // NotBob Configuration File helper routines.

    internal static class ConfigAssist
    {
        private static int RedirectCount = 0;
        private static readonly int RedirectMax = 3;

        /// <summary>
        /// Headless (-silent) mode: never show dialogs — a modal prompt would hang a
        /// scheduled task. With no config file, in-memory defaults are used read-only.
        /// </summary>
        internal static bool SilentMode = false;

        internal static void ConfigLoad(ref RedConfiguration config, string appName)
        {
            bool createConfig = false;
            string filename = "?";
            try
            {
                string cfgName = Path.GetFileNameWithoutExtension(Application.ExecutablePath) + ".cfg";
                filename = ConfigAssist.GetConfigFilename(cfgName, appName, Application.ExecutablePath);
                if (!string.IsNullOrEmpty(filename))
                {
                    while (File.Exists(filename) && new FileInfo(filename).Length > 0)
                    {
                        config = ConfigAssist.Load<RedConfiguration>(filename);

                        // A crafted/corrupt file can deserialize with a nil child object;
                        // restore any missing sub-object before anything dereferences it.
                        config?.EnsureSubObjects();

                        // Does the config file redirect to another location?
                        if (!string.IsNullOrWhiteSpace(config.RedirectTo))
                        {
                            RedirectCount++;
                            if (RedirectCount >= RedirectMax)
                            {
                                string redirectMsg = string.Format(TXT.Translate("Redirect maximum [{0}] reached in configuration file"), RedirectMax);
                                // Never show a modal in headless mode — it would hang a scheduled task.
                                if (SilentMode)
                                    Console.Error.WriteLine(redirectMsg);
                                else
                                    UiAssist.MsgBoxError(redirectMsg);
                                config.RedirectTo = string.Empty;
                                break;
                            }
                            else
                            {
                                filename = config.RedirectTo;
                            }
                        }
                        else
                        {
                            // No redirect in place, use this configuration file
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(filename) && File.Exists(filename))
                    {
                        // The config may already have its ReadOnly flag set.
                        // This is an extra check to see if the file itself is set to ReadOnly
                        if (File.GetAttributes(filename).HasFlag(FileAttributes.ReadOnly))
                        {
                            config.IsReadOnly = true;
                        }
                        // If the file is zero length, then the config needs to be created
                        if (new FileInfo(filename).Length == 0)
                        {
                            createConfig = true;
                        }
                    }
                    else
                    {
                        createConfig = true;
                    }
                }
                else
                {
                    createConfig = true;
                }

                if (createConfig)
                {
                    RedConfiguration obj = new RedConfiguration();
                    obj.SetToDefaults();
                    config = obj;
                    if (string.IsNullOrWhiteSpace(filename) || SilentMode)
                    {
                        config.IsReadOnly = true;
                    }
                }
            }
            catch (Exception ex)
            {
                string emsg = string.Format("{0}:{1}{2}", TXT.Translate("Error trying to read configuration file"), RedGetText.CrLf1, filename);
                if (SilentMode)
                {
                    Console.Error.WriteLine(emsg + " - " + ex.Message);
                }
                else
                {
                    UiAssist.MsgBoxException(emsg, ex);
                }

                // A corrupt or unreadable config must not crash the app —
                // fall back to read-only defaults
                if (config == null)
                {
                    config = new RedConfiguration();
                    config.SetToDefaults();
                }
                config.IsReadOnly = true;
            }
            finally
            {
                config.PopulateRuntime(filename, Application.ExecutablePath, Application.ProductName, Application.ProductVersion);
                if (createConfig && !string.IsNullOrWhiteSpace(config.Runtime.ConfigFilename) && !config.IsReadOnly)
                {
                    ConfigSave(config);
                }
                config.DataIsDirty = false;
            }
        }

        private static void ConfigSave(RedConfiguration config)
        {
            try
            {
                config.CreatedBy = config.Runtime.CreatedBy;

                if (!config.IsReadOnly)
                {
                    string cfgFolder = Path.GetDirectoryName(config.Runtime.ConfigFilename);
                    if ((!Directory.Exists(cfgFolder)))
                    {
                        Directory.CreateDirectory(cfgFolder);
                    }
                    ConfigAssist.Save(config, config.Runtime.ConfigFilename);
                    config.DataIsDirty = false;
                }
                else
                {
                    string roMsg = TXT.Translate("Config File is READ ONLY and cannot be saved.");
                    if (SilentMode)
                        Console.Error.WriteLine(roMsg);
                    else
                        UiAssist.MsgBoxError(roMsg);
                }
            }
            catch (Exception ex)
            {
                string emsg = string.Format("{0}:{1}{2}", TXT.Translate("Error trying to save configuration file"), RedGetText.CrLf1, config.Runtime.ConfigFilename);
                // Headless save failures must report to stderr, never a modal dialog.
                if (SilentMode)
                    Console.Error.WriteLine(emsg + " - " + ex.Message);
                else
                    UiAssist.MsgBoxException(emsg, ex);
                config.IsReadOnly = true;
            }
        }

        internal static void ConfigSaveWithPrompt(RedConfiguration config, bool ask = false)
        {
            bool saveRequired = config.DataIsDirty && !config.IsReadOnly;
            if (saveRequired)
            {
                if (ask)
                {
                    saveRequired = ConfigAssist.ConfigSavePrompt(config, saveRequired);
                }
                if (saveRequired)
                {
                    ConfigAssist.ConfigSave(config);
                }
            }
        }

        private static bool ConfigSavePrompt(RedConfiguration config, bool saveRequired)
        {
            string msg = TXT.Translate("Save Settings?") + "\r\n" + config.Filename;
            return UiAssist.BAskYesNo(msg, saveRequired ? MessageBoxDefaultButton.Button1 : MessageBoxDefaultButton.Button2);
        }

        private static string GetConfigFilename(string cfgName, string appName, string executablePath)
        {
            string configFilename = string.Empty;
            try
            {
                // Try portable mode first (ie app folder)
                configFilename = GetConfigFilenamePortable(cfgName, appName, executablePath);
                if (!File.Exists(configFilename))
                {
                    // Try %APPDATA%
                    string cfgFile = GetConfigFilenameAppData(cfgName, appName, executablePath);
                    if (File.Exists(cfgFile))
                    {
                        configFilename = cfgFile;
                    }
                    else if (SilentMode)
                    {
                        // Headless: never prompt — run with in-memory defaults
                        configFilename = string.Empty;
                    }
                    else
                    {
                        // No config file found, ask user where they would like it to be created
                        if (ConfirmUseOfPortableMode())
                        {
                            // User wants to use portable mode
                            configFilename = GetConfigFilenamePortable(cfgName, appName, executablePath);
                        }
                        else
                        {
                            // User wants to use %APPDATA%
                            configFilename = GetConfigFilenameAppData(cfgName, appName, executablePath);
                        }
                        //string cfgFolder = Path.GetDirectoryName(configFilename);
                    }
                }
            }
            catch (Exception)
            {
                configFilename = string.Empty;
            }
            return configFilename;
        }

        private static string GetConfigFilenamePortable(string cfgName, string appName, string executablePath)
        {
            string cfgFolder = Path.GetDirectoryName(executablePath);
            string configFilename = Path.Combine(cfgFolder, cfgName);
            return configFilename;
        }

        private static string GetConfigFilenameAppData(string cfgName, string appName, string executablePath)
        {
            string cfgBase = Path.GetFileNameWithoutExtension(cfgName);
            string cfgFolder = GetNotBobAppFolder(appName, Environment.SpecialFolder.ApplicationData);
            string configFilename = Path.Combine(cfgFolder, cfgName);
            return configFilename;
        }

        private static bool AskUserForConfigPortableOrAppData(int defaultButton)
        {
            bool respx = false;
            using (NotBob.UI.NBMsgBox mbox = new NotBob.UI.NBMsgBox(TXT.Translate("Choose RED++ settings location"), MessageBoxIcon.Question))
            {
                mbox.ControlBox = true;
                mbox.Icon = RED.Properties.Resources.iconProject;
                mbox.SetMinSize(520, 150);
                mbox.SetMessage(TXT.Translate("Where should RED++ store settings?") + RedGetText.CrLf2
                    + TXT.Translate("Portable file: stores RED+.cfg beside RED+.exe.") + RedGetText.CrLf1
                    + TXT.Translate("AppData: best for Program Files or managed installs."));
                mbox.SetButton(1, TXT.Translate("Use portable file"), DialogResult.Yes, isDefault: defaultButton == 1);
                mbox.SetButton(2, TXT.Translate("Use AppData"), DialogResult.No, isDefault: defaultButton == 2);
                mbox.ShowDialog();
                switch (mbox.DialogExitButton)
                {
                    case NotBob.UI.NBMsgBoxExitButton.Button1:
                        respx = (defaultButton == 1) ? true : false;
                        break;
                    case NotBob.UI.NBMsgBoxExitButton.Button2:
                        respx = (defaultButton == 2) ? true : false;
                        break;
                    default:
                        respx = false;
                        break;
                }
            }
            return respx;
        }

        private static bool ConfirmUseOfAppDataMode()
        {
            return AskUserForConfigPortableOrAppData(2);
        }

        private static bool ConfirmUseOfPortableMode()
        {
            return AskUserForConfigPortableOrAppData(1);
        }

        private static string GetNotBobAppFolder(string appName, Environment.SpecialFolder specialFolder)
        {
            string settingsFolder = GetNotBobFolder(specialFolder);
            settingsFolder = Path.Combine(settingsFolder, appName);
            return settingsFolder;
        }

        private static string GetNotBobFolder(Environment.SpecialFolder specialFolder)
        {
            string settingsFolder = GetSpecialFolder(specialFolder);
            settingsFolder = Path.Combine(settingsFolder, "NotBob");
            return settingsFolder;
        }

        private static string GetSpecialFolder(Environment.SpecialFolder specialFolder)
        {
            string settingsFolder = Environment.GetFolderPath(specialFolder);
            return settingsFolder;
        }

        private static T Load<T>(string filename)
        {
            T respx = default(T);
            if (File.Exists(filename))
            {
                respx = NBSerialize.DeserializeFromXmlFile<T>(filename);
            }
            return respx;
        }

        private static void Save<T>(T config, string filename)
        {
            NBSerialize.SerializeToXmlFile<T>(config, filename);
        }
    }
}
