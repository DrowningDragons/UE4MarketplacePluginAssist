using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace UE4MarketplacePluginAssist
{
    public enum ParseResultType
    {
        Warning,
        Error
    }

    public struct ParseResult
    {
        public ParseResultType Type;
        public string Message;
        public int Line;

        public ParseResult(ParseResultType type, string message, int line)
        {
            Type = type;
            Message = message;
            Line = line;
        }
    }

    /// <summary>
    /// Interaction logic for InProgress.xaml
    /// </summary>
    public partial class InProgress : Window
    {
        public MainWindow mainWindow;
        public bool bZip;

        private string logPath;
        private string logFile;

        List<ParseResult> parsed;

        private ViewDialog warningDialog;
        private ViewDialog errorDialog;

        private bool bCompleted = false;

        private static string[] SafeErrorWords = { "Running UnrealHeaderTool" };
        private static string[] SafeWarningWords = { "Running UnrealHeaderTool" };

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
            if (mainWindow.bw.IsBusy && !bCompleted && mainWindow.progress == this)
            {
                bCompleted = true;
                mainWindow.CancelBackgroundWorker();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (!bCompleted && mainWindow.progress == this)
            {
                bCompleted = true;
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

        public void Completed(string result)
        {
            bCompleted = true;
            Button_Abort.IsEnabled = false;

            // Convert string to a stream for parsing
            Stream stream = ConvertStringToStream(result);

            logPath = mainWindow.GetLogPath();

            if (!Directory.Exists(logPath))
            {
                Directory.CreateDirectory(logPath);

                if (!Directory.Exists(logPath))
                {
                    throw new Exception("Could not create log directory");
                }
            }

            Button_OpenLog.IsEnabled = true;

            // Write output log first
            logFile = mainWindow.GetLogFile();
            File.WriteAllText(logFile, result);

            // Update final result
            bool bSuccess = result.Contains("BUILD SUCCESSFUL");
            if (bSuccess)
            {
                Text_Status.Text = "Successful";
                Border_Status.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7af366"));
            }
            else
            {
                Text_Status.Text = "Failed";
                Border_Status.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF0000"));
            }
            Spinner_Progress.Visibility = Visibility.Hidden;

            if(result.Length <= 0)
            {
                return;
            }

            parsed = new List<ParseResult>();

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
                            parsed.Add(new ParseResult(ParseResultType.Error, s, line));
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
                            parsed.Add(new ParseResult(ParseResultType.Warning, s, line));
                        }
                    }

                    line++;
                }
            }

            if(parsed.Count > 0)
            {
                int numErrors = 0;
                int numWarnings = 0;

                foreach(ParseResult r in parsed)
                {
                    switch (r.Type)
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
                string zipPath = Directory.GetParent(Directory.GetParent(mainWindow.GetPluginDirectory()).FullName).FullName;
                string pluginName = mainWindow.GetPluginName();
                string engineVersion = mainWindow.engineVersion;

                string zipFile = zipPath + "\\" + pluginName + "_4" + engineVersion + ".zip";

                // Get binaries and intermediate ready to move
                string pluginPath = mainWindow.GetPluginDirectory();

                string binariesSrc = pluginPath + "Binaries\\";
                string intermediateSrc = pluginPath + "Intermediate\\";

                string binariesTgt = Directory.GetParent(Directory.GetParent(pluginPath).FullName).FullName + "\\Binaries\\";
                string intermediateTgt = Directory.GetParent(Directory.GetParent(pluginPath).FullName).FullName + "\\Intermediate\\";

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
                    File.Delete(zipFile);
                }

                ZipFile.CreateFromDirectory(pluginPath, zipFile);
                
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
                Process.Start(fi.DirectoryName);
            }

            mainWindow.Check_Waiting_Progress();
        }

        private void Button_OpenLog_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(logFile))
            {
                Process.Start(logFile);
            }
            else
            {
                System.Windows.MessageBox.Show(this, "Log file not found");
            }
        }

        private void Button_ViewWarnings_Click(object sender, RoutedEventArgs e)
        {
            if(warningDialog != null)
            {
                if (warningDialog.IsActive)
                {
                    warningDialog.Hide();
                }
                else
                {
                    warningDialog.Show();
                }
            }
            else
            {
                warningDialog = new ViewDialog();
                warningDialog.Title = "Warnings";
                warningDialog.InitParseResults(parsed, ParseResultType.Warning);
                warningDialog.Show();
            }
        }

        private void Button_ViewErrors_Click(object sender, RoutedEventArgs e)
        {
            if (errorDialog != null)
            {
                if (errorDialog.IsActive)
                {
                    errorDialog.Hide();
                }
                else
                {
                    errorDialog.Show();
                }
            }
            else
            {
                errorDialog = new ViewDialog();
                errorDialog.Title = "Errors";
                errorDialog.InitParseResults(parsed, ParseResultType.Error);
                errorDialog.Show();
            }
        }
    }
}
