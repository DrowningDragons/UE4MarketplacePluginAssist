/**
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
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using System.Threading;
using System.ComponentModel;

namespace UE4MarketplacePluginAssist
{
    public struct WaitingProgress
    {
        public InProgress Progress;
        public string EngineVersion;

        public WaitingProgress(InProgress progress, string engineVersion)
        {
            Progress = progress;
            EngineVersion = engineVersion;
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /*
         * app version 1.0.2
         *      support for major engine versions above 4 (in preparation for UE5)
         *      made engine version change UX better
         * app version 1.0.1
         *      support for config.config with visual studio version setting
         * app version 1.0.0
         *      initial release
         */

        // ini files
        public int engineRootLine = -1;
        public int pluginLine = -1;
        public int outputLine = -1;
        public int zipLine = -1;

        // config file
        public int vsVersionLine = 0;

        // cached vars
        public bool initialized = false;
        public string engineVersion = "";
        public string configFile = "";
        public string vsVersion = "VS2019";

        public BackgroundWorker bw;
        private Process p;
        public Thread processThread;
        public CancellationTokenSource cts;
        public InProgress progress;

        public List<WaitingProgress> waiting_progress = new List<WaitingProgress>();

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
                        int line = 0;
                        string s = "";
                        while ((s = sr.ReadLine()) != null)
                        {
                            if (s.StartsWith("visualstudio="))
                            {
                                vsVersion = SplitString(s, "visualstudio=").Last();
                                Text_VSVersion.Text = vsVersion;
                            }
                            line++;
                        }
                    }
                }
                else
                {
                    using (StreamWriter sw = File.CreateText(GetConfigDirectory() + "config.config"))
                    {
                        sw.WriteLine("visualstudio=VS2019");
                    }
                }
            }
            else
            {
                // No write access?
                System.Environment.Exit(-2);
                return;
            }

            initialized = true;

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

        private string GetVisualStudioVersion()
        {
            return Text_VSVersion.Text;
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

        private string[] SplitString(string s, string separator)
        {
            return s.Split(new string[] { separator }, StringSplitOptions.None);
        }
        private string GetDirectoryFromFileName(string fileName)
        {
            // Remove the directory path
            string file = fileName.Split('\\').Last();
            return SplitString(fileName, file).First();
        }

        private string GenerateBatchCommand()
        {
            return "\"" + GetEngineRootPath() + "\\Engine\\Build\\BatchFiles\\RunUAT.bat\"" + " BuildPlugin " + "-Plugin=\"" + GetPluginPath() + "\" " + "-Package=\"" + GetOutputPath() + "\"" + " -" + vsVersion + " -Rocket";
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
                    System.Windows.MessageBox.Show(this, "Invalid directory, should contain \"Engine\" folder");
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
                    System.Windows.MessageBox.Show(this, "Invalid directory");
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
                    System.Windows.MessageBox.Show(this, "Invalid file");
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

        private bool IsEngineValid(string enginePath)
        {
            return Directory.Exists(enginePath + "\\Engine");
        }

        private bool IsOutputValid(string outputPath)
        {
            return Directory.Exists(outputPath);
        }

        private bool IsPluginValid(string pluginPath)
        {
            return (File.Exists(pluginPath));
        }

        private void ValidateDirectories()
        {
            // Ensure all directories are valid
            bool bValidEngine = Directory.Exists(GetEngineRootPath());
            bool bValidPlugin = File.Exists(GetPluginPath());
            bool bValidOutput = Directory.Exists(GetOutputPath());

            // Only enable start button if all directories are valid
            Button_Start.IsEnabled = bValidEngine && bValidPlugin && bValidOutput;
            Button_StartAll.IsEnabled = Button_Start.IsEnabled;

            // Show warning if output directory is not empty
            if (bValidOutput)
            {
                bool bOutputEmpty = Directory.GetDirectories(GetOutputPath()).Length == 0 || Directory.GetFiles(GetOutputPath()).Length == 0;
                if (bOutputEmpty)
                {
                    Text_OutputWarning.Visibility = Visibility.Hidden;
                }
                else
                {
                    Text_OutputWarning.Visibility = Visibility.Visible;
                }
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
            bool bEngineRootFound = false;
            bool bPluginFound = false;
            bool bOutputFound = false;
            bool bZipFound = false;

            // Find corresponding paths in .ini file
            using (StreamReader sr = File.OpenText(filePath))
            {
                int line = 0;
                string s = "";
                while((s = sr.ReadLine()) != null)
                {
                    if (s.StartsWith("engineroot="))
                    {
                        engineRoot = SplitString(s, "engineroot=").Last();
                        engineRootLine = line;
                        bEngineRootFound = true;
                    }
                    else if (s.StartsWith("plugin="))
                    {
                        plugin = SplitString(s, "plugin=").Last();
                        pluginLine = line;
                        bPluginFound = true;
                    }
                    else if (s.StartsWith("output="))
                    {
                        output = SplitString(s, "output=").Last();
                        outputLine = line;
                        bOutputFound = true;
                    }
                    else if (s.StartsWith("zip="))
                    {
                        zip = SplitString(s, "zip=").Last();
                        zipLine = line;
                        bZipFound = true;
                    }
                    line++;
                }
            }

            // Verify both paths were found, otherwise remake the file (only once; see bRecursionTest)
            if (!bEngineRootFound || !bPluginFound || !bOutputFound || !bZipFound)
            {
                if (bRecursionTest)
                {
                    throw new Exception("File was created with corrupt data and did not create or load properly: " + filePath);
                }
                else
                {
                    File.Delete(filePath);
                    ChangeEngineVersion(newVersion, true);
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
            Check_Zip.IsChecked = zip.Equals("True") ? true : false;

            ValidateDirectories();
        }

        static void changeTextLine(string newText, string fileName, int line_to_edit)
        {
            string[] arrLine = File.ReadAllLines(fileName);
            arrLine[line_to_edit] = newText;
            File.WriteAllLines(fileName, arrLine);
        }

        private void Text_VSVersion_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (initialized && File.Exists(GetConfigFilePath()))
            {
                changeTextLine("visualstudio=" + Text_VSVersion.Text, GetConfigFilePath(), vsVersionLine);
            }
        }

        private void Text_EngineRoot_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (engineRootLine < 0) 
            {
                throw new Exception("EngineRootLine not initialized");
            }

            changeTextLine("engineroot=" + Text_EngineRoot.Text, GetIniPath(), engineRootLine);

            ValidateDirectories();
        }

        private void Text_PluginRoot_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (pluginLine < 0)
            {
                throw new Exception("PluginLine not initialized");
            }

            changeTextLine("plugin=" + Text_PluginRoot.Text, GetIniPath(), pluginLine);

            ValidateDirectories();
        }

        private void Text_Output_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (outputLine < 0)
            {
                throw new Exception("OutputLine not initialized");
            }

            changeTextLine("output=" + Text_Output.Text, GetIniPath(), outputLine);

            ValidateDirectories();
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
            {
                return;
            }

            if (zipLine < 0)
            {
                throw new Exception("ZipLine not initialized");
            }

            changeTextLine("zip=" + Check_Zip.IsChecked.ToString(), GetIniPath(), zipLine);
        }

        public void Check_Waiting_Progress()
        {
            if (!bw.IsBusy)
            {
                // Remove completed progress
                if (progress != null && waiting_progress.Count > 0)
                {
                    WaitingProgress pr = waiting_progress[0];
                    bool bFoundRemoval = false;
                    for (int i = 0; i < waiting_progress.Count; i++)
                    {
                        pr = waiting_progress[i];
                        if (progress == pr.Progress)
                        {
                            bFoundRemoval = true;
                            break;
                        }
                    }

                    if (bFoundRemoval)
                    {
                        waiting_progress.Remove(pr);
                    }
                }

                // Begin next progress
                if (waiting_progress.Count > 0)
                {
                    progress = waiting_progress.First().Progress;
                    progress.Show();
                    Hide();

                    LoadEngineVersion(waiting_progress.First().EngineVersion, false);

                    FireUpBackgroundWorker();
                }
            }
        }

        private void Button_StartAll_Click(object sender, RoutedEventArgs e)
        {
            waiting_progress.Clear();

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
                            InProgress newProgress = new InProgress();
                            newProgress.mainWindow = this;
                            newProgress.Text_Plugin.Text = GetPluginName();
                            newProgress.Text_Version.Text = engineVersion;
                            newProgress.bZip = Check_Zip.IsChecked.Value;

                            waiting_progress.Add(new WaitingProgress(newProgress, fileVersion));
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
            // Show progress window
            progress = new InProgress();
            progress.mainWindow = this;
            progress.Text_Plugin.Text = GetPluginName();
            progress.Text_Version.Text = engineVersion;
            progress.Show();
            progress.bZip = Check_Zip.IsChecked.Value;
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

            if (!p.HasExited)
            {
                p.CloseMainWindow();
            }
        }

        private void BW_DoWork(object sender, DoWorkEventArgs e)
        {
            // Start child process
            p = new Process();

            // Redirect output stream
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.FileName = "cmd.exe";
            p.StartInfo.Arguments = e.Argument.ToString();
            bool ret = p.Start();

            e.Result = p.StandardOutput.ReadToEnd();

            p.WaitForExit();
        }

        private void BW_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (!p.HasExited)
            {
                p.CloseMainWindow();
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

