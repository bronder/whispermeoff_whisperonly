using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Linq;

namespace whisperMeOff;

public partial class VuMeterWindow : Window
{
    private const int BarCount = 32;
    private const int BarWidth = 8;
    private const int BarSpacing = 3;
    private readonly System.Windows.Shapes.Rectangle[] _bars = new System.Windows.Shapes.Rectangle[BarCount];
    private readonly double[] _barHeights = new double[BarCount];
    private readonly Random _random = new Random();
    private readonly DispatcherTimer _animationTimer;
    
    public VuMeterWindow()
    {
        InitializeComponent();
        CreateWaveformBars();
        
        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _animationTimer.Tick += AnimationTimer_Tick;
    }

    private void CreateWaveformBars()
    {
        int totalWidth = BarCount * (BarWidth + BarSpacing) - BarSpacing;
        int startX = (int)((320 - 20 - totalWidth) / 2); // Center in canvas (320 width - 20 padding)
        
        var gradient = (LinearGradientBrush)FindResource("WaveGradient");
        
        for (int i = 0; i < BarCount; i++)
        {
            _barHeights[i] = 0;
            
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = BarWidth,
                Height = 0,
                Fill = gradient,
                RadiusX = 2,
                RadiusY = 2,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            Canvas.SetLeft(rect, startX + i * (BarWidth + BarSpacing));
            Canvas.SetBottom(rect, 0);
            
            WaveformCanvas.Children.Add(rect);
            _bars[i] = rect;
        }
    }

    public void SetLevel(int level)
    {
        // Distribute the audio level across multiple bars with variation
        for (int i = 0; i < BarCount; i++)
        {
            // Create a more interesting pattern by varying each bar's response
            double variation = 0.6 + (_random.NextDouble() * 0.6); // 0.6 to 1.2
            double positionFactor = 1.0; // Keep all bars at full height
            
            double targetHeight = (level / 100.0) * 50 * variation * positionFactor; // Max height 50
            _barHeights[i] = targetHeight;
        }
        
        if (!_animationTimer.IsEnabled)
        {
            _animationTimer.Start();
        }
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        bool anyMoving = false;
        
        for (int i = 0; i < BarCount; i++)
        {
            double currentHeight = _bars[i].Height;
            double targetHeight = _barHeights[i];
            
            // Smooth animation
            double newHeight = currentHeight + (targetHeight - currentHeight) * 0.3;
            
            if (Math.Abs(newHeight - currentHeight) > 0.5 || Math.Abs(targetHeight - currentHeight) > 0.5)
            {
                anyMoving = true;
            }
            
            _bars[i].Height = Math.Max(2, newHeight);
            
            // Adjust vertical position to keep bars centered
            Canvas.SetBottom(_bars[i], (50 - _bars[i].Height) / 2);
            
            // Change color based on height
            if (newHeight > 40)
                _bars[i].Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 68, 68)); // Red
            else if (newHeight > 30)
                _bars[i].Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 170, 0)); // Orange
            else
                _bars[i].Fill = (LinearGradientBrush)FindResource("WaveGradient");
        }
        
        if (!anyMoving && _barHeights.All(h => h < 1))
        {
            // Stop animation when everything settled at zero
            _animationTimer.Stop();
        }
    }

    public void PositionAtBottomMiddle()
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        
        Left = (screenWidth - Width) / 2;
        Top = screenHeight - Height - 50;
    }
    
    protected override void OnClosed(EventArgs e)
    {
        _animationTimer.Stop();
        base.OnClosed(e);
    }
}
