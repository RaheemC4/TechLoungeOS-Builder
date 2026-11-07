using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace TechLoungeOS;

public partial class MainWindow
{
    private int currentStep;
    private void NextStepClick(object sender,RoutedEventArgs e){if(!running && !recoveryRequired && currentStep<4)ShowStep(currentStep+1);}
    private void PreviousStepClick(object sender,RoutedEventArgs e){if(!running && !recoveryRequired && currentStep>0)ShowStep(currentStep-1);}
    private void StepChecked(object sender, RoutedEventArgs e)
    {
        if (SourcePage is not null) ShowStep(int.Parse((string)((RadioButton)sender).Tag));
    }

    private void ShowStep(int index)
    {
        FrameworkElement[] pages = [SourcePage, ComponentsPage, AppsPage, PersonalisationPage, ExportPage];
        var titles = new[] { "Windows image", "Remove optional components", "Apps & setup", "Personalisation", "Finish" };
        var descriptions = new[] { "Choose a Windows 11 ISO to customise.", "Remove optional extras while keeping the essentials protected.", "Choose optional apps to finish installing quietly after your desktop opens.", "Choose your wallpaper, region and keyboard.", "Choose a setup USB, a saved ISO, or both." };
        if (index < 0 || index >= pages.Length) throw new ArgumentOutOfRangeException(nameof(index));
        currentStep=index;
        for (var i = 0; i < pages.Length; i++) pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        ((RadioButton)Navigation.Children[index]).IsChecked = true;
        StepTitle.Text = titles[index]; StepDescription.Text = descriptions[index]; SettingsScroll.ScrollToTop();
        BuildButton.Visibility=index==4?Visibility.Visible:Visibility.Collapsed;
        NextStepButton.Visibility=index<4?Visibility.Visible:Visibility.Collapsed;
        PreviousStepButton.Visibility=index>0?Visibility.Visible:Visibility.Collapsed;
        NextStepButton.IsEnabled=!running && !recoveryRequired && (index!=0 || Editions.SelectedItem is Edition);
        StepTitle.BeginAnimation(OpacityProperty, null); StepDescription.BeginAnimation(OpacityProperty, null);
        var page = pages[index];
        page.BeginAnimation(OpacityProperty, null);
        if (page.RenderTransform is not System.Windows.Media.TranslateTransform offset)
            page.RenderTransform = offset = new System.Windows.Media.TranslateTransform();
        offset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            StepTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(.7, 1, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.Stop });
            StepDescription.BeginAnimation(OpacityProperty, new DoubleAnimation(.8, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(.85, 1, TimeSpan.FromMilliseconds(100)) { FillBehavior = FillBehavior.Stop });
            offset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(130)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }
    }
}
