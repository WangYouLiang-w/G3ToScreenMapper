using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using G3ToScreenMapper.Annotations;

namespace G3ToScreenMapper
{
    public class ArucoOverlayVM : INotifyPropertyChanged
    {
        private float _screenHeight;
        private float _screenWidth;
        private readonly int _markerSize = 120;
        private readonly int _markerMargin = 25;
        private string _mousePos;
        private readonly int _borderSize = 5;
        private readonly int _markersPerGap = 4;
        public event PropertyChangedEventHandler PropertyChanged;

        public ArucoOverlayVM()
        {
        }


        public string MousePos
        {
            get => _mousePos;
            set
            {
                if (value == _mousePos) return;
                _mousePos = value;
                OnPropertyChanged();
            }
        }

        private void InitalizeImages()
        {
            var dict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict6X6_1000);
            var markerId = 0;
            float totalSize = MarkerSize + _borderSize * 2;
    
            // Add upper/lower markers
            var first = _markerMargin; //5
            var last = Width - MarkerSize - _markerMargin; //1920-120-5

            var horizMarkerCount = (int)Math.Round(Width / (MarkerSize * _markersPerGap));

            var xstep = (int)(last - first) / (horizMarkerCount - 1);
            //上屏Aruco码所在位置
            for (int i = 0; i < horizMarkerCount; i++)
            {
            
                var x = xstep * i;
                UImages.Add(new ArucoImageVM(dict, markerId++, MarkerSize, x + _markerMargin, _markerMargin, _borderSize)); 
                UImages.Add(new ArucoImageVM(dict, markerId++, MarkerSize, x + _markerMargin, Height - totalSize - _markerMargin, _borderSize));
            }

            //下屏Aruco码所在位置
            for (int i = 0; i < horizMarkerCount; i++)
            {
                var x = xstep * i;
                MImages.Add(new ArucoImageVM(dict, markerId++, MarkerSize, x + _markerMargin, _markerMargin, _borderSize));
                MImages.Add(new ArucoImageVM(dict, markerId++, MarkerSize, x + _markerMargin, Height - totalSize - _markerMargin, _borderSize));
                //if (i == 1)
                //{
                //    MImages.Add(new ArucoImageVM(dict, markerId++, MarkerSize, x - xstep / 3 + _markerMargin, _markerMargin, _borderSize));
                //    MImages.Add(new ArucoImageVM(dict, markerId++, MarkerSize, x + _markerMargin, Height - totalSize - _markerMargin, _borderSize));
                //}
                //else if (i == 2)
                //{
                //    MImages.Add(new ArucoImageVM(dict, markerId++, MarkerSize, x + xstep / 3 + _markerMargin, _markerMargin, _borderSize));
                //    MImages.Add(new ArucoImageVM(dict, markerId++, MarkerSize, x + _markerMargin, Height - totalSize - _markerMargin, _borderSize));
                //}
                //else if (i == 3)
                //{
                //    MImages.Add(new ArucoImageVM(dict, markerId++, MarkerSize, x - xstep / 3 + _markerMargin, _markerMargin, _borderSize));
                //    MImages.Add(new ArucoImageVM(dict, markerId++, MarkerSize, x - xstep / 3 + _markerMargin, Height - totalSize - _markerMargin, _borderSize));
                //}
                //else
                //{
                //    MImages.Add(new ArucoImageVM(dict, markerId++, MarkerSize, x + _markerMargin, _markerMargin, _borderSize));
                //    MImages.Add(new ArucoImageVM(dict, markerId++, MarkerSize, x + _markerMargin, Height - totalSize - _markerMargin, _borderSize));
                //}
            }


            // Add left/right markers
            //last = Height - MarkerSize - _markerMargin;
            //var vertMarkerCount = (int)Math.Round(Height / (MarkerSize * _markersPerGap)); ;
            //var ystep = (last - first) / (vertMarkerCount - 1);
            //for (int i = 1; i < vertMarkerCount-1; i++)
            //{
            //    var y = ystep * i;
            //    Images.Add(new ArucoImageVM(dict, markerId++, MarkerSize, _markerMargin, _markerMargin + y, _borderSize));
            //    Images.Add(new ArucoImageVM(dict, markerId++, MarkerSize, Width - totalSize - _markerMargin, _markerMargin + y, _borderSize));
            //}
        }

        public ObservableCollection<ArucoImageVM> Images { get; } = new ObservableCollection<ArucoImageVM>();
        public ObservableCollection<ArucoImageVM> MImages { get; } = new ObservableCollection<ArucoImageVM>();
        public ObservableCollection<ArucoImageVM> UImages { get; } = new ObservableCollection<ArucoImageVM>();
        public int MarkerSize => _markerSize;
        public float Width
        {
            get => _screenWidth;
            set { _screenWidth = value; }
        }

        public float Height
        {
            get => _screenHeight;
            set { _screenHeight = value; }
        }

        [NotifyPropertyChangedInvocator] 
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void SetSize(float actualWidth, float actualHeight)
        {
            Width = actualWidth;
            Height = actualHeight;
            InitalizeImages();
        }
    }

    public class ArucoImageVM : INotifyPropertyChanged
    {
        private bool _visible = true;
        public BitmapSource Img { get; }
        public float X { get; }
        public float Y { get; }
        public int MarkerSize { get; }
        public int BorderSize { get; }
        public int Id { get; }

        public float CenterX => X + BorderSize + MarkerSize / 2;
        public float CenterY => Y + BorderSize + MarkerSize / 2;

        public Vector2[] GetCorners()
        {
            return new Vector2[]
            {
                new Vector2(X +BorderSize, Y+BorderSize),
            };
        }

        public bool Visible
        {
            get => _visible;
            internal set
            {
                if (value == _visible) return;
                _visible = value;
                OnPropertyChanged();
            }
        }

        public ArucoImageVM(Dictionary dict, int id, int markerSize, float x, float y, int borderSize)
        {

            Img = CreateMarkerImage(dict, id, markerSize);
            X = x;
            Y = y;
            MarkerSize = markerSize;
            BorderSize = borderSize;
            Id = id;
        }

        private static BitmapSource CreateMarkerImage(Dictionary dict, int i, int markerSize)
        {
            var mat = new Mat();
            var outputArray = OutputArray.Create(mat);
            CvAruco.DrawMarker(dict, i, markerSize, outputArray);

            var img = OpenCvSharp.WpfExtensions.BitmapSourceConverter.ToBitmapSource(mat);
            return img;
        }


        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}