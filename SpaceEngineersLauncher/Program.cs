using Sandbox;
using SpaceEngineers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using VRage.Plugins;
using System.Linq;
using System.Diagnostics;
using System.Threading;

namespace avaness.SpaceEngineersLauncher
{
    static class Program
    {
		private const uint AppId = 244850u;
		private const string AppIdFile = "steam_appid.txt";
		private const string RepoUrl = "https://github.com/sepluginloader/PluginLoader/";
		private const string RepoDownloadSuffix = "releases/download/{0}/PluginLoader-{0}.zip";
		private static readonly Regex VersionRegex = new Regex(@"^v(\d+\.)*\d+$");
		private const string PluginLoaderFile = "PluginLoader.dll";
		private const string AssemblyConfigFile = "SpaceEngineersLauncher.exe.config";
		private const string OriginalAssemblyConfig = "SpaceEngineers.exe.config";
		private const string ProgramGuid = "03f85883-4990-4d47-968e-5e4fc5d72437";
		private static readonly Version SupportedGameVersion = new Version(1, 202, 0);

		private static SplashScreen splash;
		private static Mutex mutex; // For ensuring only a single instance of SE

		static void Main(string[] args)
		{
			if (IsReport(args))
            {
				StartSpaceEngineers(args);
				return;
			}

			if (!IsSingleInstance())
				return;

			if (!IsSupportedGameVersion())
			{
				MessageBox.Show("Game version not supported! Requires " + SupportedGameVersion.ToString(3) + " or later");
				return;
			}

			StartPluginLoader(args);
			StartSpaceEngineers(args);
			Close();
		}

		private static void StartPluginLoader(string[] args)
        {
			splash = new SplashScreen("avaness.SpaceEngineersLauncher");

			try
			{
				LogFile.Init(Path.Combine("Plugins", "launcher.log"));
				LogFile.WriteLine("Starting - v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3));

				ConfigFile config = ConfigFile.Load(Path.Combine("Plugins", "launcher.xml"));

				// Fix tls 1.2 not supported on Windows 7 - github.com is tls 1.2 only
				try
				{
					ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
				}
				catch (NotSupportedException e)
				{
					LogFile.WriteLine("An error occurred while setting up networking, web requests will probably fail: " + e);
				}

				if (!File.Exists(AppIdFile))
				{
					LogFile.WriteLine(AppIdFile + " does not exist, creating.");
					File.WriteAllText(AppIdFile, AppId.ToString());
				}

				if (!Steamworks.SteamAPI.IsSteamRunning())
				{
					LogFile.WriteLine("Steam not detected!");
					MessageBox.Show("Steam must be running before you can start Space Engineers.");
					splash.Delete();
					Environment.Exit(0);
				}

				EnsureAssemblyConfigFile();

				if (!config.NoUpdates)
					Update(config);

				StringBuilder pluginLog = new StringBuilder("Loading plugins: ");
				List<string> plugins = new List<string>();

				if (CanUseLoader(config))
				{
					pluginLog.Append(PluginLoaderFile).Append(',');
					plugins.Add(PluginLoaderFile);
				}
				else
				{
					LogFile.WriteLine("WARNING: Plugin Loader does not exist.");
				}

				if (args != null && args.Length > 1)
				{
					int pluginFlag = Array.IndexOf(args, "-plugin");
					if (pluginFlag >= 0)
					{
						args[pluginFlag] = "";
						for (int i = pluginFlag + 1; i < args.Length && !args[i].StartsWith("-"); i++)
						{
							if (File.Exists(args[i]))
							{
								pluginLog.Append(args[i]).Append(',');
								plugins.Add(args[i]);
							}
							else
							{
								LogFile.WriteLine("WARNING: '" + args[i] + "' does not exist.");
							}
						}
					}
				}

				splash.SetText("Registering plugins...");

				if (plugins.Count > 0)
				{
					if (pluginLog.Length > 0)
						pluginLog.Length--;
					LogFile.WriteLine(pluginLog.ToString());

					MyPlugins.RegisterUserAssemblyFiles(plugins);
				}

				splash.SetText("Starting game...");
			}
			catch (Exception e)
			{
				if (Application.OpenForms.Count > 0)
				{
					Form form = Application.OpenForms[0];
					MessageBox.Show(form, "Plugin Loader crashed: " + e);
					form.Close();
				}
				else
				{
					LogFile.WriteLine("Error while getting Plugin Loader ready: " + e);
					MessageBox.Show("Plugin Loader crashed: " + e);
				}
			}

			MyCommonProgramStartup.BeforeSplashScreenInit += Close;
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static void StartSpaceEngineers(string[] args)
		{
			MyProgram.Main(args);
		}

        private static bool IsSupportedGameVersion()
		{
			SpaceEngineers.Game.SpaceEngineersGame.SetupBasicGameInfo();
			int? gameVersionInt = Sandbox.Game.MyPerGameSettings.BasicGameInfo.GameVersion;
			if (!gameVersionInt.HasValue)
				return true;
			string gameVersionStr = VRage.Utils.MyBuildNumbers.ConvertBuildNumberFromIntToStringFriendly(gameVersionInt.Value, ".");
			Version gameVersion = new Version(gameVersionStr);
			return gameVersion >= SupportedGameVersion;
        }

        private static bool IsSingleInstance()
        {
			// Check for other SpaceEngineersLauncher.exe
			mutex = new Mutex(true, ProgramGuid, out bool createdNew);
			if (!createdNew)
				return false;

			// Check for other SpaceEngineers.exe
			string sePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "SpaceEngineers.exe");
			if (Process.GetProcessesByName("SpaceEngineers").Any(x => x.MainModule.FileName.Equals(sePath, StringComparison.OrdinalIgnoreCase)))
				return false;

			return true;
        }

        private static void EnsureAssemblyConfigFile()
        {
			// Without this file, SE will have many bugs because its dependencies will not be correct.
            if (!File.Exists(AssemblyConfigFile) && File.Exists(OriginalAssemblyConfig))
			{
                File.Copy(OriginalAssemblyConfig, AssemblyConfigFile);
				Close();
				Application.Restart();
				Process.GetCurrentProcess().Kill();
			}
		}

		private static void Close()
		{
			MyCommonProgramStartup.BeforeSplashScreenInit -= Close;
			splash?.Delete();
			splash = null;
			LogFile.Dispose();
		}

        static bool IsReport(string[] args)
        {
			return args != null && args.Length > 0 
				&& (Array.IndexOf(args, "-report") >= 0 || Array.IndexOf(args, "-reporX") >= 0);
        }

		static void Update(ConfigFile config)
        {
			splash.SetText("Checking for updates...");


			string currentVersion = null;
			if (!string.IsNullOrWhiteSpace(config.LoaderVersion) && CanUseLoader(config) && VersionRegex.IsMatch(config.LoaderVersion))
			{
				LogFile.WriteLine("Plugin Loader " + currentVersion);
				currentVersion = config.LoaderVersion;
			}
			else
			{
				LogFile.WriteLine("Plugin Loader version unknown");
			}

			if (!IsLatestVersion(config.NetworkTimeout, currentVersion, out string latestVersion))
			{
				LogFile.WriteLine("An update is available to " + latestVersion);

				StringBuilder prompt = new StringBuilder();
				if (string.IsNullOrWhiteSpace(currentVersion))
                {
					prompt.Append("Plugin Loader is not installed!").AppendLine();
					prompt.Append("Version to download: ").Append(latestVersion).AppendLine();
					prompt.Append("Would you like to install it now?");
				}
				else
                {
					prompt.Append("An update is available for Plugin Loader:").AppendLine();
					prompt.Append(currentVersion).Append(" -> ").Append(latestVersion).AppendLine();
					prompt.Append("Would you like to update now?");
				}

				DialogResult result = MessageBox.Show(splash, prompt.ToString(), "Space Engineers Launcher", MessageBoxButtons.YesNoCancel);
				if (result == DialogResult.Yes)
				{
					splash.SetText("Downloading update...");
					if (!TryDownloadUpdate(config, latestVersion))
						MessageBox.Show("Update failed!");
				}
				else if (result == DialogResult.Cancel)
				{
					splash.Delete();
					Environment.Exit(0);
				}
			}
		}

		static bool IsLatestVersion(int networkTimeout, string currentVersion, out string latestVersion)
        {
			try
			{
				Uri uri = new Uri(RepoUrl + "releases/latest", UriKind.Absolute);
				LogFile.WriteLine("Downloading " + uri);
				HttpWebRequest request = WebRequest.CreateHttp(uri);
				request.Timeout = networkTimeout;
				HttpWebResponse response = request.GetResponse() as HttpWebResponse;
				if (response?.ResponseUri != null)
				{
					string version = response.ResponseUri.OriginalString;
					int versionStart = version.LastIndexOf('v');
					if (versionStart >= 0 && versionStart < version.Length)
					{
						latestVersion = version.Substring(versionStart);
						if (string.IsNullOrWhiteSpace(currentVersion) || currentVersion != latestVersion)
							return !VersionRegex.IsMatch(latestVersion);
					}
				}
			}
			catch (Exception e) 
			{
				LogFile.WriteLine("An error occurred while getting the latest version: " + e);
			}
			latestVersion = currentVersion;
			return true;
		}

		static bool TryDownloadUpdate(ConfigFile config, string version)
        {
			try
            {
				HashSet<string> files = new HashSet<string>();

				LogFile.WriteLine("Updating to Plugin Loader " + version);

				Uri uri = new Uri(RepoUrl + string.Format(RepoDownloadSuffix, version), UriKind.Absolute);
				LogFile.WriteLine("Downloading " + uri);
				HttpWebRequest request = WebRequest.CreateHttp(uri);
				request.Timeout = config.NetworkTimeout;
				request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

				HttpWebResponse response = (HttpWebResponse)request.GetResponse();
				using (Stream zipFileStream = response.GetResponseStream())
				using (ZipArchive zipFile = new ZipArchive(zipFileStream))
				{
					foreach (ZipArchiveEntry entry in zipFile.Entries)
					{
						string fileName = Path.GetFileName(entry.FullName);

						using (Stream entryStream = entry.Open())
						using (FileStream entryFile = File.Create(fileName))
						{
							entryStream.CopyTo(entryFile);
						}

						files.Add(fileName);
					}
				}

				config.LoaderVersion = version;
				config.Files = files.ToArray();
				config.Save();
				return true;
			}
			catch (Exception e)
			{
				LogFile.WriteLine("An error occurred while updating: " + e);
			}
			return false;
		}

		static bool CanUseLoader(ConfigFile config)
        {
			if (!File.Exists(PluginLoaderFile))
            {
				LogFile.WriteLine("WARNING: File verification failed, file does not exist: " + PluginLoaderFile);
				return false;
			}

			if (config.Files != null)
			{
				foreach (string file in config.Files)
				{
					if (!File.Exists(file))
                    {
						LogFile.WriteLine("WARNING: File verification failed, file does not exist: " + file);
						return false;
					}
				}
			}

			return true;
        }
    }
}
