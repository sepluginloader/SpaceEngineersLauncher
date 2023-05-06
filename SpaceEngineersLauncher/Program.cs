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
using System.Security.Cryptography;

namespace avaness.SpaceEngineersLauncher
{
    static class Program
    {
		private const uint AppId = 244850u;
		private const string RepoUrl = "https://github.com/sepluginloader/PluginLoader/";
		private const string RepoDownloadSuffix = "releases/download/{0}/PluginLoader-{0}.zip";
		private static readonly Regex VersionRegex = new Regex(@"^v(\d+\.)*\d+$");
		private const string PluginLoaderFile = "PluginLoader.dll";
		private const string OriginalAssemblyFile = "SpaceEngineers.exe";
		private const string ProgramGuid = "03f85883-4990-4d47-968e-5e4fc5d72437";
		private static readonly Version SupportedGameVersion = new Version(1, 202, 0);
		private const int MutexTimeout = 1000; // ms
		private const int SteamTimeout = 30; // seconds

		private static string exeLocation;
		private static SplashScreen splash;
		private static Mutex mutex; // For ensuring only a single instance of SE
		private static bool mutexActive;

		static void Main(string[] args)
		{
			if (IsReport(args))
            {
				StartSpaceEngineers(args);
				return;
			}

			exeLocation = Path.GetDirectoryName(Path.GetFullPath(Assembly.GetExecutingAssembly().Location));

			if (!IsSingleInstance())
			{
				Show("Error: Space Engineers is already running!");
				return;
			}

			if (!IsSupportedGameVersion())
			{
				Show("Game version not supported! Requires " + SupportedGameVersion.ToString(3) + " or later");
				return;
			}

			try
			{
				StartPluginLoader(args);
				StartSpaceEngineers(args);
				Close();
			}
			finally
            {
				if (mutexActive)
					mutex.ReleaseMutex();
			}
		}

		private static void StartPluginLoader(string[] args)
        {
			splash = new SplashScreen("avaness.SpaceEngineersLauncher");

			try
			{
				string pluginsDir = Path.Combine(exeLocation, "Plugins");
				if(!Directory.Exists(pluginsDir))
					Directory.CreateDirectory(pluginsDir);

				LogFile.Init(Path.Combine(pluginsDir, "launcher.log"));
				LogFile.WriteLine("Starting - v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3));

				ConfigFile config = ConfigFile.Load(Path.Combine(pluginsDir, "launcher.xml"));

				// Fix tls 1.2 not supported on Windows 7 - github.com is tls 1.2 only
				try
				{
					ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
				}
				catch (NotSupportedException e)
				{
					LogFile.WriteLine("An error occurred while setting up networking, web requests will probably fail: " + e);
				}

				string appIdFile = Path.Combine(exeLocation, "steam_appid.txt");
				if (!File.Exists(appIdFile))
				{
					LogFile.WriteLine(appIdFile + " does not exist, creating.");
					File.WriteAllText(appIdFile, AppId.ToString());
				}

				StartSteam();

				EnsureAssemblyConfigFile();

				if (!config.NoUpdates)
					Update(config);

				StringBuilder pluginLog = new StringBuilder("Loading plugins: ");
				List<string> plugins = new List<string>();

				splash.SetText("Registering plugins...");

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
							string plugin = args[i];
							if (plugin.EndsWith("PluginLoader.dll", StringComparison.OrdinalIgnoreCase))
								continue;
							if (File.Exists(plugin))
							{
                                pluginLog.Append(plugin).Append(',');
								plugins.Add(plugin);
							}
							else
							{
								LogFile.WriteLine("WARNING: '" + plugin + "' does not exist.");
							}
						}
					}
				}

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
				LogFile.WriteLine("Error while getting Plugin Loader ready: " + e);
				Show("Plugin Loader crashed: " + e);
				if (Application.OpenForms.Count > 0)
                    Application.OpenForms[0].Close();
			}

			MyCommonProgramStartup.BeforeSplashScreenInit += Close;
		}

        private static void StartSteam()
		{
			if(!Steamworks.SteamAPI.IsSteamRunning())
			{
				splash.SetText("Starting steam...");
				try
				{
					Process steam = Process.Start("steam://");
					if(steam != null)
                    {
						for (int i = 0; i < SteamTimeout; i++)
						{
							Thread.Sleep(1000);
							if (Steamworks.SteamAPI.Init())
								return;
						}
					}
				}
				catch { }

				LogFile.WriteLine("Steam not detected!");
				Show("Steam must be running before you can start Space Engineers.");
				splash.Delete();
				Environment.Exit(0);
			}

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
			mutex = new Mutex(true, ProgramGuid, out mutexActive);
			if (!mutexActive)
            {
				try
				{
					mutexActive = mutex.WaitOne(MutexTimeout);
					if(!mutexActive)
						return false;
				}
				catch (AbandonedMutexException)
				{ } // Abandoned probably means that the process was killed or crashed
			}

			// Check for other SpaceEngineers.exe
			string sePath = Path.Combine(exeLocation, OriginalAssemblyFile);
			if (Process.GetProcessesByName("SpaceEngineers").Any(x => x.MainModule.FileName.Equals(sePath, StringComparison.OrdinalIgnoreCase)))
				return false;

			return true;
        }

        private static void EnsureAssemblyConfigFile()
        {
			// Without this file, SE will have many bugs because its dependencies will not be correct.
			string originalConfig = Path.Combine(exeLocation, OriginalAssemblyFile + ".config");
			string newConfig = Path.Combine(exeLocation, Path.GetFileName(Assembly.GetExecutingAssembly().Location) + ".config");
			if (File.Exists(originalConfig))
            {
				if (!File.Exists(newConfig) || !FilesEqual(originalConfig, newConfig))
                {
					File.Copy(originalConfig, newConfig, true);
					Restart();
				}
			}
			else if(File.Exists(newConfig))
            {
				File.Delete(newConfig);
				Restart();
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
				currentVersion = config.LoaderVersion;
				LogFile.WriteLine("Plugin Loader " + currentVersion);
			}
			else
			{
				LogFile.WriteLine("Plugin Loader version unknown");
			}

			if (!IsLatestVersion(config, currentVersion, out string latestVersion))
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

				DialogResult result = Show(prompt.ToString(), MessageBoxButtons.YesNoCancel);
				if (result == DialogResult.Yes)
				{
					splash.SetText("Downloading update...");
					if (!TryDownloadUpdate(config, latestVersion))
						Show("Update failed!");
				}
				else if (result == DialogResult.Cancel)
				{
					splash.Delete();
					Environment.Exit(0);
				}
			}
		}

		static bool IsLatestVersion(ConfigFile config, string currentVersion, out string latestVersion)
        {
			try
			{
				Uri uri = new Uri(RepoUrl + "releases/latest", UriKind.Absolute);
				HttpWebResponse response = Download(config, uri);
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
				HttpWebResponse response = Download(config, uri);
				using (Stream zipFileStream = response.GetResponseStream())
				using (ZipArchive zipFile = new ZipArchive(zipFileStream))
				{
					foreach (ZipArchiveEntry entry in zipFile.Entries)
					{
						string fileName = Path.GetFileName(entry.FullName);
						string filePath = Path.Combine(exeLocation, fileName);

						using (Stream entryStream = entry.Open())
						using (FileStream entryFile = File.Create(filePath))
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

		static HttpWebResponse Download(ConfigFile config, Uri uri)
		{
			LogFile.WriteLine("Downloading " + uri);
			HttpWebRequest request = WebRequest.CreateHttp(uri);
			request.Timeout = config.NetworkTimeout;
			request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
			if (!config.AllowIPv6)
				request.ServicePoint.BindIPEndPointDelegate = BlockIPv6;
			return request.GetResponse() as HttpWebResponse;
		}

		private static IPEndPoint BlockIPv6(ServicePoint servicePoint, IPEndPoint remoteEndPoint, int retryCount)
		{
			if (remoteEndPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
				return new IPEndPoint(IPAddress.Any, 0);

			throw new InvalidOperationException("No IPv4 address");
		}

		static bool CanUseLoader(ConfigFile config)
        {
			if (!File.Exists(Path.Combine(exeLocation, PluginLoaderFile)))
            {
				LogFile.WriteLine("WARNING: File verification failed, file does not exist: " + PluginLoaderFile);
				return false;
			}

			if (config.Files != null)
			{
				foreach (string file in config.Files)
				{
					if (!File.Exists(Path.Combine(exeLocation, file)))
                    {
						LogFile.WriteLine("WARNING: File verification failed, file does not exist: " + file);
						return false;
					}
				}
			}

			return true;
        }

		static DialogResult Show(string msg, MessageBoxButtons buttons = MessageBoxButtons.OK)
        {
			if (Application.OpenForms.Count > 0)
                return MessageBox.Show(Application.OpenForms[0], msg, "Space Engineers Launcher", buttons);
			return MessageBox.Show(msg, "Space Engineers Launcher", buttons);
		}

		static void Restart()
        {
			Close();
			Application.Restart();
			Process.GetCurrentProcess().Kill();
		}

		static bool FilesEqual(string file1, string file2)
        {
			FileInfo fileInfo1 = new FileInfo(file1);
			FileInfo fileInfo2 = new FileInfo(file2);
			return fileInfo1.Length == fileInfo2.Length && GetHash256(file1) == GetHash256(file2);
        }

		static string GetHash256(string file)
		{
			using (SHA256CryptoServiceProvider sha = new SHA256CryptoServiceProvider())
			{
				return GetHash(file, sha);
			}
		}

		static string GetHash(string file, HashAlgorithm hash)
		{
			using (FileStream fileStream = new FileStream(file, FileMode.Open))
			{
				using (BufferedStream bufferedStream = new BufferedStream(fileStream))
				{
					byte[] data = hash.ComputeHash(bufferedStream);
					StringBuilder sb = new StringBuilder(2 * data.Length);
					foreach (byte b in data)
						sb.AppendFormat("{0:x2}", b);
					return sb.ToString();
				}
			}
		}
	}
}
