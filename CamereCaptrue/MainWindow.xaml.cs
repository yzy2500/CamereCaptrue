using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Window = System.Windows.Window;

namespace CamereCaptrue
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        readonly CaptureHelper Capture;
        public MainWindow()
        {
            InitializeComponent();
            Capture = new CaptureHelper(FrameSource);
        }

        /// <summary>
        /// 初始化摄像头设备
        /// </summary>
        /// <param name="e"></param>
        protected override async void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var devices = (await MediaFrameSourceGroup.FindAllAsync()).ToList();
            if (devices != null && devices.Count > 0)
            {
                DeviceBox.ItemsSource = devices;
                DeviceBox.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 切换摄像头设备
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void DeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeviceBox.SelectedItem is MediaFrameSourceGroup group)
            {
                await Capture.ClearFrameReader();

                MediaCapture media = new();
                var settings = new MediaCaptureInitializationSettings
                {
                    SourceGroup = group,
                    SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                };
                await media.InitializeAsync(settings);

                var formats = new List<FormatsInfo>();
                var startedKinds = new HashSet<MediaFrameSourceKind>();
                foreach (MediaFrameSource source in media.FrameSources.Values)
                {
                    MediaFrameSourceKind kind = source.Info.SourceKind;
                    if (startedKinds.Contains(kind)) continue;
                    foreach (MediaFrameFormat f in source.SupportedFormats.Where(x => x.Subtype.ToUpper() == "NV12"))
                        formats.Add(new FormatsInfo
                        {
                            Width = f.VideoFormat.Width,
                            Height = f.VideoFormat.Height,
                            FPS = f.FrameRate.Numerator,
                        });
                }

                media.Dispose();
                await Capture.Create(group);

                FormatBox.ItemsSource = formats;
                FormatBox.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 切换格式
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FormatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FormatBox.SelectedItem is FormatsInfo formats)
                await Capture.TryPreview(formats);
        }

        /// <summary>
        /// 拍照
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void PhotoBtn_Click(object sender, RoutedEventArgs e)
        {
            var buf = await Capture.Photo();
            if (buf == null) return;
            using var mat = new Mat(Capture.FrameHeight, Capture.FrameWidth, MatType.CV_8UC4, buf);
            var img = new Image { Source = mat.ToBitmapSource() };
            SavePathBtn.Content = img;
            SavePathBtn.Visibility = Visibility.Visible;
            await Task.Run(() =>
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), DateTime.Now.ToString("M"));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                mat.SaveImage(System.IO.Path.Combine(dir, DateTime.Now.ToString("ddHHmmss") + ".jpg"));
            });
        }

        /// <summary>
        /// 打开保存地址
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenSavePath_Click(object sender, RoutedEventArgs e)
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), DateTime.Now.ToString("M"));
            System.Diagnostics.Process.Start("explorer.exe", dir);
        }
    }
}