using System.Collections.Generic;
using OpenCvSharp;

namespace G3ToScreenMapper
{
    public class ScreenWithMarkers
    {

        private readonly Dictionary<int, MarkerPos> _markers = new Dictionary<int, MarkerPos>();
        private IEnumerable<Point2f> _coords;
        public int Width { get; }
        public int Height { get; }
        public float MarkerSize { get; }

        //构造函数
        public ScreenWithMarkers(int width, int height, float markerSize)  
        {
            Width = width;
            Height = height;
            MarkerSize = markerSize;
            //maker的定位
            _coords = new[] {                      
                new Point2f(0, 0),
                new Point2f(Width, 0),
                new Point2f(Width, Height),
                new Point2f(0, Height) };
        }


        public IReadOnlyDictionary<int, MarkerPos> Markers => _markers;
        public IEnumerable<Point2f> Coords => _coords;

        public void AddMarker(int id, Pos p, int from00)                                                               
        {
            _markers[id] = new MarkerPos(id, p, from00, this);
        }

        public void AddMarkerXY(int id, float x, float y)
        {
            _markers[id] = new MarkerPos(id, x, y, this);
        }
    }


    public class UpScreenWithMarkers
    {

        private readonly Dictionary<int, MarkerPosUp> _markers = new Dictionary<int, MarkerPosUp>();
        private IEnumerable<Point2f> _coords;
        public int Width { get; }
        public int Height { get; }
        public float MarkerSize { get; }

        //构造函数
        public UpScreenWithMarkers(int width, int height, float markerSize)
        {
            Width = width;
            Height = height;
            MarkerSize = markerSize;
            //maker的定位
            _coords = new[] {
                new Point2f(0, 0),
                new Point2f(Width, 0),
                new Point2f(Width, Height),
                new Point2f(0, Height) };
        }


        public IReadOnlyDictionary<int, MarkerPosUp> Markers => _markers;
        public IEnumerable<Point2f> Coords => _coords;

        public void AddMarker(int id, Pos p, int from00)
        {
            _markers[id] = new MarkerPosUp(id, p, from00, this);
        }

        public void AddMarkerXY(int id, float x, float y)
        {
            _markers[id] = new MarkerPosUp(id, x, y, this);
        }
    }
}