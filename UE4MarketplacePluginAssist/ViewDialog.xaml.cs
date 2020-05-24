using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Interaction logic for ViewDialog.xaml
    /// </summary>
    public partial class ViewDialog : Window
    {
        public List<ParseResult> result = new List<ParseResult>();

        public ViewDialog()
        {
            InitializeComponent();
        }

        public void InitParseResults(List<ParseResult> results, ParseResultType viewType)
        {
            result.Clear();

            foreach (ParseResult r in results)
            {
                if (r.Type == viewType)
                {
                    result.Add(r);
                }
            }

            string count = result.Count.ToString();
            if (result.Count <= 1)
            {
                Button_Prev.IsEnabled = false;
                Button_Next.IsEnabled = false;

                Text_Current.Text = count;
                Text_Max.Text = count;
            }
            else
            {
                Text_Max.Text = count;
            }

            SetCurrent(1);
        }

        private int GetCurrent()
        {
            return int.Parse(Text_Current.Text);
        }

        private void SetCurrent(int current)
        {
            if(result == null || result.Count == 0)
            {
                return;
            }

            // Clamp current
            current = Math.Max(1, current);
            current = Math.Min(result.Count, current);

            Text_Current.Text = current.ToString();

            Button_Prev.IsEnabled = (current != 1);
            Button_Next.IsEnabled = (current != result.Count);

            Text_LineNumber.Text = result[current - 1].Line.ToString();
            Text_Message.Text = result[current - 1].Message;
        }

        private void Button_Next_Click(object sender, RoutedEventArgs e)
        {
            SetCurrent(GetCurrent() + 1);
        }

        private void Button_Prev_Click(object sender, RoutedEventArgs e)
        {
            SetCurrent(GetCurrent() - 1);
        }
    }
}
