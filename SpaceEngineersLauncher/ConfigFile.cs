using System;
using System.IO;
using System.Xml.Serialization;

namespace avaness.SpaceEngineersLauncher
{
    [XmlRoot("LauncherConfig")]
    public class ConfigFile
    {
        private string filePath;

        public string LoaderVersion { get; set; }

        public bool NoUpdates { get; set; }

        [XmlArrayItem("File")]
        public string[] Files { get; set; }

        private int networkTimeout = 10000;
        public int NetworkTimeout
        {
            get
            {
                return networkTimeout;
            }
            set
            {
                if (value < 100)
                    networkTimeout = 100;
                else if (value > 60000)
                    networkTimeout = 60000;
                else
                    networkTimeout = value;
            }
        }

        public bool AllowIPv6 { get; set; } = true;


        public ConfigFile()
        {

        }

        public static ConfigFile Load(string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(ConfigFile));
                    FileStream fs = File.OpenRead(filePath);
                    ConfigFile config = (ConfigFile)serializer.Deserialize(fs);
                    fs.Close();
                    config.filePath = filePath;
                    return config;
                }
                catch (Exception e)
                {
                    LogFile.WriteLine($"An error occurred while loading launcher config: " + e);
                }
            }

            return new ConfigFile
            {
                filePath = filePath
            };
        }

        public void Save()
        {
            try
            {
                LogFile.WriteLine("Saving config");
                XmlSerializer serializer = new XmlSerializer(typeof(ConfigFile));
                if (File.Exists(filePath))
                    File.Delete(filePath);
                FileStream fs = File.OpenWrite(filePath);
                serializer.Serialize(fs, this);
                fs.Flush();
                fs.Close();
            }
            catch (Exception e)
            {
                LogFile.WriteLine($"An error occurred while saving launcher config: " + e);
            }
        }
    }
}
