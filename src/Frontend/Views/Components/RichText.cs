using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using OSTream.ViewModels;
using System.Collections.Generic;
namespace OSTream.Frontend.Views.Components;

public static class RichText
{
    public static readonly AttachedProperty<IReadOnlyList<TextPart>?> PartsProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IReadOnlyList<TextPart>?>("Parts", typeof(RichText));
    public static IReadOnlyList<TextPart>? GetParts(TextBlock t)
    {
        return t.GetValue(PartsProperty);
    }

    public static void SetParts(TextBlock t, IReadOnlyList<TextPart>? value)
    {
        _ = t.SetValue(PartsProperty, value);
    }

    static RichText()
    {
        _ = PartsProperty.Changed.AddClassHandler<TextBlock>((block, e) =>
        {
            InlineCollection inlines = [];
            foreach (TextPart part in GetParts(block) ?? [])
            {
                Run run = new(part.Separator ? $"  {part.Text}  " : part.Text);
                if (part.Bold)
                {
                    run.Classes.Add("rich-strong");
                }
                else if (part.Separator)
                {
                    run.Classes.Add("rich-separator");
                }

                inlines.Add(run);
            }
            block.Inlines = inlines;
        });
    }
}