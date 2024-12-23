using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Collections;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;
using static AsmResolver.PE.DotNet.Cil.CilOpCodes;

namespace AwaitFuscator.Engine;

/// <summary>
/// Represents a single type implementing a state machine for an asynchronous method.
/// </summary>
public class AsyncStateMachineType
{
    public AsyncStateMachineType(ObfuscatorContext context, MethodDefinition method)
    {
        var methodBuilder = context.CodeFactory.GetMethodBuilderFactory(method.Signature!.ReturnType);

        var factory = context.TargetModule.CorLibTypeFactory;

        // Define base state machine type definition.
        Definition = new TypeDefinition(
            null,
            context.Parameters.UseAnonymousTypes
                ? $"<{method.Name}>d__0"
                : $"StateMachine_{method.Name}",
            TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit | TypeAttributes.NestedAssembly,
            context.CodeFactory.ValueTypeType
        );

        // Wire interfaces.
        Definition.Interfaces.Add(new InterfaceImplementation(context.CodeFactory.IAsyncStateMachineType));
        Definition.CustomAttributes.Add(new CustomAttribute(context.CodeFactory.CompilerGeneratedAttributeConstructor));

        // Add internal state fields.
        Definition.Fields.Add(StateField = new FieldDefinition("<>1__state", FieldAttributes.Public, factory.Int32));
        Definition.Fields.Add(BuilderField = new FieldDefinition("<>t__builder", FieldAttributes.Public,
            methodBuilder.Type.ToTypeSignature(true)
        ));
        Definition.Fields.Add(ConditionField = new FieldDefinition("<>s__1", FieldAttributes.Private, factory.Boolean));

        // Lift parameters to fields.
        var parameters = new Dictionary<Parameter, FieldDefinition>();
        ParameterFields = parameters;

        if (method.Parameters.ThisParameter is { } thisParameter)
        {
            var field = new FieldDefinition("<>4__this", FieldAttributes.Public, thisParameter.ParameterType);
            Definition.Fields.Add(field);
            parameters[thisParameter] = field;
        }

        foreach (var parameter in method.Parameters)
        {
            var field = new FieldDefinition(parameter.Name, FieldAttributes.Public, parameter.ParameterType);
            Definition.Fields.Add(field);
            parameters[parameter] = field;
        }

        // Add code for standard explicit implemented methods.
        AddSetStateMachineMethod(context, methodBuilder);
    }

    /// <summary>
    /// Gets the type definition that is to be added to the target module.
    /// </summary>
    public TypeDefinition Definition { get; }

    /// <summary>
    /// Gets the field containing the method builder for this state machine.
    /// </summary>
    public FieldDefinition BuilderField { get; }

    /// <summary>
    /// Gets the field containing the current state of this state machine.
    /// </summary>
    public FieldDefinition StateField { get; }

    /// <summary>
    /// Gets a field containing the result of a boolean condition in the MoveNext method.
    /// </summary>
    public FieldDefinition ConditionField { get; }

    /// <summary>
    /// Gets a mapping from parameters to their lifted fields added to the state machine.
    /// </summary>
    public IReadOnlyDictionary<Parameter, FieldDefinition> ParameterFields { get; }

    private void AddSetStateMachineMethod(ObfuscatorContext context, AsyncMethodBuilderFactory methodBuilder)
    {
        var setStateMachine = new MethodDefinition(
            "SetStateMachine",
            MethodAttributes.Private | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
            (MethodSignature?) context.CodeFactory.IAsyncStateMachine_SetStateMachineMethod.Signature
        );

        setStateMachine.CilMethodBody = new CilMethodBody(setStateMachine)
        {
            Instructions =
            {
                // this.SetStateMachine(P_0);
                Ldarg_0,
                {Ldflda, BuilderField},
                Ldarg_1,
                {Call, methodBuilder.SetStateMachineMethod },

                // return;
                Ret
            }
        };

        Definition.MethodImplementations.Add(new MethodImplementation(
            context.CodeFactory.IAsyncStateMachine_SetStateMachineMethod,
            setStateMachine
        ));

        Definition.Methods.Add(setStateMachine);
    }
}