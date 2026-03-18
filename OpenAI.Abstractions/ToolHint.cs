namespace OpenAI;

/// <summary>
/// Base type for structured UI hints that tools emit alongside their content.
/// The UI layer pattern-matches on the concrete subtype to render an appropriate widget.
/// </summary>
public abstract record ToolHint;

/// <summary>
/// Emitted by image-generation tools to describe current rendering progress.
/// </summary>
/// <param name="Progress">Completion percentage in the range [0, 100].</param>
/// <param name="Step">Current sampling step (0 when not yet started).</param>
/// <param name="TotalSteps">Total sampling steps (0 when not yet known).</param>
public record ImageGenerationHint(double Progress, int Step, int TotalSteps) : ToolHint;
