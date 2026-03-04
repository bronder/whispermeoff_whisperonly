using System.Windows;

namespace whisperMeOff;

public partial class VuMeterWindow : Window
{
    public VuMeterWindow()
    {
        InitializeComponent();
    }

    public void SetLevel(int level)
    {
        VuMeter.Value = level >= 0 ? level : 0;
        
        // Change color based on level
        if (level > 80)
            VuMeter.Foreground = System.Windows.Media.Brushes.Red;
        else if (level > 60)
            VuMeter.Foreground = System.Windows.Media.Brushes.Orange;
        else
            VuMeter.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 0));
    }

    public void PositionAtBottomMiddle()
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        
        Left = (screenWidth - Width) / 2;
        Top = screenHeight - Height - 50; // 50 pixels from bottom
    }
}
