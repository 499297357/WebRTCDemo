using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog.Extensions.Logging;
using System.IO;
using System.Drawing;
using System.Windows;
using System.Runtime.InteropServices;
using static Org.BouncyCastle.Bcpg.Attr.ImageAttrib;
using System.Security.Cryptography;
using System.Windows.Controls;

namespace Source
{
    public class WebRtc
    {

        private const string STUN_URL = "stun:stun.sipgate.net";
        RTCPeerConnection pc;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;
        Action<string> SetIceAction;
        public WebRtc(Action<string> action)
        {
            SetIceAction = action;
            logger = AddConsoleLogger();
            RTCConfiguration config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
            };
            pc = new RTCPeerConnection(config);
            //pc.createDataChannel("asdasd");
            pc.onicecandidate += Pc_onicecandidate;


        }
        WriteableBitmap bitmap;
        public void SetIce(string ice)
        {
            if (RTCIceCandidateInit.TryParse(ice, out var iceCandidateInit))
            {
                logger.LogDebug("Got remote ICE candidate.");
                pc.addIceCandidate(iceCandidateInit);
            }
        }

        private void Pc_onicecandidate(RTCIceCandidate obj)
        {
            string ICe = obj.toJSON();
            SetIceAction.Invoke(ICe);
        }
        /// <summary>
        /// 请求连接
        /// </summary>
        /// <returns></returns>
        public string createOffer()
        {
            var Resp = pc.createOffer(null);
            pc.setLocalDescription(Resp);
            return Resp.toJSON();
        }

        /// <summary>
        /// 请求连接收到回复
        /// </summary>
        /// <param name="answer"></param>
        public void SetAnswer(string answer)
        {
            if (RTCSessionDescriptionInit.TryParse(answer, out var descriptionInit))
            {
                var result = pc.setRemoteDescription(descriptionInit);
            }
        }


        /// <summary>
        /// 收到请求，回复
        /// </summary>
        /// <param name="offer"></param>
        /// <returns></returns>
        public string createAnswer(string offer)
        {
            if (RTCSessionDescriptionInit.TryParse(offer, out var descriptionInit))
            {
                var result = pc.setRemoteDescription(descriptionInit);
            }

            var answerSdp = pc.createAnswer(null);
            pc.setLocalDescription(answerSdp);

            return answerSdp.toJSON();
        }

        private const string ffmpegLibFullPath = @".\\lib";
        /// <summary>
        /// 发送屏幕
        /// </summary>
        public void SetMonitor()
        {
            //string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE, ffmpegLibFullPath, logger);
            //var mediaFileSource = new SIPSorceryMedia.FFmpeg.FFmpegFileSource(MP4_PATH, false, new AudioEncoder());
            List<SIPSorceryMedia.FFmpeg.Monitor> monitors = SIPSorceryMedia.FFmpeg.FFmpegMonitorManager.GetMonitorDevices();
            SIPSorceryMedia.FFmpeg.Monitor primaryMonitor = null;
            if (monitors?.Count > 0)
            {
                foreach (SIPSorceryMedia.FFmpeg.Monitor monitor in monitors)
                {
                    if (monitor.Primary)
                    {
                        primaryMonitor = monitor;
                        break;
                    }
                }
                if (primaryMonitor == null)
                {
                    primaryMonitor = monitors[0];
                }
            }
            SIPSorceryMedia.FFmpeg.FFmpegScreenSource mediaFileSource = null;
            if (primaryMonitor != null)
            {
                mediaFileSource = new SIPSorceryMedia.FFmpeg.FFmpegScreenSource(primaryMonitor.Path, new System.Drawing.Rectangle(primaryMonitor.Rect.X, primaryMonitor.Rect.Y, (((int)(primaryMonitor.Rect.Width * 1)) / 2) * 2, (((int)(primaryMonitor.Rect.Height * 1)) / 2) * 2), 10);
                mediaFileSource.OnVideoSourceError += (msg) => pc.Close(msg);
            }
            else
            {
                throw new NotSupportedException($"Cannot find adequate monitor ...");
            }
            mediaFileSource.RestrictFormats(x => x.Codec == VideoCodecsEnum.H264);
            //mediaFileSource.RestrictFormats(x => x.Codec == AudioCodecsEnum.G722);
            mediaFileSource.OnVideoSourceError += (e) => pc.Close("source eof");

            MediaStreamTrack videoTrack = new MediaStreamTrack(mediaFileSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendRecv);
            pc.addTrack(videoTrack);


            mediaFileSource.OnVideoSourceEncodedSample += pc.SendVideo;
            //mediaFileSource.OnAudioSourceEncodedSample += pc.SendAudio;
            pc.OnVideoFormatsNegotiated += (videoFormats) => mediaFileSource.SetVideoSourceFormat(videoFormats.First());
            //pc.OnAudioFormatsNegotiated += (audioFormats) => mediaFileSource.SetAudioSourceFormat(audioFormats.First());

            pc.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice disconnection");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    await mediaFileSource.CloseVideo();
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    await mediaFileSource.StartVideo();
                }
            };

            //pc.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            //pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            //pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}.");
            //pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");

        }


        /// <summary>
        /// 接收屏幕
        /// </summary>
        public void GetMonitor(MainWindow grid, System.Windows.Controls.Image image)
        {
            SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE, @".\\lib");
            var videoEP = new FFmpegVideoEndPoint();
            videoEP.RestrictFormats(format => format.Codec == VideoCodecsEnum.H264);

            videoEP.OnVideoSinkDecodedSampleFaster += (RawImage rawImage) =>
            {
                if (rawImage.PixelFormat == SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum.Rgb)
                {
                    grid.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (bitmap == null)
                        {
                            bitmap = new WriteableBitmap(rawImage.Width, rawImage.Height, 0, 0, System.Windows.Media.PixelFormats.Bgra32, null);
                            //format = System.Drawing.Imaging.PixelFormat.Format32bppArgb;
                            image.Source = bitmap;
                        }
                        ToBitmapSource3(bitmap, rawImage);

                        //cpu和内存高于上面方法
                        //image.Source = ToBitmapSource2(rawImage);

                        //cpu和内存高于上面方法
                        //image.Source = ToBitmapSource1(rawImage);



                    }));
                }
            };

            videoEP.OnVideoSinkDecodedSample += (byte[] bmp, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat) =>
            {
                //_form.BeginInvoke(new Action(() =>
                //{
                //    if (pixelFormat == SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum.Rgb)
                //    {
                //        if (_picBox.Width != (int)width || _picBox.Height != (int)height)
                //        {
                //            logger.LogDebug($"Adjusting video display from {_picBox.Width}x{_picBox.Height} to {width}x{height}.");
                //            _picBox.Width = (int)width;
                //            _picBox.Height = (int)height;
                //        }

                //        unsafe
                //        {
                //            fixed (byte* s = bmp)
                //            {
                //                Bitmap bmpImage = new Bitmap((int)width, (int)height, (int)(bmp.Length / height), PixelFormat.Format24bppRgb, (IntPtr)s);
                //                _picBox.Image = bmpImage;
                //            }
                //        }
                //    }
                //}));
            };

            MediaStreamTrack videoTrack = new MediaStreamTrack(videoEP.GetVideoSinkFormats(), MediaStreamStatusEnum.RecvOnly);
            //MediaStreamTrack videoTrack = new MediaStreamTrack(new VideoFormat(96, "VP8", 90000, "x-google-max-bitrate=5000000"), MediaStreamStatusEnum.RecvOnly);
            pc.addTrack(videoTrack);

            pc.OnVideoFrameReceived += videoEP.GotVideoFrame;
            pc.OnVideoFormatsNegotiated += (formats) =>
            {
                videoEP.SetVideoSinkFormat(formats.First());
            };

            pc.onconnectionstatechange += async (state) =>
            {
                // logger.LogDebug($"对等连接状态更改为 {state}.");

                if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice 断开");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    await videoEP.CloseVideo();
                }
            };

            //pc.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            // pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP发送 {media}\n{sr.GetDebugSummary()}");
            //pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"RECV STUN {msg.Header.MessageType} (txid: {msg.Header.TransactionId.HexStr()}) from {ep}.");
            //pc.GetRtpChannel().OnStunMessageSent += (msg, ep, isRelay) => logger.LogDebug($"SEND STUN {msg.Header.MessageType} (txid: {msg.Header.TransactionId.HexStr()}) to {ep}.");
            // pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE连接状态更改为 {state}.");


            //List<System.Windows.Media.Color> colors = new List<System.Windows.Media.Color>();
            //colors.Add(System.Windows.Media.Colors.Red);
            //colors.Add(System.Windows.Media.Colors.Blue);
            //colors.Add(System.Windows.Media.Colors.Green);
            ////Add List entries here
            //colors.Add(System.Windows.Media.Color.FromRgb(0, 0, 0));

            //BitmapPalette myPalette = new BitmapPalette(colors);
        }

        public static BitmapImage BitmapToBitmapImage(RawImage rawImage)
        {
            Bitmap bitmap = new Bitmap(rawImage.Width, rawImage.Height, rawImage.Stride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, rawImage.Sample);
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png); // 坑点：格式选Bmp时，不带透明度

                stream.Position = 0;
                BitmapImage result = new BitmapImage();
                result.BeginInit();
                // According to MSDN, "The default OnDemand cache option retains access to the stream until the image is needed."
                // Force the bitmap to load right now so we can dispose the stream.
                result.CacheOption = BitmapCacheOption.OnLoad;
                result.StreamSource = stream;
                result.EndInit();
                result.Freeze();
                return result;
            }
        }


        [DllImport("gdi32")]
        private static extern int DeleteObject(IntPtr o);
        BitmapSource ToBitmapSource1(RawImage rawImage)
        {
            Bitmap bitmap = new Bitmap(rawImage.Width, rawImage.Height, rawImage.Stride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, rawImage.Sample);
            IntPtr ptr = bitmap.GetHbitmap();//obtain the Hbitmap
            BitmapSource bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(ptr, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); //to bitmap resource
            DeleteObject(ptr);//release the HBitmap
            return bs;
        }

        BitmapPalette myPalette = BitmapPalettes.WebPalette;
        public ImageSource ToBitmapSource2(RawImage rawImage)
        {
            Bitmap bitmap = new Bitmap(rawImage.Width, rawImage.Height, rawImage.Stride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, rawImage.Sample);
            var bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            BitmapSource bitmapSource = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgr24, myPalette, bmpData.Scan0, bitmap.Width * bitmap.Height * 3, bitmap.Width * 3);
            bitmap.UnlockBits(bmpData);
            return bitmapSource;
        }


        System.Drawing.Imaging.PixelFormat format = System.Drawing.Imaging.PixelFormat.Format32bppArgb;
        public void ToBitmapSource3(WriteableBitmap bit, RawImage rawImage)
        {
            Bitmap src = new Bitmap(rawImage.Width, rawImage.Height, rawImage.Stride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, rawImage.Sample);
            var data = src.LockBits(new System.Drawing.Rectangle(0, 0, src.Width, src.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, format);
            bit.WritePixels(new Int32Rect(0, 0, src.Width, src.Height), data.Scan0, data.Height * data.Stride, data.Stride, 0, 0);
            src.UnlockBits(data);
            src.Dispose();
        }
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var serilogLogger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
            .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<WebRtc>();
        }


    }
}
