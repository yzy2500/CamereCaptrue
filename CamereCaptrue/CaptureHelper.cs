using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace CamereCaptrue
{
    internal class CaptureHelper
    {
        #region 初始化

        public CaptureHelper(Image image) => FrameSource = image;

        /// <summary>
        /// 创建摄像头捕获的实体
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        public async Task<bool> Create(MediaFrameSourceGroup device)
        {
            if (device is null) return false;
            var settings = new MediaCaptureInitializationSettings
            {
                SourceGroup = device,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video,
            };
            await Capture.InitializeAsync(settings);
            return true;
        }

        #endregion

        #region 帧预览
        /// <summary>
        /// 捕获设备
        /// </summary>
        readonly MediaCapture Capture = new();
        /// <summary>
        /// 帧数据读取设备
        /// </summary>
        MediaFrameReader Reader { get; set; }
        /// <summary>
        /// 渲染对象
        /// </summary>
        readonly Image FrameSource;
        /// <summary>
        /// 为false表示正在设置预览格式并尝试开始捕获帧数据
        /// </summary>
        bool CanInitReader = true;
        /// <summary>
        /// 帧宽度
        /// </summary>
        public int FrameWidth { get; private set; }
        /// <summary>
        /// 帧高度
        /// </summary>
        public int FrameHeight { get; private set; }
        /// <summary>
        /// 帧数据缓冲区
        /// </summary>
        Windows.Storage.Streams.Buffer FrameBuf { get; set; }
        /// <summary>
        /// 帧数据
        /// </summary>
        public byte[] FrameData { get; private set; }
        /// <summary>
        /// NV12帧数据
        /// </summary>
        Mat FrameNV12 { get; set; }
        /// <summary>
        /// Bgr帧数据
        /// </summary>
        Mat FrameBgr { get; set; }

        /// <summary>
        /// 尝试预览
        /// </summary>
        /// <param name="format">预览的格式</param>
        /// <param name="element">显示预览画面的控件</param>
        /// <returns></returns>
        public async Task<bool> TryPreview(FormatsInfo format)
        {
            if (!CanInitReader) return false;
            CanInitReader = false;
            try
            {
                await ClearFrameReader();
                await Task.Delay(50);
                return await Preview(format);
            }
            catch { return false; }
            finally { CanInitReader = true; }
        }
        /// <summary>
        /// 尝试预览
        /// </summary>
        /// <param name="format">预览的格式</param>
        /// <returns></returns>
        private async Task<bool> Preview(FormatsInfo format)
        {
            foreach (MediaFrameSource source in Capture.FrameSources.Values)
            {
                MediaFrameSourceKind kind = source.Info.SourceKind;
                if (kind != MediaFrameSourceKind.Color) continue;
                //查找指定的格式
                var f = source.SupportedFormats.FirstOrDefault(x => x.Subtype.ToUpper() == "NV12" && x.VideoFormat.Width == format.Width
                    && x.VideoFormat.Height == format.Height && x.FrameRate.Numerator == format.FPS);
                if (f is null) continue;
                //尝试设置格式
                try { await source.SetFormatAsync(f); }
                catch { continue; }
                //创建帧数据读取设备
                MediaFrameReader frameReader = await Capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Nv12);
                frameReader.FrameArrived += FrameReader_FrameArrived;
                MediaFrameReaderStartStatus status = await frameReader.StartAsync();
                if (status == MediaFrameReaderStartStatus.Success)
                {
                    Reader = frameReader;
                    return true;
                }
                //没能正确创建，将其释放掉
                await frameReader.StopAsync();
                frameReader.Dispose();
            }
            return false;
        }

        /// <summary>
        /// 捕获帧数据
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void FrameReader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            using var frame = sender.TryAcquireLatestFrame();
            if (frame != null)
            {
                using var softwareBitmap = frame.VideoMediaFrame.SoftwareBitmap;
                if (softwareBitmap != null)
                {
                    if (FrameWidth == 0 || FrameHeight == 0)
                    {
                        FrameWidth = softwareBitmap.PixelWidth;
                        FrameHeight = softwareBitmap.PixelHeight;
                        using var m = softwareBitmap.LockBuffer(BitmapBufferAccessMode.Read);
                        using var n = m.CreateReference();
                        var t = m.GetPlaneDescription(0);
                        FrameBuf = new Windows.Storage.Streams.Buffer(n.Capacity);
                    }

                    softwareBitmap.CopyToBuffer(FrameBuf);
                    using var reader = DataReader.FromBuffer(FrameBuf);
                    if (FrameData is null || FrameData.Length != FrameBuf.Length) 
                    {
                        FrameData = new byte[FrameBuf.Length];
                        var prt = Marshal.AllocHGlobal(FrameData.Length);
                        FrameNV12 = new Mat(FrameHeight * 3 / 2, FrameWidth, MatType.CV_8UC1, prt);
                        FrameBgr = new Mat();
                    }
                    reader.ReadBytes(FrameData);
                    Marshal.Copy(FrameData, 0, FrameNV12.Data, FrameData.Length);
                    Cv2.CvtColor(FrameNV12, FrameBgr, ColorConversionCodes.YUV2BGR_NV12);
                    RendererFrame();
                }
            }
        }

        /// <summary>
        /// 渲染帧
        /// </summary>
        private void RendererFrame()
        {
            FrameSource.Dispatcher.InvokeAsync(() =>
            {
                if (FrameSource.Source is not WriteableBitmap bmp || bmp.PixelWidth != FrameWidth || bmp.PixelHeight != FrameHeight)
                {
                    FrameSource.Source = new WriteableBitmap(FrameWidth, FrameHeight, 96, 96, PixelFormats.Bgr24, null);
                    return;
                }
                WriteableBitmapConverter.ToWriteableBitmap(FrameBgr, bmp);
            });
        }

        #endregion

        #region 释放

        /// <summary>
        /// 释放帧读取
        /// </summary>
        /// <returns></returns>
        public async Task ClearFrameReader(bool clearFrame = true)
        {
            if (Reader is not null && Monitor.TryEnter(Reader))
            {
                MediaFrameReader reader = null;
                lock (Reader)
                {
                    reader = Reader;
                    Reader = null;
                }
                await reader?.StopAsync();
                reader?.Dispose();
                FrameNV12?.Dispose();
                FrameNV12 = null;
                FrameBgr?.Dispose();
                FrameBgr = null;
                FrameWidth = 0;
                FrameHeight = 0;
                FrameData = null;
                if (clearFrame) await FrameSource.Dispatcher.InvokeAsync(() => FrameSource.Source = null);
            }
        }

        public async Task Dispose()
        {
            await ClearFrameReader();
            Capture.Dispose();
        }

        #endregion

        #region 拍照

        public Task<byte[]> Photo()
        {
            if (FrameWidth == 0 || FrameHeight == 0) return null;
            return Task.Run(async () =>
            {
                try
                {
                    var iep = ImageEncodingProperties.CreateUncompressed(MediaPixelFormat.Bgra8);
                    var lag = await Capture.PrepareLowLagPhotoCaptureAsync(iep);
                    var photo = await lag.CaptureAsync();
                    var softwareBitmap = photo.Frame.SoftwareBitmap;
                    await lag.FinishAsync();

                    using var m = softwareBitmap.LockBuffer(BitmapBufferAccessMode.Read);
                    using var n = m.CreateReference();
                    var t = m.GetPlaneDescription(0);
                    var buf = new Windows.Storage.Streams.Buffer(n.Capacity);
                    var data = new byte[n.Capacity];

                    softwareBitmap.CopyToBuffer(buf);
                    using var reader = DataReader.FromBuffer(buf);
                    reader.ReadBytes(data);
                    return data;
                }
                catch
                {
                    return null;
                }
            });
        }

        #endregion
    }

    internal class FormatsInfo
    {
        public uint Width { get; set; }
        public uint Height { get; set; }
        public uint FPS { get; set; }
        [JsonIgnore]
        public string Name { get { return $"{Width}x{Height}  FPS:{FPS}"; } }
    }
}
