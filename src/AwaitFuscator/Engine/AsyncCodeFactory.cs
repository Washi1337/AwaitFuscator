using System.Runtime.CompilerServices;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;
using static AsmResolver.PE.DotNet.Cil.CilOpCodes;

namespace AwaitFuscator.Engine;

/// <summary>
/// Provides members for easily constructing new code associated with asynchronous methods.
/// </summary>
public class AsyncCodeFactory
{
    private readonly Dictionary<TypeSignature, AsyncMethodBuilderFactory> _methodBuilderFactories = new();

    public AsyncCodeFactory(ModuleDefinition targetModule)
    {
        TargetModule = targetModule;

        var factory = targetModule.CorLibTypeFactory;

        ValueTypeType = factory.CorLibScope
            .CreateTypeReference("System", "ValueType")
            .ImportWith(targetModule.DefaultImporter);

        ActionType = factory.CorLibScope
            .CreateTypeReference("System", "Action")
            .ImportWith(targetModule.DefaultImporter);

        ExceptionType = factory.CorLibScope
            .CreateTypeReference("System", "Exception")
            .ImportWith(targetModule.DefaultImporter);

        INotifyCompletionType = factory.CorLibScope
            .CreateTypeReference("System.Runtime.CompilerServices", "INotifyCompletion")
            .ImportWith(targetModule.DefaultImporter);

        ExtensionAttributeConstructor = factory.CorLibScope
            .CreateTypeReference("System.Runtime.CompilerServices", "ExtensionAttribute")
            .CreateMemberReference(".ctor", MethodSignature.CreateInstance(factory.Void))
            .ImportWith(targetModule.DefaultImporter);

        CompilerGeneratedAttributeConstructor = factory.CorLibScope
            .CreateTypeReference("System.Runtime.CompilerServices", "CompilerGeneratedAttribute")
            .CreateMemberReference(".ctor", MethodSignature.CreateInstance(factory.Void))
            .ImportWith(targetModule.DefaultImporter);

        IAsyncStateMachineType = factory.CorLibScope
            .CreateTypeReference("System.Runtime.CompilerServices", "IAsyncStateMachine")
            .ImportWith(targetModule.DefaultImporter);

        IAsyncStateMachine_MoveNextMethod = IAsyncStateMachineType
            .CreateMemberReference(
                "MoveNext",
                MethodSignature.CreateInstance(TargetModule.CorLibTypeFactory.Void)
            ).ImportWith(targetModule.DefaultImporter);

        IAsyncStateMachine_SetStateMachineMethod = IAsyncStateMachineType
            .CreateMemberReference(
                "SetStateMachine",
                MethodSignature.CreateInstance(
                    TargetModule.CorLibTypeFactory.Void,
                    IAsyncStateMachineType.ToTypeSignature(false)
                )
            ).ImportWith(targetModule.DefaultImporter);
    }

    /// <summary>
    /// Gets the module the factory is targeting.
    /// </summary>
    public ModuleDefinition TargetModule { get; }

    /// <summary>
    /// Gets a reference to <see cref="ValueType"/>.
    /// </summary>
    public ITypeDefOrRef ValueTypeType { get; }

    /// <summary>
    /// Gets a reference to <see cref="Action"/>.
    /// </summary>
    public ITypeDefOrRef ActionType { get; }

    /// <summary>
    /// Gets a reference to <see cref="Exception"/>.
    /// </summary>
    public ITypeDefOrRef ExceptionType { get; }

    /// <summary>
    /// Gets a reference to the parameterless constructor of <see cref="ExtensionAttribute"/>.
    /// </summary>
    public MemberReference ExtensionAttributeConstructor { get; }

    /// <summary>
    /// Gets a reference to the parameterless constructor of <see cref="CompilerGeneratedAttribute"/>.
    /// </summary>
    public MemberReference CompilerGeneratedAttributeConstructor { get; }

    /// <summary>
    /// Gets a reference to the <see cref="INotifyCompletion"/> interface.
    /// </summary>
    public ITypeDefOrRef INotifyCompletionType { get; }

    /// <summary>
    /// Gets a reference to the <see cref="IAsyncStateMachine"/> interface.
    /// </summary>
    public ITypeDefOrRef IAsyncStateMachineType { get; }

    /// <summary>
    /// Gets a reference to the <see cref="IAsyncStateMachine.MoveNext"/> method.
    /// </summary>
    public MemberReference IAsyncStateMachine_MoveNextMethod { get; }

    /// <summary>
    /// Gets a reference to the <see cref="IAsyncStateMachine.SetStateMachine"/> method.
    /// </summary>
    public MemberReference IAsyncStateMachine_SetStateMachineMethod { get; }

    /// <summary>
    /// Obtains the factory for a <see cref="AsyncTaskMethodBuilder{TResult}"/>, initialized to the provided return type.
    /// </summary>
    /// <param name="returnType">The return type to get the factory for.</param>
    /// <returns>The factory.</returns>
    /// <remarks>
    /// This method returns a factory for <see cref="AsyncVoidMethodBuilder"/> if <paramref name="returnType"/> encodes
    /// <see cref="Void"/>.
    /// </remarks>
    public AsyncMethodBuilderFactory GetMethodBuilderFactory(TypeSignature returnType)
    {
        if (!_methodBuilderFactories.TryGetValue(returnType, out var factory))
        {
            factory = new AsyncMethodBuilderFactory(TargetModule, returnType, IAsyncStateMachineType, ExceptionType);
            _methodBuilderFactories.Add(returnType, factory);
        }

        return factory;
    }

    /// <summary>
    /// Creates a new state machine startup stub that is to be placed in an async method.
    /// </summary>
    /// <param name="stateMachineType">The state machine to initialize.</param>
    /// <param name="method">The method to create the stub for.</param>
    /// <returns>The new method body starting the state machine.</returns>
    public CilMethodBody CreateStartupStub(AsyncStateMachineType stateMachineType, MethodDefinition method)
    {
        var returnType = method.Signature!.ReturnType;

        var methodBuilder = GetMethodBuilderFactory(returnType);
        var stateMachineLocal = new CilLocalVariable(stateMachineType.Definition.ToTypeSignature(true));

        var stub = new CilMethodBody(method)
        {
            InitializeLocals = true,
            LocalVariables = { stateMachineLocal }
        };

        var il = stub.Instructions;

        // stateMachine.<>t__builder = AsyncTaskMethodBuilder<T>.Create() / AsyncVoidMethodBuilder.Create();
        il.Add(Ldloca, stateMachineLocal);
        il.Add(Call, methodBuilder.CreateMethod);
        il.Add(Stfld, stateMachineType.BuilderField);

        if (method.Parameters.ThisParameter is { } thisParameter)
        {
            // stateMachine.this = this;
            il.Add(Ldloca, stateMachineLocal);
            il.Add(Ldarg_0);
            il.Add(Stfld, stateMachineType.ParameterFields[thisParameter]);
        }

        foreach (var parameter in method.Parameters)
        {
            // stateMachine.Parameter = parameter;
            il.Add(Ldloca, stateMachineLocal);
            il.Add(Ldarg, parameter);
            il.Add(Stfld, stateMachineType.ParameterFields[parameter]);
        }

        // stateMachine.<>1__state = -1;
        il.Add(Ldloca, stateMachineLocal);
        il.Add(Ldc_I4_M1);

        // stateMachine.<>t__builder.Start(ref stateMachine);
        il.Add(Stfld, stateMachineType.StateField);
        il.Add(Ldloca, stateMachineLocal);
        il.Add(Ldflda, stateMachineType.BuilderField);
        il.Add(Ldloca, stateMachineLocal);
        il.Add(Call, methodBuilder.StartMethod
            .MakeGenericInstanceMethod(stateMachineType.Definition.ToTypeSignature(true))
            .ImportWith(TargetModule.DefaultImporter));

        if (returnType.ElementType != ElementType.Void)
        {
            // return stateMachine.<>t__builder.Task;
            il.Add(Ldloca, stateMachineLocal);
            il.Add(Ldflda, stateMachineType.BuilderField);
            il.Add(Call, methodBuilder.GetTaskMethod);
        }

        // return;
        il.Add(Ret);

        il.OptimizeMacros();

        return stub;
    }

    /// <summary>
    /// Constructs a new GetAwaiter extension method for the provided input type that returns an instance of the
    /// provided awaiter type.
    /// </summary>
    /// <param name="awaiterType">The awaiter type to instantiate.</param>
    /// <param name="inputType">The type of expressions to await.</param>
    /// <returns>The new GetAwaiter method.</returns>
    public MethodDefinition CreateGetAwaiterMethod(AwaiterType awaiterType, TypeSignature inputType)
    {
        var awaiterTypeSig = awaiterType.Definition.ToTypeSignature(true);
        var getAwaiter = new MethodDefinition(
            "GetAwaiter",
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodSignature.CreateStatic(awaiterTypeSig, inputType)
        );

        getAwaiter.CilMethodBody = new CilMethodBody(getAwaiter)
        {
            Instructions =
            {
                Ldarg_0,
                {Newobj, awaiterType.Constructor},
                Ret
            }
        };

        getAwaiter.CustomAttributes.Add(new CustomAttribute(ExtensionAttributeConstructor));

        return getAwaiter;
    }
}