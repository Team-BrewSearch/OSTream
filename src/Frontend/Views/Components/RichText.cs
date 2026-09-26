using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using OSTream.ViewModels;
namespace OSTream.Views.Components;
public static class RichText
{
    public static readonly AttachedProperty<IReadOnlyList<TextPart>?> PartsProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IReadOnlyList<TextPart>?>("Parts", typeof(RichText));
    public static IReadOnlyList<TextPart>? GetParts(TextBlock t) => t.GetValue(PartsProperty);
    public static void SetParts(TextBlock t, IReadOnlyList<TextPart>? value) => t.SetValue(PartsProperty, value);
    static RichText()
    {
        PartsProperty.Changed.AddClassHandler<TextBlock>((block, e) =>
        {
            var inlines = new InlineCollection();
            foreach (var part in GetParts(block) ?? [])
            {
                var run = new Run(part.Separator ? $"  {part.Text}  " : part.Text);
                if (part.Bold) run.Classes.Add("rich-strong");
                else if (part.Separator) run.Classes.Add("rich-separator");
                inlines.Add(run);
            }
            block.Inlines = inlines;
        });
    }
}