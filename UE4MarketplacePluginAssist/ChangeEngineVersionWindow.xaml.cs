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
    /// <summary>
    /// Interaction logic for ChangeEngineVersionWindow.xaml
    /// </summary>
    public partial class ChangeEngineVersionWindow : Window
    {
        public MainWindow mainWindow;

        public ChangeEngineVersionWindow()
        {
            InitializeComponent();
        }


        private static readonly Regex _regex = new Regex("[^0-9]"); //regex that matches disallowed text
        private static bool IsTextAllowed(string text)
        {
            return _regex.IsMatch(text);
        }

        private void EngineVersion_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (IsTextAllowed(e.Text))
            {
                e.Handled = true;
            }
        }

        private void Button_EngineVersionCancel_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.Show();
            Close();
        }

        private void Button_EngineVersionApply_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.Show();
            mainWindow.ChangeEngineVersion(Text_EngineVersion.Text);

            Close();
        }

        private void Button_IncrEngine_Click(object sender, RoutedEventArgs e)
        {
            int v = int.Parse(Text_EngineVersion.Text);
            v++;
            Text_EngineVersion.Text = v.ToString();
        }

        private void Button_DecrEngine_Click(object sender, RoutedEventArgs e)
        {
            int v = int.Parse(Text_EngineVersion.Text);
            v--;
            Text_EngineVersion.Text = v.ToString();
        }
    }
}
