using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Windows.Threading;
using G3SDK;
using MathNet.Numerics.LinearAlgebra.Complex;
using OpenCvSharp;
using Task = System.Threading.Tasks.Task;
using System.Net.Sockets;
using System.Net;
using System.Collections;
using System.Windows.Media.Imaging;

namespace G3ToScreenMapper
{
    class SendEyeData : ScreenMapper
    {
        private readonly G3Api _api;
        private readonly Dispatcher _dispatcher;
        private System.Timers.Timer _timer;


        public Matrix eye_data_matrix = new DenseMatrix(10000000, 6);
        public Matrix event_data_matrix = new DenseMatrix(10000, 2);
        public int gaze_data_count = 0;
        public int event_data_count = 0;
        /*
        获取眼动数据:
        @eye_data[gaze_data1,
                 gaze_data2,
                 device_time_stamp,
                 label]


        @event_data[gaze_data1,
            gaze_data2,
            device_time_stamp,
            label]
        */

        private List<double> eye_data = new List<double>(4);
        private List<double> event_data = new List<double>(4);


        private int sample_fre = 100;
        private double dur_one_packet = 0.04;
        private int n_points;

        private Queue MsgQue;   //创建一个消息队列
        private int MaxSize;
        private int online_label = 0;
        private bool IsSend = false;
        private double screen_flag;


        public SendEyeData(G3Api api, Dispatcher dispatcher) : base(api, dispatcher)
        {
            _api = api;
            _dispatcher = dispatcher;
        }


        public void Run()
        {
            ScreenMapperToStart();
            // 获取事件标签
            var eventToken = _api.Rudimentary.SyncPort.Subscribe(eventReceived);
            //获取眼动数据
            var gazeToken = _api.Rudimentary.Gaze.Subscribe(GazeReceived);   //和pythonSOK中的订阅函数相似，GazeReceived类似回调函数


            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += async (sender, args) =>
            {
                try
                {
                    await _api.Rudimentary.Keepalive();
                }
                catch
                {
                    _timer.Stop();
                    if (G3Address != null)
                        _dispatcher.Invoke(() => G3Address.Invoke(this, "眼动仪断开连接！"));
                }

            };
            _timer.Enabled = true;


            n_points = (int)(sample_fre * dur_one_packet);
            MaxSize = n_points * 4;
            MsgQue = new Queue(MaxSize);
            Task.Run(SendMsg);   //进入线程池
        }



        private void eventReceived(G3SyncPortData g3LabelData)
        {
            
            if (g3LabelData.Direction == G3SDK.Direction.In)
            {
                online_label = g3LabelData.Value;
                if (g3LabelData.Value == 1)
                {
                    event_data_matrix[event_data_count, 0] = g3LabelData.Value;
                    event_data_matrix[event_data_count++, 1] = g3LabelData.TimeStamp.TotalMilliseconds;
                    eye_data_matrix[gaze_data_count, 4] = g3LabelData.Value;
                }
                Trace.WriteLine(g3LabelData.Value.ToString());
            }     
        }


        public Vector2 WarpedGaze2D;
        public Vector2 Warped2Gaze2D;
        public Vector2 SendGaze2D;
        private void GazeReceived(G3GazeData g3GazeData)
        {

            //Trace.WriteLine(g3GazeData.Gaze2D.ToString());

            //上下屏注视点投影的结果
            if (_videoToUpScreen != null)
                Warped2Gaze2D = MapFromNormalizedVideoToNormalizedWarpedImage(g3GazeData.Gaze2D, _videoToUpScreen);
            else
                Warped2Gaze2D = Vector2Extensions.INVALID;

            if (_videoToScreen != null)
                WarpedGaze2D = MapFromNormalizedVideoToNormalizedWarpedImage(g3GazeData.Gaze2D, _videoToScreen);
            else
                WarpedGaze2D = Vector2Extensions.INVALID;


            if (Warped2Gaze2D.IsValid())
            {
                SendGaze2D = Warped2Gaze2D;
                screen_flag = 1;
            }

            else if (WarpedGaze2D.IsValid())
            {
                SendGaze2D = WarpedGaze2D;
                screen_flag = 2;
            }
            else
            {
                SendGaze2D = new Vector2(0, 0);
                screen_flag = 0;
            }


            // 接收到发送请求开始收集数据
            if (IsSend)
            {
                eye_data.Add(SendGaze2D.X);
                eye_data.Add(SendGaze2D.Y);
                eye_data.Add(online_label);
                if (online_label == 1)
                {
                    online_label = 0;
                }
                eye_data.Add(screen_flag);
                if (MsgQue.Count < MaxSize)
                {
                    for (int i = 0; i < eye_data.Count; i++)
                    {
                        if (double.IsNaN(eye_data[i]))
                        {
                            int data = 0;
                            MsgQue.Enqueue(data);
                        }
                        else
                        {
                            int data = (int)(eye_data[i] * 10000);
                            MsgQue.Enqueue(data);
                        }
                    }
                    eye_data.Clear();
                }
                else
                {
                    Trace.WriteLine("队列已满！");
                    eye_data[3] = 0;
                    eye_data.Clear();
                }

            }

            //注视点的映射
            if (GazeDataResults != null)
            {
                var gazeResults = new GazeDataResults(
                    g3GazeData.Gaze2D,
                    WarpedGaze2D,
                    Warped2Gaze2D
                    );
                _dispatcher.Invoke(() => GazeDataResults?.Invoke(this, gazeResults));
            }

            eye_data_matrix[gaze_data_count, 0] = SendGaze2D.X;
            eye_data_matrix[gaze_data_count, 1] = SendGaze2D.Y;
            eye_data_matrix[gaze_data_count, 2] = g3GazeData.Gaze2D.X;
            eye_data_matrix[gaze_data_count, 3] = g3GazeData.Gaze2D.Y;
            eye_data_matrix[gaze_data_count++, 5] = g3GazeData.TimeStamp.TotalMilliseconds;
        }

        public event EventHandler<GazeDataResults> GazeDataResults;

        public event EventHandler<string> G3Address;

        public Socket sever_socket;
        
        private Socket client_socket;
        private bool IsConnect = false;
        private byte[] bs = new byte[0];
        public bool IS_Send = false;
        public IPAddress LockIP = IPAddress.Parse("127.0.0.1");
        public int port = 8848;
        // 发送注视点数据
        public void SendMsg()
        {
       
            //IPAddress LockIP = null;

            //IPHostEntry ipEntry = Dns.GetHostEntry(Dns.GetHostName());
            //foreach (var ip in ipEntry.AddressList)
            //{
            //    if (ip.AddressFamily == AddressFamily.InterNetwork)
            //    {
            //        LockIP = ip;
            //    }
            //}
            
            while (true)
            {
                if (IS_Send)
                {
                    IPEndPoint address = new IPEndPoint(LockIP, port);
                    sever_socket.Bind(address);
                    connect_tcp();  //等待连接

                    while (IS_Send)
                    {
                        if (IsConnect)
                        {
                            if (MsgQue.Count == MaxSize)
                            {
                                for (int i = 0; i < MaxSize; i++)
                                {
                                    int Val = (int)MsgQue.Dequeue();
                                    byte[] bytes_val = BitConverter.GetBytes(Int64.Parse(Val.ToString()));

                                    byte[] sum_btyarr = new byte[bs.Length + bytes_val.Length];
                                    Array.Copy(bs, 0, sum_btyarr, 0, bs.Length);
                                    Array.Copy(bytes_val, 0, sum_btyarr, bs.Length, bytes_val.Length);
                                    bs = sum_btyarr;
                                }

                                byte[] bytes_pck_length = BitConverter.GetBytes(Int32.Parse(bs.Length.ToString()));
                                byte[] send_bytesArr = new byte[bytes_pck_length.Length + bs.Length];
                                Array.Copy(bytes_pck_length, 0, send_bytesArr, 0, bytes_pck_length.Length);
                                Array.Copy(bs, 0, send_bytesArr, bytes_pck_length.Length, bs.Length);

                                try
                                {
                                    client_socket.Send(send_bytesArr);
                                    if (G3Address != null)
                                        _dispatcher.Invoke(() => G3Address.Invoke(this, "正在发送数据！"));
                                }
                                catch
                                {
                                    if (G3Address != null)
                                        _dispatcher.Invoke(() => G3Address.Invoke(this, "断开连接，停止发送数据！"));
                                    client_socket.Close();
                                }

                                bs = new byte[0];

                            }
                        }
                    }
                }
                else
                {
                    if (G3Address != null)
                        _dispatcher.Invoke(() => G3Address.Invoke(this, "IP:" + LockIP.ToString() + " Port:" + port.ToString()));
                }

            }

        }

        // 建立TCP连接
        public void connect_tcp()
        {
            try
            {
                sever_socket.Listen();
                client_socket = sever_socket.Accept(); //阻塞
                IsSend = true;
                IsConnect = true;
                if (G3Address != null)
                    _dispatcher.Invoke(() => G3Address.Invoke(this, "连接成功!"));
            }
            catch
            {
                IsConnect = false;
            }
       

        }
    }


    public class GazeDataResults
    {
        public Vector2 WarpedGaze2D { get; }

        public Vector2 Warped2Gaze2D { get; }
        public Vector2 G3GazeDatas { get; }

        public GazeDataResults(Vector2 g3GazeDatas, Vector2 warpedGaze2D, Vector2 warped2Gaze2D)
        {
            G3GazeDatas = g3GazeDatas;
            WarpedGaze2D = warpedGaze2D;
            Warped2Gaze2D = warped2Gaze2D;
        }

    }

}

