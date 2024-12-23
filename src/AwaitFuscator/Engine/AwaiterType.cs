using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;
using static AsmResolver.PE.DotNet.Cil.CilOpCodes;

namespace AwaitFuscator.Engine;

/// <summary>
/// Provides information about a single Awaiter type introduced by AwaitFuscator.
/// </summary>
public class AwaiterType
{
    /// <summary>
    /// Creates a new awaiter type.
    /// </summary>
    /// <param name="context">The context the obfuscator is situated in.</param>
    /// <param name="name">The name of the awaiter type.</param>
    /// <param name="frameType">The type signature referencing the frame type containing all local variables.</param>
    /// <param name="inputType">The type of expressions to generate an awaiter for.</param>
    /// <param name="outputType">The type of values to produce as a result for this awaiter, or <c>null</c> if the type should be its own result.</param>
    /// <remarks>
    /// This populates everything in the awaiter type, except for the implementation of the <c>GetResult</c> method.
    /// </remarks>
    public AwaiterType(
        ObfuscatorContext context,
        string name,
        TypeSignature frameType,
        TypeSignature inputType,
        TypeSignature? outputType)
    {
        // Create a new struct awaiter.
        Definition = new TypeDefinition(
            null, name,
            TypeAttributes.SequentialLayout | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            context.CodeFactory.ValueTypeType
        );

        Definition.Interfaces.Add(new InterfaceImplementation(context.CodeFactory.INotifyCompletionType));
        Definition.CustomAttributes.Add(new CustomAttribute(context.CodeFactory.CompilerGeneratedAttributeConstructor));

        // Add the frame field.
        Definition.Fields.Add(FrameField = new FieldDefinition(
            "Frame",
            FieldAttributes.Public,
            frameType)
        );

        // Add the required methods.
        Constructor = CreateConstructor(context, inputType, frameType);
        GetIsCompletedMethod = CreateIsCompletedProperty(context);
        OnCompletedMethod = CreateOnCompletedMethod(context);
        GetResultMethod = CreateGetResultMethod(context, outputType ?? Definition.ToTypeSignature(true));
    }

    /// <summary>
    /// Gets the awaiter type definition that is to be added to the module.
    /// </summary>
    public TypeDefinition Definition { get; }

    /// <summary>
    /// Gets the constructor method of the awaiter.
    /// </summary>
    public MethodDefinition Constructor { get; }

    /// <summary>
    /// Gets the definition for the <c>get_isCompleted</c> method for this awaiter.
    /// </summary>
    public MethodDefinition GetIsCompletedMethod { get; }

    /// <summary>
    /// Gets the definition for the <c>OnCompleted</c> method for this awaiter.
    /// </summary>
    public MethodDefinition OnCompletedMethod { get; }

    /// <summary>
    /// Gets the definition for the <c>GetResult</c> method for this awaiter.
    /// </summary>
    public MethodDefinition GetResultMethod { get; }

    /// <summary>
    /// Gets the field storing the current frame.
    /// </summary>
    public FieldDefinition FrameField { get; }

    private MethodDefinition CreateConstructor(ObfuscatorContext context, TypeSignature inputType, TypeSignature frameType)
    {
        var result = MethodDefinition.CreateConstructor(context.TargetModule, inputType);

        var il = result.CilMethodBody!.Instructions;
        il.Clear();

        il.Add(Ldarg_0);
        il.Add(Ldarg_1);

        // If the input type is not an object (i.e., another awaiter), we need to extract the current frame from it.
        if (inputType.ElementType != ElementType.Object)
        {
            il.Add(Ldfld, inputType.Resolve()!
                .Fields.First(x => SignatureComparer.Default.Equals(x.Signature!.FieldType, frameType)));
        }

        il.Add(Stfld, FrameField);
        il.Add(Ret);

        Definition.Methods.Add(result);
        return result;
    }

    private MethodDefinition CreateIsCompletedProperty(ObfuscatorContext context)
    {
        var factory = context.TargetModule.CorLibTypeFactory;

        // Define getter method.
        var result = new MethodDefinition(
            "get_IsCompleted",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
            MethodSignature.CreateInstance(factory.Boolean)
        );

        result.MethodBody = new CilMethodBody(result)
        {
            Instructions =
            {
                // return true;
                Ldc_I4_1,
                Ret
            }
        };

        Definition.Methods.Add(result);

        // Define IsCompleted property.
        var isCompleted = new PropertyDefinition("IsCompleted", 0, PropertySignature.CreateInstance(factory.Boolean));
        isCompleted.Semantics.Add(new MethodSemantics(result, MethodSemanticsAttributes.Getter));
        Definition.Properties.Add(isCompleted);

        return result;
    }

    private MethodDefinition CreateOnCompletedMethod(ObfuscatorContext context)
    {
        var factory = context.TargetModule.CorLibTypeFactory;

        var result = new MethodDefinition(
            "OnCompleted",
            MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
            MethodSignature.CreateInstance(
                factory.Void,
                context.CodeFactory.ActionType.ToTypeSignature(false)
            )
        );

        // We don't need to do anything on completion, as we are never an actual asynchronous operation.
        result.MethodBody = new CilMethodBody(result)
        {
            Instructions = {Ret}
        };

        Definition.Methods.Add(result);

        return result;
    }

    private MethodDefinition CreateGetResultMethod(ObfuscatorContext context, TypeSignature outputType)
    {
        var result = new MethodDefinition(
            "GetResult",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodSignature.CreateInstance(outputType)
        );

        // Note: Body is to be filled in by caller.
        result.CilMethodBody = new CilMethodBody(result);

        Definition.Methods.Add(result);
        return result;
    }

}