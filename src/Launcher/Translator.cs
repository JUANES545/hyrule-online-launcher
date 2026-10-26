using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace HyruleOnline {
 // Remembers the English text the XAML was written with and puts the current language's text back on every pass.
 // Elements named in Lang.Dynamic have their text set from code, so only their tooltip and automation name are translated here.
 sealed class Translator {
  sealed class Item {
   public DependencyObject Target;
   public string Kind, English;
  }

  readonly List<Item> items = new List<Item>();

  public void Capture(DependencyObject node) {
   var element = node as FrameworkElement;
   bool fromCode = element != null && System.Array.IndexOf(Lang.Dynamic, element.Name) >= 0;
   var text = node as TextBlock;
   if (!fromCode && text != null && !string.IsNullOrEmpty(text.Text)) items.Add(new Item { Target = node, Kind = "Text", English = text.Text });
   var content = node as ContentControl;
   if (!fromCode && content != null && content.Content is string) items.Add(new Item { Target = node, Kind = "Content", English = (string)content.Content });
   if (element != null) {
    if (element.ToolTip is string) items.Add(new Item { Target = node, Kind = "ToolTip", English = (string)element.ToolTip });
    string name = AutomationProperties.GetName(element);
    if (!string.IsNullOrEmpty(name)) items.Add(new Item { Target = node, Kind = "Name", English = name });
   }
   if (fromCode) return;
   foreach (object child in LogicalTreeHelper.GetChildren(node)) {
    var next = child as DependencyObject;
    if (next != null) Capture(next);
   }
  }

  public void Apply() {
   foreach (var item in items) {
    string text = Lang.T(item.English);
    switch (item.Kind) {
     case "Text": ((TextBlock)item.Target).Text = text; break;
     case "Content": ((ContentControl)item.Target).Content = text; break;
     case "ToolTip": ((FrameworkElement)item.Target).ToolTip = text; break;
     default: AutomationProperties.SetName(item.Target, text); break;
    }
   }
  }
 }
}
