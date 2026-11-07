using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TechLoungeOS;

public partial class MainWindow
{
    private bool AnimateUpdateFeedback=>SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast && IsVisible;
    private void SetUpdateCheckingMotion(bool checking)
    {
        var rotation=(RotateTransform)UpdateCheckIcon.RenderTransform;
        rotation.BeginAnimation(RotateTransform.AngleProperty,null);
        if(checking && AnimateUpdateFeedback)
            rotation.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromMilliseconds(1100)){RepeatBehavior=RepeatBehavior.Forever});
        else rotation.Angle=0;
    }
    private void SetUpdateFeedback(string summary)
    {
        if(UpdateFeedback.Text==summary)return;
        var previous=UpdateFeedback.Text;
        var reverse=PreviousUpdateFeedback.Text==summary && PreviousUpdateFeedback.Opacity>0;
        var incoming=reverse?PreviousUpdateFeedback.Opacity:0;
        var outgoing=reverse?UpdateFeedback.Opacity:1;
        UpdateFeedback.BeginAnimation(OpacityProperty,null);
        PreviousUpdateFeedback.BeginAnimation(OpacityProperty,null);
        PreviousUpdateFeedback.Text=previous;UpdateFeedback.Text=summary;
        // Both labels share a reserved slot. Opacity never changes layout or moves the button.
        if(!AnimateUpdateFeedback){PreviousUpdateFeedback.Opacity=0;UpdateFeedback.Opacity=1;return;}
        PreviousUpdateFeedback.Opacity=0;UpdateFeedback.Opacity=1;
        var duration=TimeSpan.FromMilliseconds(180);
        var ease=new CubicEase{EasingMode=EasingMode.EaseInOut};
        PreviousUpdateFeedback.BeginAnimation(OpacityProperty,new DoubleAnimation(previous.Length==0?0:outgoing,0,duration){EasingFunction=ease,FillBehavior=FillBehavior.Stop});
        UpdateFeedback.BeginAnimation(OpacityProperty,new DoubleAnimation(incoming,1,duration){EasingFunction=ease,FillBehavior=FillBehavior.Stop});
    }
}
