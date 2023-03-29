using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using G3SDK;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using Task = System.Threading.Tasks.Task;

namespace G3ToScreenMapper
{
    
    public class ScreenMapper
    {
        private readonly G3Api _api;
        private readonly Dispatcher _dispatcher;
        private VideoCapture _rtspSource;
        private ScreenWithMarkers _screen;
        private UpScreenWithMarkers _upscreen;


        public Mat _videoToScreen;
        public Mat _screenToVideo;
        public Size _videoSize;
        public Mat _videoToWarped;
        public Mat _videoToUpScreen;
        public Mat _CaptureImag;
        public Mat _warpedImage;
        public Mat _warped2Image;

        public ScreenMapper(G3Api api, Dispatcher dispatcher)
        {
            _api = api;
            _dispatcher = dispatcher;
        }

        public void ScreenMapperToStart()
        {
            _rtspSource = new VideoCapture(_api.LiveRtspUrl(), VideoCaptureAPIs.FFMPEG);   //借助opencv
            Task.Run(ListenToFrames);   //进入线程池
        }

        //实时获取视频帧
        private void ListenToFrames()
        {
         
            while (true)
            {
                var g = _rtspSource.Grab();
                if (g)
                {
                    _CaptureImag = _rtspSource.RetrieveMat();
                    if (_videoSize.Width == 0)
                        _videoSize = new Size(_CaptureImag.Width, _CaptureImag.Height);
                    FindMarkersInImage(_CaptureImag);    //寻找当前帧，标记的位置
                }
               Task.Delay(5);
            }
        }

        private void FindMarkersInImage(Mat openCvImage)
        {
         
            var sw = new Stopwatch();  //用于测量运行时间

            sw.Start();
            //使用默认值初始化检测器的参数
            var parameters = DetectorParameters.Create();

            //字典用于表示aruco标记具有怎样的尺寸、编码等
            var dict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict6X6_1000); //包含了1000种6*6位标记的字典

            //标记码寻找
            CvAruco.DetectMarkers(openCvImage, dict, out var markers, out var ids, parameters, out var rejected);

            var VideoPoints = new List<Point2d>();   //标记在视频中的位置（存放角点、右上、顺时针）
            var screenPoints = new List<Point2d>();  //标记在下屏上的位置

            var upscreenPoints = new List<Point2d>();  //标记在上屏上的位置
            var UpVideoPoints = new List<Point2d>();  //上屏标记在视频中的位置


            // 寻找下屏标记位置
            for (int i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (_screen.Markers.ContainsKey(id))   //查看检测到的码，和下屏上的码是否相同
                {
                    var videoMarker = markers[i];
                    foreach (var videoMarkerCorners in videoMarker)
                    {
                        VideoPoints.Add(new Point2d(videoMarkerCorners.X, videoMarkerCorners.Y));
                    }

                   
                    var screenMarker = _screen.Markers[id];
                    foreach (var screenMarkerCorner in screenMarker.Corners)
                        screenPoints.Add(new Point2d(screenMarkerCorner.X, screenMarkerCorner.Y));
                }
            }

            // 寻找上屏标记位置
            for (int i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (_upscreen.Markers.ContainsKey(id))   //查看检测到的码，和下屏上的码是否相同
                {
                    var videoMarker = markers[i];
                    foreach (var videoMarkerCorners in videoMarker)
                    {
                        UpVideoPoints.Add(new Point2d(videoMarkerCorners.X, videoMarkerCorners.Y));
                    }

                    
                    var screenMarker = _upscreen.Markers[id];
                    foreach (var screenMarkerCorner in screenMarker.Corners)
                        upscreenPoints.Add(new Point2d(screenMarkerCorner.X, screenMarkerCorner.Y));
                }
            }
            // 投影点坐标
            var warpedPoints = screenPoints.Select(ScreenToWarped).ToList();

            if (screenPoints.Count >= 3)
            {
                var t = Cv2.FindHomography(VideoPoints, screenPoints); //计算二维点对之间的最优单射变换矩阵 视频->屏幕
                if (t.Cols == 3)
                {
                    _videoToScreen = t;
                    //_screenToVideo = Cv2.FindHomography(screenPoints, VideoPoints);
                    //_videoToWarped = Cv2.FindHomography(VideoPoints, warpedPoints);
                }
            }

            if (upscreenPoints.Count >= 3)
            {
                var t = Cv2.FindHomography(UpVideoPoints, upscreenPoints); //计算二维点对之间的最优单射变换矩阵 视频->屏幕
                if (t.Cols == 3)
                {
                    _videoToUpScreen = t;
                }
            }


            //*************************************************************************//
            //-------------------------------------------------------------------------//


            // 在上位机上的显示原视频帧
            if (OnImage != null)
            {
                _dispatcher.Invoke(() => OnImage?.Invoke(this, MatToBitmap(openCvImage)));
            }

            // 在上位机上的显示原视频帧中下屏的投影变换
            if (_videoToScreen != null && OnWarpedImage != null)
            {
                _warpedImage = openCvImage.EmptyClone();
                //透视变换 src：输入图像 dst：输出图像 ；透视变换矩阵；输出图像的大小
                Cv2.WarpPerspective(openCvImage, _warpedImage, _videoToScreen, _warpedImage.Size());
                _dispatcher.Invoke(() => OnWarpedImage?.Invoke(this, MatToBitmap(_warpedImage)));
            }


            // 在上位机上的显示原视频帧中上屏的投影变换
            if (_videoToUpScreen != null && OnWarped2Image != null)
            {
                _warped2Image = openCvImage.EmptyClone();
                //透视变换 src：输入图像 dst：输出图像 ；透视变换矩阵；输出图像的大小
                Cv2.WarpPerspective(openCvImage, _warped2Image, _videoToUpScreen, _warped2Image.Size());
                _dispatcher.Invoke(() => OnWarped2Image?.Invoke(this, MatToBitmap(_warped2Image)));
            }

            if (OnImageResults != null)
            {
                var imageResults = new ImageResults(
                    _screenToVideo != null ? Cv2.PerspectiveTransform(_screen.Coords, _screenToVideo) : new Point2f[0],
                    markers,
                    rejected,
                    ids, 
                    _videoSize);
                _dispatcher.Invoke(() => OnImageResults?.Invoke(this, imageResults));
            }
            sw.Stop();


            //TimeSpan ts = sw.Elapsed;
            //Trace.WriteLine(ts.TotalSeconds.ToString());
        }

        // 注视点的投影变换
        public Vector2 MapFromNormalizedVideoToNormalizedWarpedImage(Vector2 normalizedGaze2D, Mat videoToScreen)
        {
            if (normalizedGaze2D.IsValid() && videoToScreen != null)
            {
                var gazeInVideoPixels = new Point2f(normalizedGaze2D.X * _videoSize.Width, normalizedGaze2D.Y * _videoSize.Height);

                var gazeInWarpedCoords = Cv2.PerspectiveTransform(new[] { gazeInVideoPixels }, videoToScreen).Last();

                if ((gazeInWarpedCoords.X / _videoSize.Width < 1) && (gazeInWarpedCoords.X / _videoSize.Width > 0) && (gazeInWarpedCoords.Y / _videoSize.Height < 1) && (gazeInWarpedCoords.Y / _videoSize.Height > 0))
                    return new Vector2(gazeInWarpedCoords.X / _videoSize.Width, gazeInWarpedCoords.Y / _videoSize.Height);
                else
                    return Vector2Extensions.INVALID;
            }
            return Vector2Extensions.INVALID;
        }
                                                                        



        private Point2d ScreenToWarped(Point2d p)
        {
            return new Point2d(((p.X / _screen.Width + 1d) / 3) * _videoSize.Width, ((p.Y / _screen.Height + 1d) / 3) * _videoSize.Height);
        }


        public event EventHandler<BitmapSource> OnWarpedImage;
        public event EventHandler<BitmapSource> OnImage;
        public event EventHandler<BitmapSource> OnWarped2Image;
        public static BitmapSource MatToBitmap(Mat image)
        {
            return OpenCvSharp.WpfExtensions.BitmapSourceConverter.ToBitmapSource(image);
        }


        public event EventHandler<ImageResults> OnImageResults;

        public void InitScreen(int width, int height, float markerSize)
        {
            _screen = new ScreenWithMarkers(width, height, markerSize);
            _upscreen = new UpScreenWithMarkers(width, height, markerSize);
        }


        public void AddMarkerXY(int id, float x, float y)
        {
            _screen.AddMarkerXY(id, x, y);
        }

        public void AddUpMarkerXY(int id, float x, float y)
        {
            _upscreen.AddMarkerXY(id, x, y);
        }




    }

    public class ImageResults
    {
        public Point2f[] MappedScreen { get; }
        public Point2f[][] MarkerCorners { get; }
        public Point2f[][] Rejected { get; }
        public int[] MarkerIds { get; }
        public Size VideoSize { get; }

        public ImageResults(Point2f[] mappedScreen, Point2f[][] markerCorners, Point2f[][] rejected, int[] markerIds,
            Size videoSize)
        {
            MappedScreen = mappedScreen;
            MarkerCorners = markerCorners;
            Rejected = rejected;
            MarkerIds = markerIds;
            VideoSize = videoSize;
        }
    }



}