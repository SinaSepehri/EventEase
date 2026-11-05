using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace EventEase.Components;

/// <summary>
/// An <see cref="InputText"/> that updates its value on every keystroke ("oninput")
/// instead of when the field loses focus ("onchange").
///
/// Why: putting @bind-Value:event="oninput" on InputText passes ChangeEventArgs where a string is
/// expected, which crashes at runtime. A plain &lt;input @bind:event="oninput"&gt; avoids the crash
/// but bypasses EditContext, so there's no field-level validation and no invalid CSS class.
/// This component gives live updates *and* full form validation.
/// </summary>
public class InputTextOnInput : InputText
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttributeIfNotNullOrEmpty(2, "name", NameAttributeValue);
        builder.AddAttributeIfNotNullOrEmpty(3, "class", CssClass);
        builder.AddAttribute(4, "value", CurrentValueAsString);
        builder.AddAttribute(5, "oninput", EventCallback.Factory.CreateBinder<string?>(
            this, value => CurrentValueAsString = value, CurrentValueAsString));
        builder.SetUpdatesAttributeName("value");
        builder.AddElementReferenceCapture(6, element => Element = element);
        builder.CloseElement();
    }
}

internal static class RenderTreeBuilderExtensions
{
    public static void AddAttributeIfNotNullOrEmpty(this RenderTreeBuilder builder, int sequence, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            builder.AddAttribute(sequence, name, value);
        }
    }
}
