using AsmResolver.DotNet;

namespace AwaitFuscator.Engine;

/// <summary>
/// Provides a context for an obfuscator.
/// </summary>
public sealed class ObfuscatorContext
{
    public ObfuscatorContext(ModuleDefinition targetModule, ObfuscatorParameters parameters)
    {
        TargetModule = targetModule;
        Parameters = parameters;
        CodeFactory = new AsyncCodeFactory(targetModule);
    }

    /// <summary>
    /// Gets the target module to obfuscate.
    /// </summary>
    public ModuleDefinition TargetModule
    {
        get;
    }

    /// <summary>
    /// Gets the parameters to use during the obfuscation process.
    /// </summary>
    public ObfuscatorParameters Parameters
    {
        get;
    }

    /// <summary>
    /// Gets a factory for constructing new async code.
    /// </summary>
    public AsyncCodeFactory CodeFactory
    {
        get;
    }

}