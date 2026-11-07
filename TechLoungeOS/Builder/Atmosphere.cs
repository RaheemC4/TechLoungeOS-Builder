using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TechLoungeOS;

public partial class MainWindow
{
    internal void RefreshMotion()
    {
        SetUpdateCheckingMotion(checkingUpdate);
        SetAtmosphereMotion(SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast &&
            IsVisible && IsActive && WindowState != WindowState.Minimized);
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
        {
            foreach (var page in new FrameworkElement[] { SourcePage, ComponentsPage, AppsPage, PersonalisationPage, ExportPage })
            {
                page.BeginAnimation(OpacityProperty, null);
                if (page.RenderTransform is TranslateTransform offset) offset.BeginAnimation(TranslateTransform.YProperty, null);
            }
            StepTitle.BeginAnimation(OpacityProperty, null); StepDescription.BeginAnimation(OpacityProperty, null);
            UpdateFeedback.BeginAnimation(OpacityProperty,null);PreviousUpdateFeedback.BeginAnimation(OpacityProperty,null);
            UpdateFeedback.Opacity=1;PreviousUpdateFeedback.Opacity=0;
        }
    }

    private bool atmosphereMoving;
    private void SetAtmosphereMotion(bool enabled)
    {
        enabled &= !SystemParameters.HighContrast;
        AtmosphereLayer.Visibility = SystemParameters.HighContrast ? Visibility.Collapsed : Visibility.Visible;
        if (atmosphereMoving == enabled) return;
        atmosphereMoving = enabled;
        AtmosphereBrush.BeginAnimation(RadialGradientBrush.CenterProperty, null);
        AtmosphereBrush.BeginAnimation(RadialGradientBrush.GradientOriginProperty, null);
        if (!enabled) return;
        var drift = new PointAnimation(new Point(.9, .05), new Point(.6, .55), TimeSpan.FromSeconds(24))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        // Animate brush coordinates only, with no layout changes, blur filters or per-frame event handlers.
        AtmosphereBrush.BeginAnimation(RadialGradientBrush.CenterProperty, drift);
        AtmosphereBrush.BeginAnimation(RadialGradientBrush.GradientOriginProperty, drift);
    }
}
