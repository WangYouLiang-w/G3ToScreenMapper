using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Forms;
using G3SDK;
using OpenCvSharp;
using Size = OpenCvSharp.Size;
using System.Diagnostics;
using MathNet.Numerics.LinearAlgebra.Complex;
using Matrix = MathNet.Numerics.LinearAlgebra.Complex.Matrix;
using MathNet.Numerics.Data.Matlab;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;
using G3ToScreenMapper.Annotations;
using System.Windows.Resources;
using System.Net.Sockets;

namespace G3ToScreenMapper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>

    public partial class MainWindow
    {
        private G3Api _api;
        private readonly Brush _lineCol = new SolidColorBrush(Colors.Aqua);
        private readonly Brush _textCol = new SolidColorBrush(Colors.OrangeRed);
        private readonly Brush _red = new SolidColorBrush(Colors.Red);
        private readonly Brush _white = new SolidColorBrush(Colors.White);
        private readonly Brush _black = new SolidColorBrush(Colors.Black);
        private readonly ArucoOverlayVM _arucoVM = new ArucoOverlayVM();
        private readonly Screen _screen;
        private SendEyeData _sendEyeData;

        private int GazePointsCount = 0;
        private Point2f LastPoint = new Point2f(0, 0);

        private bool isRecoder = false;
        private string FilePath;

        private bool Is_S = false;
        private bool Is_Start = false;

        ObservableCollection<G3_event> EventList = new ObservableCollection<G3_event>();


        public MainWindow()
        {
            InitializeComponent();
            _screen = Screen.AllScreens[0];
            _arucoVM.SetSize(_screen.WorkingArea.Width, _screen.WorkingArea.Height);

            // SubscribeToSceneCamera();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }

        private void Init30InchScreen()
        {
            _sendEyeData.InitScreen(_screen.WorkingArea.Width, _screen.WorkingArea.Height, _arucoVM.MarkerSize);
            foreach (var m in _arucoVM.MImages)
            {
                _sendEyeData.AddMarkerXY(m.Id, m.X, m.Y);
            }

            foreach (var m in _arucoVM.UImages)
            {
                _sendEyeData.AddUpMarkerXY(m.Id, m.X, m.Y);
            }

        }

        private async void SubscribeToSceneCamera()
        {

            //寻找眼动仪设备
            var browser = new G3Browser();
            _api = null;
            while (_api == null)
            {
                var devices = await browser.ProbeForDevices();
                if (devices.Any())
                    _api = devices.First();
                await Task.Delay(100);
            }
            _btnCalibrate.IsEnabled = true;

            //实例化对象
            _sendEyeData = new SendEyeData(_api, Dispatcher);

            //屏幕映射到图像的结果
            _sendEyeData.OnImageResults += ScreenMapperOnOnImageResults;

            //下屏的投影图像
            _sendEyeData.OnWarpedImage += (sender, source) =>
            {
                _warpedImage.Source = source;
                _warpedImage.Height = source.Height / 2;
                _warpedImage.Width = source.Width / 2;
                _warpedCanvas.Width = _warpedImage.Width;
                _warpedCanvas.Height = _warpedImage.Height;
            };

            //上屏的投影图像
            _sendEyeData.OnWarped2Image += (sender, source) =>
            {
                _warped2Image.Source = source;
                _warped2Image.Height = source.Height / 2;
                _warped2Image.Width = source.Width / 2;
                _warped2Canvas.Width = _warped2Image.Width;
                _warped2Canvas.Height = _warped2Image.Height;
            };

            //原视频图像
            _sendEyeData.OnImage += (sender, source) =>   //匿名委托
            {
                _img.Source = source;
                _img.Height = source.Height / 2;
                _img.Width = source.Width / 2;
                _gazeCanvas.Width = _img.Width;
                _gazeCanvas.Height = _img.Height;
                _markerCanvas.Width = _img.Width;
                _markerCanvas.Height = _img.Height;

                _HeatImage.Source = source;
                _HeatImage.Width = source.Width / 2;
                _HeatImage.Height = source.Height / 2;

                _HeatCanvas.Width = _HeatImage.Width;
                _HeatCanvas.Height = _HeatImage.Height;
            };


            //初始化屏幕：
            Init30InchScreen();


            //视点位置
            _sendEyeData.GazeDataResults += (sender, source) =>
            {
                SetGazeEllipsePos(_gazeMarker, source.G3GazeDatas, _gazeCanvas);
                SetGazeEllipsePos(_gazeMarker2, source.G3GazeDatas, _gazeCanvas);

                SetGazeEllipsePos(_warpedGazeMarker, source.WarpedGaze2D, _warpedCanvas);
                SetGazeEllipsePos(_warpedGazeMarker2, source.WarpedGaze2D, _warpedCanvas);

                SetGazeEllipsePos(_warped2GazeMarker, source.Warped2Gaze2D, _warped2Canvas);
                SetGazeEllipsePos(_warped2GazeMarker2, source.Warped2Gaze2D, _warped2Canvas);

                GazeHeatImageResults(_HeatCanvas, source.G3GazeDatas);
            };

            _sendEyeData.G3Address += (sender, source) =>
            {
                address.Text = source;
            };
            // 计算+发送
            _sendEyeData.Run();

        }
        //---------------------------------------------------------------------------------------------------------//
        private void SetGazeEllipsePos(Ellipse gaze, Vector2 gaze2D, Canvas canvas)
        {

            Canvas.SetLeft(gaze, gaze2D.IsValid() ? (gaze2D.X * canvas.Width) - gaze.Width / 2 : 0);
            Canvas.SetTop(gaze, gaze2D.IsValid() ? (gaze2D.Y * canvas.Height) - gaze.Height / 2 : 0);
        }

        //---------------------------------------------------------------------------------------------------------//

        private void GazeHeatImageResults(Canvas canvas, Vector2 gaze2D)
        {
            if (GazePointsCount == 300)
            {
                _HeatCanvas.Children.Clear();
                GazePointsCount = 0;
            }
            DrawGazePoints(canvas, gaze2D);
        }


        private void DrawGazePoints(Canvas canvas, Vector2 gaze2D)
        {
            double X = 0;
            double Y = 0;
            var GazePoint = new Ellipse { Height = 15, Width = 15, Fill = _red };
            GazePointsCount = GazePointsCount + 1;
            if (gaze2D.IsValid())
            {
                X = (gaze2D.X * canvas.Width) - GazePoint.Width / 2;
                Y = (gaze2D.Y * canvas.Height) - GazePoint.Height / 2;
            }
            else
            {
                X = 0;
                Y = 0;
            }
            Canvas.SetLeft(GazePoint, X);
            Canvas.SetTop(GazePoint, Y);

            canvas.Children.Add(GazePoint);

            if (GazePointsCount > 1)
            {
                canvas.Children.Add(new Line()
                {
                    X1 = LastPoint.X,
                    X2 = X,
                    Y1 = LastPoint.Y,
                    Y2 = Y,
                    Stroke = _black,
                    StrokeThickness = 2
                });
            }
            LastPoint = new Point2f((float)X, (float)Y);
        }


        //---------------------------------------------------------------------------------------------------------//
        private void ScreenMapperOnOnImageResults(object sender, ImageResults e)
        {
            _markerCanvas.Children.Clear();
            DrawCorners(-1, e.MappedScreen, _white, _markerCanvas, e.VideoSize);

            for (var c = 0; c < e.MarkerCorners.Length; c++)
            {
                DrawCorners(e.MarkerIds[c], e.MarkerCorners[c], _lineCol, _markerCanvas, e.VideoSize);
            }

            for (var c = 0; c < e.Rejected.Length; c++)
            {
                DrawCorners(-1, e.Rejected[c], _textCol, _markerCanvas, e.VideoSize);
            }
        }

        private void DrawCorners(int id, Point2f[] corners, Brush color, Canvas canvas, Size videoSize)
        {
            var scaleX = canvas.ActualWidth / videoSize.Width;
            var scaleY = canvas.ActualHeight / videoSize.Height;
            for (int i = 0; i < corners.Length; i++)
            {
                var c1 = corners[i];
                var c2 = corners[(i + 1) % corners.Length];
                canvas.Children.Add(new Line()
                {
                    X1 = c1.X * scaleX,
                    X2 = c2.X * scaleX,
                    Y1 = c1.Y * scaleY,
                    Y2 = c2.Y * scaleY,
                    Stroke = color,
                    StrokeThickness = 2
                });
            }

            if (corners.Length > 0)
            {
                canvas.Children.Add(new Line()
                {
                    X1 = corners[0].X * scaleX - 4,
                    X2 = corners[0].X * scaleX + 4,
                    Y1 = corners[0].Y * scaleY - 4,
                    Y2 = corners[0].Y * scaleY + 4,
                    Stroke = _red,
                    StrokeThickness = 3
                });
                if (id >= 0)
                {
                    var x = corners.Average(corn => corn.X) * scaleX;
                    var y = corners.Average(corn => corn.Y) * scaleY;
                    Text(x, y, id.ToString());
                }
            }
        }

        private void Text(double x, double y, string text)
        {

            var textBlock = new TextBlock { Text = text, Foreground = _textCol, FontSize = 10 };
            Canvas.SetLeft(textBlock, x);
            Canvas.SetTop(textBlock, y);
            _markerCanvas.Children.Add(textBlock);
        }

        //---------------------------------------------控件------------------------------------------------------------//

        private async void Button_Click_Calibrate(object sender, System.Windows.RoutedEventArgs e)
        {
            var res = await _api.Calibrate.Run();
            Dispatcher.Invoke(() =>
            {
                _btnCalibrate.Background = new SolidColorBrush(res ? Colors.Green : Colors.Red);
            });
        }

        private void Button_Click_Recoder(object sender, System.Windows.RoutedEventArgs e)
        {
            isRecoder = !isRecoder;

            if (isRecoder)
            {

                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.Title = "选择文件夹";
                saveFileDialog.OverwritePrompt = true;
                saveFileDialog.AddExtension = true;
                saveFileDialog.DefaultExt = "mat";
                saveFileDialog.Filter = "文件(*.mat)|*.*";   //可选择的文件类型
                if (saveFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    FilePath = saveFileDialog.FileName; //得到保存路径及文件名
                }
                else
                    isRecoder = !isRecoder;
            }

            else //停止记录 则保存数据
            {

                IList<Matrix> matrices = new List<Matrix>();
                matrices.Add((Matrix)_sendEyeData.eye_data_matrix.Resize(_sendEyeData.gaze_data_count, 6));
                matrices.Add((Matrix)_sendEyeData.event_data_matrix.Resize(_sendEyeData.event_data_count, 2));

                IList<string> names = new List<string>();
                names.Add("eye_data");
                names.Add("event_data");


                if (matrices.Count != names.Count)
                {
                    throw new ArgumentException("Each matrix must have a name. Number of matrices must equal to the number of names.");
                }

                MatlabWriter.Store(FilePath, matrices.Zip(names, new Func<Matrix, string, MatlabMatrix>(MatlabWriter.Pack)));

                //保存完置零
                _sendEyeData.eye_data_matrix = new DenseMatrix(10000000, 6);
                _sendEyeData.event_data_matrix = new DenseMatrix(10000, 2);
                _sendEyeData.gaze_data_count = 0;
                _sendEyeData.event_data_count = 0;

            }

            Dispatcher.Invoke(() =>
            {
                _btnRecoder.Background = new SolidColorBrush(isRecoder ? Colors.Red : Colors.DarkGray);   //按键变红表示正在记录
            });
        }

        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void WIndow_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {

        }

        private void _SendEyeData_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Is_S = !Is_S;
            if (Is_S)
            {
                _sendEyeData.sever_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _sendEyeData.IS_Send = true;
            }
                
            else
            {
                _sendEyeData.IS_Send = false;
                _sendEyeData.sever_socket.Close();
            }
               
            Dispatcher.Invoke(() =>
            {
                _SendEyeData.Background = new SolidColorBrush(Is_S ? Colors.Red : Colors.DarkGray);   //按键变红表示正在记录
            });
        }

        private void _start_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Is_Start = !Is_Start;
            Dispatcher.Invoke(() =>
            {
                _start.Background = new SolidColorBrush(Is_Start ? Colors.White: Colors.DarkGray);   //按键变红表示正在记录
            });

            if (Is_Start)
                SubscribeToSceneCamera();
            else
            {
                _sendEyeData.sever_socket.Close();

                Dispatcher.Invoke(() =>
                {
                    _btnCalibrate.Background = new SolidColorBrush(Colors.DarkGray);
                });
            }
            
        }
    }

    public class G3_event : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string event_value;
        private string event_TimeStamp;


        public string _event_value
        {
            get { return event_value; }
            set
            {
                event_value = value;
                OnPropertyChanged(event_value);
            }
        }


        public string _event_TimeStamp
        {
            get { return event_TimeStamp; }
            set
            {
                event_TimeStamp = value;
                OnPropertyChanged(event_TimeStamp);
            }
        }

        [Annotations.NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }


}

