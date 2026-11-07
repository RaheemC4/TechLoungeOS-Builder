using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace TechLoungeOS;

// Native Toggle semantics, stable row identity and short composited transitions.
internal sealed class AppChoice : ToggleButton
{
    private int transition;
    private bool? selectionVisible;
    private double? selectionMinimum;
    private Thickness? selectionSpacing;
    private static bool Motion => SystemParameters.ClientAreaAnimation&&!SystemParameters.HighContrast;
    public AppChoice() => SetResourceReference(StyleProperty,"AppChoice");
    public override void OnApplyTemplate(){base.OnApplyTemplate();UpdateVisuals(false);}
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if(e.Property==IsCheckedProperty||e.Property==IsMouseOverProperty||e.Property==IsPressedProperty||e.Property==IsKeyboardFocusedProperty||e.Property==IsEnabledProperty)UpdateVisuals(IsLoaded);
    }
    private static void Fade(UIElement target,double value,int milliseconds,bool animate)
    {
        var from=target.Opacity;target.BeginAnimation(OpacityProperty,null);target.Opacity=value;
        if(animate&&Motion&&from!=value)target.BeginAnimation(OpacityProperty,new DoubleAnimation(from,value,TimeSpan.FromMilliseconds(milliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
    }
    private void UpdateVisuals(bool animate)
    {
        foreach(var (name,value,duration) in new[]{("Selected",IsChecked==true?1d:0d,160),("Check",IsChecked==true?1d:0d,120),("Hover",IsEnabled&&IsMouseOver?(IsPressed ? 0.10 : 0.045):0d,IsPressed?65:120),("Focus",IsKeyboardFocused?1d:0d,100)})
            if(GetTemplateChild(name) is UIElement layer)Fade(layer,value,duration,animate);
    }
    internal void SelectionVisibility(bool visible,Action remove)
    {
        if(selectionVisible==visible)return;
        selectionMinimum??=MinHeight;selectionSpacing??=Margin;
        selectionVisible=visible;
        var version=++transition;
        IsHitTestVisible=visible;
        Focusable=visible;MinHeight=visible?selectionMinimum.Value:0;
        if(!Motion||!IsLoaded)
        {
            BeginAnimation(MaxHeightProperty,null);BeginAnimation(MarginProperty,null);BeginAnimation(OpacityProperty,null);
            MaxHeight=double.PositiveInfinity;Margin=selectionSpacing.Value;Opacity=1;
            if(!visible)remove();return;
        }
        var from=ActualHeight;BeginAnimation(MaxHeightProperty,null);BeginAnimation(MarginProperty,null);
        MaxHeight=visible?double.PositiveInfinity:0;
        var duration=TimeSpan.FromMilliseconds(150);
        if(visible)
        {
            Fade(this,1,130,true);Margin=selectionSpacing.Value;return;
        }
        Fade(this,0,130,true);
        var collapse=new DoubleAnimation(from,0,duration){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
        collapse.Completed+=(_,_)=>{if(version==transition)remove();};
        BeginAnimation(MaxHeightProperty,collapse);
        var margin=Margin;Margin=new Thickness(0);
        BeginAnimation(MarginProperty,new ThicknessAnimation(margin,new Thickness(0),duration){FillBehavior=FillBehavior.Stop});
    }
}
