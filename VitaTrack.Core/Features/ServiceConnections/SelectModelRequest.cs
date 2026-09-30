using System.ComponentModel.DataAnnotations;

namespace VitaTrack.Core.Features.ServiceConnections;

/// <summary>
/// What the model picker binds: the model to send, and the reasoning effort to send it
/// at. A model is required because a connection with no model is the one state
/// enrichment refuses, and refusing it at the picker is cheaper than refusing it at the
/// request.
/// <para>
/// The variant vocabulary is checked here, against
/// <see cref="ServiceDescriptorRegistry.Variants"/>, rather than by a second list of
/// the seven values. A value outside the vocabulary is a <c>reasoning_effort</c> the
/// providers reject — at request time, long after the user thought they had chosen —
/// and <c>/v1/models</c> carries nothing to discover it from, so this is the only place
/// a bad one can be caught.
/// </para>
/// </summary>
public class SelectModelRequest : IValidatableObject
{
    public const string ModelRequired = "Model is required.";

    public const string UnknownVariant = "Reasoning effort is not one this app offers. Choose one from the list.";

    [Display(Name = "Model")]
    [Required(ErrorMessage = ModelRequired)]
    public string Model { get; set; } = string.Empty;

    [Display(Name = "Reasoning effort")]
    [Required(ErrorMessage = UnknownVariant)]
    public string Variant { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Blank is [Required]'s business, and reporting both would put two messages on
        // one field for one mistake.
        if (string.IsNullOrWhiteSpace(Variant)) yield break;

        if (ServiceDescriptorRegistry.FindVariant(Variant) is null)
            yield return new ValidationResult(UnknownVariant, [nameof(Variant)]);
    }
}
