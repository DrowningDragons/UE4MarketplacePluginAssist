using System;
using System.Collections.Generic;
using System.Windows;

namespace UE4MarketplacePluginAssist
{
    /// <summary>
    /// Interaction logic for ViewDialog.xaml
    /// </summary>
    public partial class ViewDialog : Window
    {
        private readonly List<ParseResult> _result = new List<ParseResult>();

        public ViewDialog()
        {
            InitializeComponent();
        }

        public void InitParseResults(List<ParseResult> results, ParseResultType viewType)
        {
            _result.Clear();

            foreach (ParseResult r in results)
            {
                if (r.type == viewType)
                {
                    _result.Add(r);
                }
            }

            string count = _result.Count.ToString();
            if (_result.Count <= 1)
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
            if(_result == null || _result.Count == 0)
            {
                return;
            }

            // Clamp current
            current = Math.Max(1, current);
            current = Math.Min(_result.Count, current);

            Text_Current.Text = current.ToString();

            Button_Prev.IsEnabled = (current != 1);
            Button_Next.IsEnabled = (current != _result.Count);

            Text_LineNumber.Text = _result[current - 1].line.ToString();
            Text_Message.Text = _result[current - 1].message;
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
