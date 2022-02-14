using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using static System.Windows.Media.ColorConverter;

namespace UE4MarketplacePluginAssist
{
    public enum ParseResultType
    {
        Warning,
        Error
    }

    public struct ParseResult
    {
        public readonly ParseResultType type;
        public readonly string message;
        public readonly int line;

        public ParseResult(ParseResultType type, string message, int line)
        {
            this.type = type;
            this.message = message;
            this.line = line;
        }
    }

    /// <summary>
    /// Interaction logic for InProgress.xaml
    /// </summary>
    public partial class InProgress : Window
    {
        public MainWindow mainWindow;
        public bool bZip;

        private string _logPath;
        private string _logFile;

        private List<ParseResult> _parsed;

        private ViewDialog _warningDialog;
        private ViewDialog _errorDialog;

        private bool _bCompleted = false;

        private static readonly string[] SafeErrorWords = { "Running UnrealHeaderTool" };
        private static readonly string[] SafeWarningWords = { "Running UnrealHeaderTool" };

        public InProgress()
        {
            InitializeComponent();
        }

        private void Button_Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Button_Abort_Click(object sender, RoutedEventArgs e)
        {
            if (mainWindow.bw.IsBusy && !_bCompleted && mainWindow.progress == this)
            {
                _bCompleted = true;
                mainWindow.CancelBackgroundWorker();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (!_bCompleted && mainWindow.progress == this)
            {
                _bCompleted = true;
                mainWindow.CancelBackgroundWorker();
            }

            if (mainWindow.progress == this)
            {
                mainWindow.Show();
            }
        }

        private static Stream ConvertStringToStream(string s)
        {
            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new StreamWriter(stream);

            writer.Write(s);
            writer.Flush();
            stream.Position = 0;

            return stream;
        }

        private bool CheckZipPath(DirectoryInfo dir, string testDir, string preText, bool bTest = false)
        {
            if (dir != null && !bTest)
            {
                return true;
            }
            
            var mBoxError = preText + " [ " + testDir +
                            " ] has null parent. Zip aborted.";
            MessageBox.Show(this, mBoxError);
            
            return false;
        }

        public void Completed(string result)
        {
            _bCompleted = true;
            Button_Abort.IsEnabled = false;

            // Convert string to a stream for parsing
            Stream stream = ConvertStringToStream(result);

            _logPath = mainWindow.GetLogPath();

            if (!Directory.Exists(_logPath))
            {
                Directory.CreateDirectory(_logPath);

                if (!Directory.Exists(_logPath))
                {
                    throw new Exception("Could not create log directory");
                }
            }

            Button_OpenLog.IsEnabled = true;

            // Write output log first
            _logFile = mainWindow.GetLogFile();
            File.WriteAllText(_logFile, result);

            // Update final result
            bool bSuccess = result.Contains("BUILD SUCCESSFUL");
            if (bSuccess)
            {
                Text_Status.Text = "Successful";
                Border_Status.Background = new SolidColorBrush((Color)ConvertFromString("#7af366"));
            }
            else
            {
                Text_Status.Text = "Failed";
                Border_Status.Background = new SolidColorBrush((Color)ConvertFromString("#FF0000"));
            }
            Spinner_Progress.Visibility = Visibility.Hidden;

            if(result.Length <= 0)
            {
                return;
            }

            _parsed = new List<ParseResult>();

            // Parse result stream
            using (StreamReader sr = new StreamReader(stream))
            {
                string s = "";
                int line = 1;
                while ((s = sr.ReadLine()) != null)
                {
                    if (s.Contains("error"))
                    {
                        // Make sure this wasn't triggered in a line marked as safe
                        bool bSafe = false;
                        foreach (string safe in SafeErrorWords)
                        {
                            if (s.Contains(safe))
                            {
                                bSafe = true;
                                break;
                            }
                        }

                        if (!bSafe)
                        {
                            _parsed.Add(new ParseResult(ParseResultType.Error, s, line));
                        }
                    }
                    else if (s.Contains("warning"))
                    {
                        // Make sure this wasn't triggered in a line marked as safe
                        bool bSafe = false;
                        foreach (string safe in SafeWarningWords)
                        {
                            if (s.Contains(safe))
                            {
                                bSafe = true;
                                break;
                            }
                        }

                        if (!bSafe)
                        {
                            _parsed.Add(new ParseResult(ParseResultType.Warning, s, line));
                        }
                    }

                    line++;
                }
            }

            if(_parsed.Count > 0)
            {
                int numErrors = 0;
                int numWarnings = 0;

                foreach(ParseResult r in _parsed)
                {
                    switch (r.type)
                    {
                        case ParseResultType.Error:
                            numErrors++;
                            break;
                        case ParseResultType.Warning:
                            numWarnings++;
                            break;
                        default:
                            break;
                    }
                }

                if (numErrors > 0)
                {
                    Button_ViewErrors.IsEnabled = true;
                    Button_ViewErrors.Content = "View Errors (" + numErrors.ToString() + ")";
                }

                if (numWarnings > 0)
                {
                    Button_ViewWarnings.IsEnabled = true;
                    Button_ViewWarnings.Content = "View Warnings (" + numWarnings.ToString() + ")";
                }
            }

            // Zip the plugin in preparation for distribution if desired
            if (bZip && bSuccess)
            {
                // Determine zip file
                DirectoryInfo pluginDir = Directory.GetParent(mainWindow.GetPluginDirectory());
                if (!CheckZipPath(pluginDir, mainWindow.GetPluginDirectory(), "PluginDirectory"))
                {
                    return;
                }

                string testDir = pluginDir.FullName;
                pluginDir = Directory.GetParent(pluginDir.FullName);
                if (!CheckZipPath(pluginDir, testDir, "PluginDirectoryParent"))
                {
                    return;
                }
                
                string zipPath = pluginDir.FullName;
                string pluginName = mainWindow.GetPluginName();
                string engineVersion = mainWindow.engineVersion;

                string zipFile = zipPath + "\\" + pluginName + "_" + engineVersion + ".zip";

                // Get binaries and intermediate ready to move
                string binariesSrc = mainWindow.GetPluginDirectory() + "Binaries\\";
                string intermediateSrc = mainWindow.GetPluginDirectory() + "Intermediate\\";

                string binariesTgt = pluginDir.FullName + "\\Binaries\\";
                string intermediateTgt = pluginDir.FullName + "\\Intermediate\\";

                // Move binaries and intermediate out in preparation to zip the plugin
                if (Directory.Exists(binariesSrc))
                {
                    Directory.Move(binariesSrc, binariesTgt);
                }

                if (Directory.Exists(intermediateSrc))
                {
                    Directory.Move(intermediateSrc, intermediateTgt);
                }

                while(Directory.Exists(binariesSrc) || Directory.Exists(intermediateSrc))
                {
                    // Hang here until it has finished moving them...
                }

                // Zip the plugin
                if (File.Exists(zipFile))
                {
                    // Delete pre-existing zip file
                    File.Delete(zipFile);
                }

                try
                {
                    ZipFile.CreateFromDirectory(mainWindow.GetPluginDirectory(), zipFile);
                }
                catch (Exception ex)
                {
                    var mBoxError = "Zip file could not be created: " + ex.Message.ToString() + " Zip aborted.";
                    MessageBox.Show(this, mBoxError);
                    return;
                }
                if (!File.Exists(zipFile))
                {
                    MessageBox.Show(this, "Zip file could not be created: Unknown error. Zip aborted.");
                    return;
                }
                
                // Test the file size for completion
                FileInfo fi = new FileInfo(zipFile);

                while (fi.Length <= 0)
                {
                    // Hang here until it has finished zipping...
                }

                // Move the Binaries and Intermediate back
                if (Directory.Exists(binariesTgt))
                {
                    Directory.Move(binariesTgt, binariesSrc);
                }

                if (Directory.Exists(intermediateTgt))
                {
                    Directory.Move(intermediateTgt, intermediateSrc);
                }

                while (Directory.Exists(binariesTgt) || Directory.Exists(intermediateTgt))
                {
                    // Hang here until it has finished moving them...
                }

                // Open the directory
                if (Directory.Exists(fi.DirectoryName))
                {
                    Process.Start(fi.DirectoryName);
                }
                else
                {
                    var mBoxError = "Zip file directory does not exist: " + fi.DirectoryName + " - Zip completed but error in opening the directory location.";
                    MessageBox.Show(this, mBoxError);
                }
            }

            mainWindow.Check_Waiting_Progress();
        }

        private void Button_OpenLog_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(_logFile))
            {
                Process.Start(_logFile);
            }
            else
            {
                MessageBox.Show(this, "Log file not found");
            }
        }

        private void Button_ViewWarnings_Click(object sender, RoutedEventArgs e)
        {
            if(_warningDialog != null)
            {
                if (_warningDialog.IsActive)
                {
                    _warningDialog.Hide();
                }
                else
                {
                    _warningDialog.Show();
                }
            }
            else
            {
                _warningDialog = new ViewDialog();
                _warningDialog.Title = "Warnings";
                _warningDialog.InitParseResults(_parsed, ParseResultType.Warning);
                _warningDialog.Show();
            }
        }

        private void Button_ViewErrors_Click(object sender, RoutedEventArgs e)
        {
            if (_errorDialog != null)
            {
                if (_errorDialog.IsActive)
                {
                    _errorDialog.Hide();
                }
                else
                {
                    _errorDialog.Show();
                }
            }
            else
            {
                _errorDialog = new ViewDialog();
                _errorDialog.Title = "Errors";
                _errorDialog.InitParseResults(_parsed, ParseResultType.Error);
                _errorDialog.Show();
            }
        }
    }
}
