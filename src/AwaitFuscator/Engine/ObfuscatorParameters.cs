namespace AwaitFuscator.Engine;

/// <summary>
/// Defines parameters to use for obfuscating a module.
/// </summary>
public class ObfuscatorParameters
{
    /// <summary>
    /// Gets or sets a value indicating whether the obfuscator should introduce types with anonymous names.
    /// </summary>
    public bool UseAnonymousTypes { get; set; } = true;
    
}