using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TechLoungeOS;

internal static class ActivityPresentation
{
    internal static bool Motion=>SystemParameters.ClientAreaAnimation&&!SystemParameters.HighContrast;
    internal static void Caption(FrameworkElement view,bool clock=false)
    {
        if(!Motion||!view.IsVisible)return;
        view.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(clock ? .88 : .65,1,TimeSpan.FromMilliseconds(clock?120:190)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
    }
}

internal sealed class ActivityCard : Border
{
    private static readonly ConditionalWeakTable<object,object> Appeared=new();
    public ActivityCard()
    {
        Loaded+=(_,_)=>
        {
            if(!IsVisible||DataContext is not ActivityEntry entry||!Appeared.TryAdd(entry.MotionKey,new object())||!ActivityPresentation.Motion)return;
            var height=ActualHeight;if(height<=0)return;
            var duration=TimeSpan.FromMilliseconds(220);var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
            BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,duration){EasingFunction=ease,FillBehavior=FillBehavior.Stop});
            BeginAnimation(MaxHeightProperty,new DoubleAnimation(0,height,duration){EasingFunction=ease,FillBehavior=FillBehavior.Stop});
            var move=new TranslateTransform();RenderTransform=move;
            move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(6,0,duration){EasingFunction=ease,FillBehavior=FillBehavior.Stop});
        };
        Unloaded+=(_,_)=>{BeginAnimation(OpacityProperty,null);BeginAnimation(MaxHeightProperty,null);if(RenderTransform is TranslateTransform move)move.BeginAnimation(TranslateTransform.YProperty,null);};
    }
}

internal sealed class ActivityIndicator : ProgressBar
{
    public ActivityIndicator(){SetResourceReference(ForegroundProperty,"Accent");SetResourceReference(BackgroundProperty,"Line");Height=3;Minimum=0;Maximum=100;Value=100;IsIndeterminate=ActivityPresentation.Motion;Focusable=false;IsHitTestVisible=false;}
}
