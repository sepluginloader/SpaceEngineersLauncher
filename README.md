# Space Engineers Launcher
A launcher for Space Engineers version 1.202 and greater with built in Plugin Loader.

The launcher will download and keep Plugin Loader up to date automatically. If you want to use this launcher without Plugin Loader, you can open the configuration file in the `Bin64/Plugins/` folder and change the `NoUpdates` value to true.

## Installation

1. Download the Launcher zip file from the [Releases page](https://github.com/sepluginloader/SpaceEngineersLauncher/releases/latest) and extract the SpaceEngineersLauncher.exe file.
2. Make sure the exe file is not blocked before continuing by opening the file properties of the extracted file and checking the Unblock box if it exists. 
3. The extracted files can now be placed in the game Bin64 folder.
	- You can find the Bin64 folder by right clicking on Space Engineers and selecting Properties. Then under the Local Files tab, select Browse and navigate to the Bin64 folder. 
4. Launch Space Engineers by opening the SpaceEngineersLauncher.exe file. 
5. *(optional)* To start Space Engineers with Plugin Loader via Steam, you must add the launcher to the [game launch options](https://support.steampowered.com/kb_article.php?ref=1040-JWMT-2947) using the following format: `"YourBin64PathHere\SpaceEngineersLauncher.exe" %command%`
	- Example: `"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64\SpaceEngineersLauncher.exe" %command%`