using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace TechLoungeOS;

// One native editable dropdown: typing filters its own list, selecting commits a choice.
public sealed class ChoicePicker:ComboBox
{
    private TextBox? editor;
    private bool changing;
    public SetupRegional.Choice? Choice{get;private set;}
    public ChoicePicker()
    {
        IsEditable=true;IsTextSearchEnabled=false;StaysOpenOnEdit=true;
        SetResourceReference(StyleProperty,typeof(ComboBox));
    }
    public override void OnApplyTemplate()
    {
        if(editor is not null)editor.TextChanged-=SearchChanged;
        base.OnApplyTemplate();editor=GetTemplateChild("PART_EditableTextBox") as TextBox;
        if(editor is not null)editor.TextChanged+=SearchChanged;
    }
    protected override void OnItemsSourceChanged(IEnumerable oldValue,IEnumerable newValue)
    {Choice=null;base.OnItemsSourceChanged(oldValue,newValue);}
    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        if(!changing && SelectedItem is SetupRegional.Choice selected)Choice=selected;
    }
    private void SearchChanged(object sender,TextChangedEventArgs e)
    {
        if(changing || editor?.IsKeyboardFocusWithin!=true || ItemsSource is null)return;
        if(SelectedItem is SetupRegional.Choice selected && editor.Text==selected.Label)return;
        var query=editor.Text;var caret=editor.CaretIndex;
        FilterChoices(query);editor.CaretIndex=Math.Min(caret,query.Length);IsDropDownOpen=true;
    }
    internal void FilterChoices(string query)
    {
        if(changing || ItemsSource is null)return;
        changing=true;
        try
        {
            CollectionViewSource.GetDefaultView(ItemsSource).Filter=item=>item is SetupRegional.Choice choice &&
                (choice.Label.Contains(query,StringComparison.CurrentCultureIgnoreCase) || (choice.Id?.Contains(query,StringComparison.OrdinalIgnoreCase)??false));
            Text=query;
        }
        finally{changing=false;}
    }
    internal void RestoreChoice()
    {
        if(changing || ItemsSource is null)return;
        changing=true;
        try{CollectionViewSource.GetDefaultView(ItemsSource).Filter=null;SelectedItem=Choice;Text=Choice?.Label??"";}
        finally{changing=false;}
    }
    protected override void OnDropDownClosed(EventArgs e){RestoreChoice();base.OnDropDownClosed(e);}
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {base.OnLostKeyboardFocus(e);if(!IsKeyboardFocusWithin && !IsDropDownOpen)RestoreChoice();}
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if(e.Key==Key.Escape){IsDropDownOpen=false;RestoreChoice();e.Handled=true;return;}
        if(e.Key==Key.Enter && IsDropDownOpen && SelectedItem is null && Items.Count==1){SelectedIndex=0;IsDropDownOpen=false;e.Handled=true;return;}
        base.OnPreviewKeyDown(e);
    }
}
