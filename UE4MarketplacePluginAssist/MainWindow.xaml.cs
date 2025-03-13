/*
Copyright(c) 2020 Jared Taylor

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ookii.Dialogs.Wpf;
using System.Threading;
using System.ComponentModel;
using MessageBox = System.Windows.MessageBox;
using System.IO.Compression;

namespace UE4MarketplacePluginAssist
{
    public readonly struct WaitingProgress : IEquatable<WaitingProgress>
    {
        public readonly InProgress progress;
        public readonly string engineVersion;

        public WaitingProgress(InProgress progress, string engineVersion)
        {
            this.progress = progress;
            this.engineVersion = engineVersion;
        }

        public bool Equals(WaitingProgress other)
        {
            return Equals(progress, other.progress) && engineVersion == other.engineVersion;
        }

        public override bool Equals(object obj)
        {
            return obj is WaitingProgress other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((progress != null ? progress.GetHashCode() : 0) * 397) ^ (engineVersion != null ? engineVersion.GetHashCode() : 0);
            }
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /*
         * app version 1.2.0
         *      Support zipping binaries
         *      Support not zipping FilterPlugin.ini
         *      Deletes FilterPlugin.ini from source directory when done
         *      Code clean-up
         * app version 1.1.1
         *      Copy over FilterPlugin.ini
         * app version 1.1.0
         *      510 is now default engine version
         *      VS2022 is now default version
         *      Added ability to modify your .uproject engine version (the digits get split, 510➜5.1.0)
         *      Added option to resave any unversioned packages (-run=ResavePackages -OnlyUnversioned)
         *      Instead of packing directly to the given directory, will create subdirectory for each engine version
         *      Zip file now gets created in the output directory instead of the plugin directory
         *      Added check for output directory being cleared successfully
         *      Fixed bug where wrong directory was zipped (you will no longer get .git files)
         *      Fixed bug where data validation incorrectly allowed checkboxes to be enabled
         * app version 1.0.4
         *      error checking for zip functionality
         *      removed unnecessary 'using' statements
         *      modified variables to fit standards better
         *      modified variable access modifiers
         * app version 1.0.3
         *      fixed issues with versions
         * app version 1.0.2
         *      support for major engine versions above 4 (in preparation for UE5)
         *      made engine version change UX better
         * app version 1.0.1
         *      support for config.config with visual studio version setting
         * app version 1.0.0
         *      initial release
         */

        // defaults (these still need to be changed in GUI manually)
        public string defaultEngineVersion = "510";
        private readonly string vsDefaultVersion = "VS2022";

        // ini files
        private int _engineRootLine = -1;
        private int _pluginLine = -1;
        private int _outputLine = -1;
        private int _zipLine = -1;
        private int _zipFilterPluginLine = -1;
        private int _zipBinariesLine = -1;
        private int _pluginEngineLine = -1;
        private int _saveUnversionedLine = -1;

        // config file
        private const int VsVersionLine = 0;

        // cached vars
        private readonly bool _initialized = false;
        public string engineVersion = "";
        public string configFile = "";
        private readonly string _vsVersion = "";

        public BackgroundWorker bw;
        private Process _p;
        public Thread processThread;
        public CancellationTokenSource cts;
        public InProgress progress;

        private readonly List<WaitingProgress> _waitingProgress = new List<WaitingProgress>();

        public MainWindow()
        {
            InitializeComponent();

            InitBackgroundWorker();

            if (Directory.Exists(GetConfigDirectory()))
            {
                string[] iniFiles = Directory.GetFiles(GetConfigDirectory(), "*.ini");
                if (iniFiles.Length > 0)
                {
                    string firstIniFile = iniFiles.First();
                    if (File.Exists(firstIniFile))
                    {
                        // Remove the directory path
                        string fileVersion = firstIniFile.Split('\\').Last();
                        // Remove the extension
                        fileVersion = fileVersion.Split('.').First();

                        // Change to first found ini
                        ChangeEngineVersion(fileVersion);
                    }
                }
            }
            else
            {
                Directory.CreateDirectory(GetConfigDirectory());
            }

            // Global config file
            if (Directory.Exists(GetConfigDirectory()))
            {
                string[] configFiles = Directory.GetFiles(GetConfigDirectory(), "*.config");
                if (configFiles.Length > 0 && configFiles[0].EndsWith("config.config"))
                {
                    using (StreamReader sr = File.OpenText(configFiles[0]))
                    {
                        string s;
                        while ((s = sr.ReadLine()) != null)
                        {
                            if (s.StartsWith("visualstudio="))
                            {
                                _vsVersion = SplitString(s, "visualstudio=").Last();
                                Text_VSVersion.Text = _vsVersion;
                            }
                        }
                    }
                }
                else
                {
                    using (StreamWriter sw = File.CreateText(GetConfigDirectory() + "config.config"))
                    {
                        sw.WriteLine("visualstudio=" + vsDefaultVersion);
                    }
                }
            }
            else
            {
                // No write access?
                Environment.Exit(-2);
                return;
            }

            _initialized = true;

            Debug.Write(GenerateBatchCommand());
        }

        private string GetBaseDirectory()
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private string GetConfigDirectory()
        {
            return GetBaseDirectory() + "Config\\";
        }

        private string GetIniPath()
        {
            return GetConfigDirectory() + engineVersion + ".ini";
        }
        private string GetConfigFilePath()
        {
            return GetConfigDirectory() + "config.config";
        }

        public string GetLogPath()
        {
            return GetBaseDirectory() + "Log\\";
        }

        public string GetLogFile()
        {
            return GetLogPath() + engineVersion + ".log";
        }

        private string GetEngineRootPath()
        {
            return Text_EngineRoot.Text;
        }

        private string GetPluginPath()
        {
            return Text_PluginRoot.Text;
        }

        public string GetPluginDirectory()
        {
            string[] str = SplitString(GetPluginPath(), "\\");
            string s = "";
            for (int i = 0; i < str.Length - 1; i++)
            {
                s += str[i] + "\\";
            }
            return s;
        }

        public string GetPluginName()
        {
            string remExt = SplitString(GetPluginPath(), ".uplugin").First();
            return SplitString(remExt, "\\").Last();
        }

        private string GetOutputPath()
        {
            return Text_Output.Text;
        }

        public string GetPackagedPath()
        {
            return GetOutputPath() + "\\" + engineVersion;
        }

        public string GetZipPath()
        {
            return GetPackagedPath();
        }

        public static string[] SplitString(string s, string separator)
        {
            return s.Split(new string[] { separator }, StringSplitOptions.None);
        }

        private string GenerateBatchCommand()
        {
            return "\"" + GetEngineRootPath() + "\\Engine\\Build\\BatchFiles\\RunUAT.bat\"" + " BuildPlugin " + "-Plugin=\"" + GetPluginPath() + "\" " + "-Package=\"" + GetOutputPath() + "\\" + engineVersion + "\"" + " -" + _vsVersion + " -Rocket";
        }

        public string GenerateSaveUnversionedCommand()
        {
            return "\"" + GetEngineRootPath() + "\\Engine\\Binaries\\Win64\\UnrealEditor-Cmd.exe\" \"" + GetPluginPath() + GetPluginName() + "\" -run=ResavePackages -OnlyUnversioned";
        }

        private void Browse_EngineRoot_Click(object sender, RoutedEventArgs e)
        {
            // Select engine root folder
            VistaFolderBrowserDialog folderBrowserDialog = new VistaFolderBrowserDialog();
            bool? result = folderBrowserDialog.ShowDialog(this);
            if (result.HasValue && result.Value == true)
            {
                // Verify that this is the correct folder
                if (IsEngineValid(folderBrowserDialog.SelectedPath))
                {
                    Text_EngineRoot.Text = folderBrowserDialog.SelectedPath;
                }
                else
                {
                    MessageBox.Show(this, "Invalid directory, should contain \"Engine\" folder");
                }
            }
        }

        private void Browse_Output_Click(object sender, RoutedEventArgs e)
        {
            // Select output folder
            VistaFolderBrowserDialog folderBrowserDialog = new VistaFolderBrowserDialog();
            bool? result = folderBrowserDialog.ShowDialog(this);
            if (result.HasValue && result.Value == true)
            {
                // Verify that this is the correct folder
                if (IsOutputValid(folderBrowserDialog.SelectedPath))
                {
                    Text_Output.Text = folderBrowserDialog.SelectedPath;
                }
                else
                {
                    MessageBox.Show(this, "Invalid directory");
                }
            }
        }

        private void Browse_PluginRoot_Click(object sender, RoutedEventArgs e)
        {
            // Select plugin file
            VistaOpenFileDialog openFileDialog = new VistaOpenFileDialog();
            openFileDialog.DefaultExt = ".uplugin";
            openFileDialog.Filter = "*.uplugin|*.uplugin";
            bool? result = openFileDialog.ShowDialog(this);
            if (result.HasValue && result.Value == true)
            {
                Debug.Print(openFileDialog.FileName);
                // Verify that this is the correct folder
                if (IsPluginValid(openFileDialog.FileName))
                {
                    Text_PluginRoot.Text = openFileDialog.FileName;
                }
                else
                {
                    MessageBox.Show(this, "Invalid file");
                }
            }
        }

        private void Button_AddEngineVersion_Click(object sender, RoutedEventArgs e)
        {
            ChangeEngineVersionWindow engineVersionWindow = new ChangeEngineVersionWindow();
            engineVersionWindow.mainWindow = this;

            // Current engine version
            if (engineVersion.Length > 0)
            {
                engineVersionWindow.Text_EngineVersion.Text = engineVersion;
            }

            engineVersionWindow.ShowDialog();
        }

        private static bool IsEngineValid(string enginePath)
        {
            return Directory.Exists(enginePath + "\\Engine");
        }

        private static bool IsOutputValid(string outputPath)
        {
            return Directory.Exists(outputPath);
        }

        private static bool IsPluginValid(string pluginPath)
        {
            return (File.Exists(pluginPath));
        }

        private void ValidateDirectories()
        {
            // Ensure all directories are valid
            bool bValidEngine = Directory.Exists(GetEngineRootPath());
            bool bValidPlugin = File.Exists(GetPluginPath());
            bool bValidOutput = Directory.Exists(GetOutputPath());
            bool bValidSetup = bValidEngine && bValidPlugin && bValidOutput;

            // Only enable start button if all directories are valid
            Button_Start.IsEnabled = bValidSetup;
            Button_StartAll.IsEnabled = bValidSetup;

            // Show warning if output directory is not empty
            if (bValidOutput)
            {
                bool bOutputEmpty = Directory.GetDirectories(GetOutputPath()).Length == 0 || Directory.GetFiles(GetOutputPath()).Length == 0;
                Text_OutputWarning.Visibility = bOutputEmpty ? Visibility.Hidden : Visibility.Visible;
            }
        }

        public void ChangeEngineVersion(string newVersion, bool bRecursionTest = false)
        {
            if (newVersion == engineVersion)
            {
                // No change
                return;
            }

            string baseDirectory = GetBaseDirectory();
            string directory = GetConfigDirectory();

            if (!Directory.Exists(baseDirectory))
            {
                throw new Exception("Base Directory does not exist");
            }

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);

                if (!Directory.Exists(directory))
                {
                    throw new Exception("Could not create config directory");
                }
            }

            string filePath = directory + newVersion + ".ini";

            // Create the .ini file if it doesn't exist already
            if (!File.Exists(filePath))
            {
                using (StreamWriter sw = File.CreateText(filePath))
                {
                    sw.WriteLine("engineroot=");
                    sw.WriteLine("plugin=");
                    sw.WriteLine("output=");
                    sw.WriteLine("zip=True");
                    sw.WriteLine("zipFilterPlugin=True");
                    sw.WriteLine("zipBinaries=False");
                    sw.WriteLine("upluginengine=False");
                    sw.WriteLine("saveunversioned=False");
                }
            }

            if (!File.Exists(filePath))
            {
                throw new Exception("Could not create file:" + filePath);
            }

            LoadEngineVersion(newVersion, bRecursionTest);
        }

        private void LoadEngineVersion(string newVersion, bool bRecursionTest)
        {
            string directory = GetConfigDirectory();
            string filePath = directory + newVersion + ".ini";

            engineVersion = newVersion;

            if (!File.Exists(filePath))
            {
                throw new Exception("Could not locate file:" + filePath);
            }

            string engineRoot = "";
            string plugin = "";
            string output = "";
            string zip = "";
            string zipFilterPlugin = "";
            string zipBinaries = "";
            string upluginengine = "";
            string saveunversioned = "";
            bool bEngineRootFound = false;
            bool bPluginFound = false;
            bool bOutputFound = false;
            bool bZipFound = false;
            bool bZipFilterPluginFound = false;
            bool bZipBinariesFound = false;
            bool bUpluginEngineFound = false;
            bool bSaveUnversionedFound = false;

            // Find corresponding paths in .ini file
            using (StreamReader sr = File.OpenText(filePath))
            {
                int line = 0;
                string s;
                while((s = sr.ReadLine()) != null)
                {
                    if (s.StartsWith("engineroot="))
                    {
                        engineRoot = SplitString(s, "engineroot=").Last();
                        _engineRootLine = line;
                        bEngineRootFound = true;
                    }
                    else if (s.StartsWith("plugin="))
                    {
                        plugin = SplitString(s, "plugin=").Last();
                        _pluginLine = line;
                        bPluginFound = true;
                    }
                    else if (s.StartsWith("output="))
                    {
                        output = SplitString(s, "output=").Last();
                        _outputLine = line;
                        bOutputFound = true;
                    }
                    else if (s.StartsWith("zip="))
                    {
                        zip = SplitString(s, "zip=").Last();
                        _zipLine = line;
                        bZipFound = true;
                    }
                    else if (s.StartsWith("zipFilterPlugin="))
                    {
                        zipFilterPlugin = SplitString(s, "zipFilterPlugin=").Last();
                        _zipFilterPluginLine = line;
                        bZipFilterPluginFound = true;
                    }
                    else if (s.StartsWith("zipBinaries="))
                    {
                        zipBinaries = SplitString(s, "zipBinaries=").Last();
                        _zipBinariesLine = line;
                        bZipBinariesFound = true;
                    }
                    else if (s.StartsWith("upluginengine="))
                    {
                        upluginengine = SplitString(s, "upluginengine=").Last();
                        _pluginEngineLine = line;
                        bUpluginEngineFound = true;
                    }
                    else if (s.StartsWith("saveunversioned="))
                    {
                        saveunversioned = SplitString(s, "saveunversioned=").Last();
                        _saveUnversionedLine = line;
                        bSaveUnversionedFound = true;
                    }
                    line++;
                }
            }

            // Verify both paths were found, otherwise remake the file (only once; see bRecursionTest)
            if (!bEngineRootFound || !bPluginFound || !bOutputFound || !bZipFound || !bZipFilterPluginFound || !bZipBinariesFound || !bUpluginEngineFound || !bSaveUnversionedFound)
            {
                if (bRecursionTest)
                {
                    throw new Exception("File was created with corrupt data and did not create or load properly: " + filePath);
                }
                else
                {
                    File.Delete(filePath);
                    ChangeEngineVersion(newVersion, true);
                    return;
                }
            }

            // Load and enable engine root
            Text_EngineRoot.IsEnabled = true;
            Browse_EngineRoot.IsEnabled = true;
            Text_EngineRoot.Text = engineRoot;

            // Load and enable plugin
            Text_PluginRoot.IsEnabled = true;
            Browse_PluginRoot.IsEnabled = true;
            Text_PluginRoot.Text = plugin;

            // Load and enable output
            Text_Output.IsEnabled = true;
            Browse_Output.IsEnabled = true;
            Text_Output.Text = output;

            // Load and set zip
            Check_Zip.IsEnabled = true;
            Check_Zip.IsChecked = zip.Equals("True");
            
            // Load and set zip filter plugin
            ZipFilterPlugin.IsEnabled = true;
            ZipFilterPlugin.IsChecked = zipFilterPlugin.Equals("True");
            
            // Load and set zip binaries
            ZipBinaries.IsEnabled = true;
            ZipBinaries.IsChecked = zipBinaries.Equals("True");

            // Load and set uplugin engine version
            ChangeUPlugin.IsEnabled = true;
            ChangeUPlugin.IsChecked = upluginengine.Equals("True");

            // Load and set save unversioned
            SavedUnversioned.IsEnabled = true;
            SavedUnversioned.IsChecked = saveunversioned.Equals("True");

            ValidateDirectories();
        }

        public static void ChangeTextLine(string newText, string fileName, int lineToEdit)
        {
            string[] arrLine = File.ReadAllLines(fileName);
            arrLine[lineToEdit] = newText;
            File.WriteAllLines(fileName, arrLine);
        }

        private void Text_VSVersion_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_initialized && File.Exists(GetConfigFilePath()))
            {
                ChangeTextLine("visualstudio=" + Text_VSVersion.Text, GetConfigFilePath(), VsVersionLine);
            }
        }

        private void Text_EngineRoot_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_engineRootLine < 0) 
            {
                throw new Exception("EngineRootLine not initialized");
            }

            ChangeTextLine("engineroot=" + Text_EngineRoot.Text, GetIniPath(), _engineRootLine);

            ValidateDirectories();
        }

        private void Text_PluginRoot_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_pluginLine < 0)
            {
                throw new Exception("PluginLine not initialized");
            }

            ChangeTextLine("plugin=" + Text_PluginRoot.Text, GetIniPath(), _pluginLine);

            ValidateDirectories();
        }

        private void Text_Output_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_outputLine < 0)
            {
                throw new Exception("OutputLine not initialized");
            }

            ChangeTextLine("output=" + Text_Output.Text, GetIniPath(), _outputLine);

            ValidateDirectories();
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
            {
                return;
            }

            if (_zipLine < 0)
            {
                throw new Exception("ZipLine not initialized");
            }

            ChangeTextLine("zip=" + Check_Zip.IsChecked, GetIniPath(), _zipLine);
        }
        
        private void ZipFilterPlugin_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
            {
                return;
            }

            if (_zipFilterPluginLine < 0)
            {
                throw new Exception("ZipFilterPluginLine not initialized");
            }

            ChangeTextLine("zipFilterPlugin=" + ZipFilterPlugin.IsChecked, GetIniPath(), _zipFilterPluginLine);
        }
        
        private void ZipBinaries_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
            {
                return;
            }
            
            if (_zipBinariesLine < 0)
            {
                throw new Exception("ZipBinariesLine not initialized");
            }
            
            ChangeTextLine("zipBinaries=" + ZipBinaries.IsChecked, GetIniPath(), _zipBinariesLine);
        }
        
        private void ChangeUPlugin_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
            {
                return;
            }

            if (_pluginEngineLine < 0)
            {
                throw new Exception("UPluginEngine not initialized");
            }

            ChangeTextLine("upluginengine=" + ChangeUPlugin.IsChecked, GetIniPath(), _pluginEngineLine);
        }

        private void SavedUnversioned_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
            {
                return;
            }

            if (_saveUnversionedLine < 0)
            {
                throw new Exception("SaveUnversioned not initialized");
            }

            ChangeTextLine("saveunversioned=" + SavedUnversioned.IsChecked.ToString(), GetIniPath(), _saveUnversionedLine);
        }

        private bool NukeOutputDirectory()
        {
            DirectoryInfo directory = new DirectoryInfo(GetOutputPath());

            foreach (FileInfo file in directory.GetFiles())
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex)
                {
                    var mBoxError = "File " + file + " could not be deleted. Consider deleting it manually. " + ex.Message;
                    MessageBox.Show(this, mBoxError);
                    return false;
                }
            }
            foreach (DirectoryInfo dir in directory.GetDirectories())
            {
                try
                {
                    dir.Delete(true);
                }
                catch (Exception ex)
                {
                    var mBoxError = "Directory " + dir + " could not be deleted. Consider deleting it manually. " + ex.Message;
                    MessageBox.Show(this, mBoxError);
                    return false;
                }
            }

            return true;
        }

        public void Check_Waiting_Progress()
        {
            if (!bw.IsBusy)
            {
                // Remove completed progress
                if (progress != null && _waitingProgress.Count > 0)
                {
                    WaitingProgress pr = _waitingProgress[0];
                    bool bFoundRemoval = false;
                    foreach (var t in _waitingProgress)
                    {
                        pr = t;
                        if (progress == pr.progress)
                        {
                            bFoundRemoval = true;
                            break;
                        }
                    }

                    if (bFoundRemoval)
                    {
                        _waitingProgress.Remove(pr);
                    }
                }

                // Begin next progress
                if (_waitingProgress.Count > 0)
                {
                    progress = _waitingProgress.First().progress;
                    progress.Show();
                    Hide();

                    LoadEngineVersion(_waitingProgress.First().engineVersion, false);

                    FireUpBackgroundWorker();
                }
            }
        }

        private void Button_StartAll_Click(object sender, RoutedEventArgs e)
        {
            _waitingProgress.Clear();

            if (!NukeOutputDirectory())
            {
                return;
            }

            // Find each valid ini
            if (Directory.Exists(GetConfigDirectory()))
            {
                string[] iniFiles = Directory.GetFiles(GetConfigDirectory(), "*.ini");

                foreach (string i in iniFiles)
                {
                    if (File.Exists(i))
                    {
                        // Remove the directory path
                        string fileVersion = i.Split('\\').Last();
                        // Remove the extension
                        fileVersion = fileVersion.Split('.').First();

                        // Change to next ini
                        LoadEngineVersion(fileVersion, false);

                        if (Button_Start.IsEnabled)
                        {
                            // Valid ini, create progress window
                            // Show progress window
                            InProgress progress = new InProgress();
                            progress.mainWindow = this;
                            progress.Text_Plugin.Text = GetPluginName();
                            progress.Text_Version.Text = engineVersion;
                            progress.bZip = Check_Zip.IsChecked.Value;
                            progress.bUProjectEngineVersion = ChangeUPlugin.IsChecked.Value;
                            progress.bSaveUnversioned = SavedUnversioned.IsChecked.Value;

                            _waitingProgress.Add(new WaitingProgress(progress, fileVersion));
                        }
                    }
                }
            }

            Check_Waiting_Progress();
        }

        private void FireUpBackgroundWorker()
        {
            // Fire up background worker
            string cmd = "/C " + "\"" + GenerateBatchCommand() + "\"";

            if (!bw.IsBusy)
            {
                bw.RunWorkerAsync(cmd);
                bw.Dispose();
            }
        }

        private void Button_Start_Click(object sender, RoutedEventArgs e)
        {
            if (!NukeOutputDirectory())
            {
                return;
            }

            // Show progress window
            progress = new InProgress();
            progress.mainWindow = this;
            progress.Text_Plugin.Text = GetPluginName();
            progress.Text_Version.Text = engineVersion;
            progress.bZip = Check_Zip.IsChecked != null && Check_Zip.IsChecked.Value;
            progress.bZipFilterPlugin = ZipFilterPlugin.IsChecked != null && ZipFilterPlugin.IsChecked.Value;
            progress.bZipBinaries = ZipBinaries.IsChecked != null && ZipBinaries.IsChecked.Value;
            progress.bUProjectEngineVersion = ChangeUPlugin.IsChecked != null && ChangeUPlugin.IsChecked.Value;
            progress.bSaveUnversioned = SavedUnversioned.IsChecked != null && SavedUnversioned.IsChecked.Value;

            progress.Show();
            Hide();

            FireUpBackgroundWorker();
        }

        // Background Worker
        private void InitBackgroundWorker()
        {
            bw = new BackgroundWorker();

            bw.WorkerReportsProgress = false;
            bw.WorkerSupportsCancellation = true;

            bw.DoWork += new DoWorkEventHandler(BW_DoWork);
            bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(BW_RunWorkerCompleted);
        }

        public void CancelBackgroundWorker()
        {
            bw.CancelAsync();

            if (!_p.HasExited)
            {
                _p.CloseMainWindow();
            }
        }

        private void BW_DoWork(object sender, DoWorkEventArgs e)
        {
            // Start child process
            _p = new Process();

            // Redirect output stream
            _p.StartInfo.UseShellExecute = false;
            _p.StartInfo.RedirectStandardOutput = true;
            _p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            _p.StartInfo.FileName = "cmd.exe";
            _p.StartInfo.Arguments = e.Argument.ToString();
            _p.Start();

            e.Result = _p.StandardOutput.ReadToEnd();

            _p.WaitForExit();
        }

        private void BW_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (!_p.HasExited)
            {
                _p.CloseMainWindow();
            }

            string result = e.Result.ToString();
            if(progress != null && progress.IsVisible)
            {
                progress.Completed(result);
            }
        }

        private void Button_About_Click(object sender, RoutedEventArgs e)
        {
            About about = new About();
            about.ShowDialog();
        }
    }
}

