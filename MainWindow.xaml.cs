using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WClipbroad = System.Windows.Clipboard;

namespace FileContentSearcher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        const byte ZERO = 0;

        public FlowDocument FileContent
        {
            get { return (FlowDocument)GetValue(FileContentProperty); }
            set { SetValue(FileContentProperty, value); }
        }

        public static readonly DependencyProperty FileContentProperty =
            DependencyProperty.Register("FileContent", typeof(FlowDocument), typeof(MainWindow), new PropertyMetadata(null, new PropertyChangedCallback(FileContent_Changed)));

        private static void FileContent_Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue != null)
            {
                var instance = (d as MainWindow).rtbFileContent;
                instance.Document = e.NewValue as FlowDocument;
            }
        }

        public string SearchingString
        {
            get { return (string)GetValue(SearchingStringProperty); }
            set { SetValue(SearchingStringProperty, value); }
        }

        public static readonly DependencyProperty SearchingStringProperty =
            DependencyProperty.Register("SearchingString", typeof(string), typeof(MainWindow), new PropertyMetadata(""));

        public string CurrentSearchingFile
        {
            get { return (string)GetValue(CurrentSearchingFileProperty); }
            set { SetValue(CurrentSearchingFileProperty, value); }
        }

        public static readonly DependencyProperty CurrentSearchingFileProperty =
            DependencyProperty.Register("CurrentSearchingFile", typeof(string), typeof(MainWindow), new PropertyMetadata(""));

        public FileInfo SelectedFile
        {
            get { return (FileInfo)GetValue(SelectedFileProperty); }
            set { SetValue(SelectedFileProperty, value); }
        }
        
        public static readonly DependencyProperty SelectedFileProperty =
            DependencyProperty.Register("SelectedFile", typeof(FileInfo), typeof(MainWindow), new PropertyMetadata(null, new PropertyChangedCallback(SelectedFile_Changed)));

        private static void SelectedFile_Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue != null)
            {
                var instance = d as MainWindow;
                var fileInfo = e.NewValue as FileInfo;
                
                if (File.ReadAllBytes(fileInfo.FullName).Contains(ZERO))
                {
                    return;
                }

                var fileContent = File.ReadAllText(fileInfo.FullName);
                Paragraph paragraph = new Paragraph();
                if (instance.cbPatternFileContent.IsChecked.GetValueOrDefault())
                {
                    var searchStrings = instance.tbSearchStrings.Text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).OrderByDescending(x => x.Length);
                    var matchCase = instance.cbCase.IsChecked.GetValueOrDefault();
                    var startIndex = 0;
                    var endIndex = fileContent.Length - searchStrings.Min(x => x.Length);
                    do
                    {
                        var source = fileContent.Substring(startIndex, endIndex - startIndex);
                        var firstValue = FindFirst(source, searchStrings, matchCase).OrderBy(x => x.Value).FirstOrDefault();
                        if (firstValue.Key != null)
                        {
                            if (firstValue.Value > 0)
                            {
                                AddText(paragraph, Brushes.White, source.Substring(0, firstValue.Value));
                            }

                            AddText(paragraph, Brushes.Yellow, source.Substring(firstValue.Value, firstValue.Key.Length));
                            startIndex += firstValue.Value + firstValue.Key.Length;
                        }
                        else
                        {
                            break;
                        }

                    } while (startIndex < endIndex);

                    if (startIndex < fileContent.Length)
                    {
                        AddText(paragraph, Brushes.Transparent, fileContent.Substring(startIndex));
                    }
                }
                else
                {
                    AddText(paragraph, Brushes.Transparent, fileContent);
                }
                
                var doc = new FlowDocument();
                doc.MinPageWidth = 160;
                doc.MaxPageWidth = 4096;
                doc.PageWidth = 2048;
                doc.Blocks.Add(paragraph);
                instance.FileContent = doc;
            }
        }

        static void AddText(Paragraph para, Brush brush, string content)
        {
            var run = new Run(content);
            run.Background = brush;
            para.Inlines.Add(run);
        }
        static IEnumerable<KeyValuePair<string, int>> FindFirst(string source, IEnumerable<string> targets, bool matchCase)
        {
            if (source.Length > 0)
            {
                foreach (var target in targets)
                {
                    var index = source.IndexOf(target);
                    if (index >= 0)
                    {
                        yield return new KeyValuePair<string, int>(target, index);
                    }
                }
            }
        }

        public ObservableCollection<FileInfo> SearchedFiles
        {
            get { return (ObservableCollection<FileInfo>)GetValue(SearchedFilesProperty); }
            set { SetValue(SearchedFilesProperty, value); }
        }

        public static readonly DependencyProperty SearchedFilesProperty =
            DependencyProperty.Register("SearchedFiles", typeof(ObservableCollection<FileInfo>), typeof(MainWindow), new PropertyMetadata(new ObservableCollection<FileInfo>()));

        private void btnChangeDirectory_Click(object sender, RoutedEventArgs e)
        {
            using (var openDirectoryDialog = new FolderBrowserDialog())
            {
                if (openDirectoryDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return;
                }
                tbDirectory.Text = openDirectoryDialog.SelectedPath;
            }
        }

        private void btnSearch_Click(object sender, RoutedEventArgs e)
        {
            var dir = tbDirectory.Text;
            if (!Directory.Exists(dir))
            {
                System.Windows.MessageBox.Show("Directory was not existed.");
                return;
            }

            var isPatternName = cbPatternFileName.IsChecked.GetValueOrDefault();
            var isPatternContent = cbPatternFileContent.IsChecked.GetValueOrDefault();
            var matchCase = cbCase.IsChecked.GetValueOrDefault();
            var filePatterns = tbSearchFilePattern.Text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var searchStrings = tbSearchStrings.Text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            SearchedFiles.Clear();
            SearchingString = "Searching...\t";
            new Task(() =>
            {
                foreach (var file in FindFile(dir, isPatternName, isPatternContent, filePatterns, matchCase, searchStrings))
                {
                    this.Dispatcher.Invoke(new Action(() =>
                    {
                        SearchedFiles.Add(file);
                    }));
                }

                this.Dispatcher.Invoke(new Action(() =>
                {
                    SearchingString = "Search completed.";
                    CurrentSearchingFile = string.Empty;
                }));

                GC.Collect();
            }).Start();
        }

        IEnumerable<FileInfo> FindFile(string dirName, bool searchName, bool searchContent, string[] filePatterns,
            bool matchCase, params string[] matchStrs)
        {
            if (searchContent || searchName)
            {
                var directoryInfo = new DirectoryInfo(dirName);
                foreach (var filePattern in filePatterns)
                {
                    foreach (var fileInfo in directoryInfo.GetFiles(filePattern, SearchOption.AllDirectories))
                    {
                        this.Dispatcher.Invoke(new Action(() =>
                        {
                            CurrentSearchingFile = fileInfo.FullName;
                        }));

                        var isHit = false;
                        if (searchName)
                        {
                            if (matchStrs.Any(x => IsContaisFunc(fileInfo.Name, x, matchCase)))
                            {
                                isHit = true;
                            }
                        }
                        if (searchContent)
                        {
                            var fileLength = fileInfo.Length;
                            if (fileLength < 1024 * 1024 * 5)
                            {
                                var fileContent = File.ReadAllText(fileInfo.FullName);
                                if (matchStrs.Any(x => IsContaisFunc(fileContent, x, matchCase)))
                                {
                                    isHit = true;
                                }
                            }
                        }

                        if (isHit)
                        {
                            yield return fileInfo;
                        }
                    }
                }
            }
        }

        static int GetIndex(string source, string target, bool matchCase)
        {
            return source.IndexOf(target, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        static Func<string, string, bool, bool> IsContaisFunc
        {
            get
            {
                return new Func<string, string, bool, bool>((s, t, m) =>
                {
                    return GetIndex(s, t, m) >= 0;
                });
            }
        }

        private void miOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer.exe", "/select, " + SelectedFile.FullName);
        }

        private void miCopyFile_Click(object sender, RoutedEventArgs e)
        {
            WClipbroad.SetFileDropList(new System.Collections.Specialized.StringCollection()
                {
                    SelectedFile.FullName
                });
        }

        private void miCopyFilePath_Click(object sender, RoutedEventArgs e)
        {
            WClipbroad.SetText(SelectedFile.FullName);
        }

        private void miCopyDirectoryPath_Click(object sender, RoutedEventArgs e)
        {
            WClipbroad.SetText(SelectedFile.DirectoryName);
        }

        private void miCopyFileContent_Click(object sender, RoutedEventArgs e)
        {
            WClipbroad.SetText(File.ReadAllText(SelectedFile.FullName));
        }
    }
}
