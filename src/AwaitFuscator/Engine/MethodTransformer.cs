using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Collections;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using Echo.Ast.Construction;
using Echo.Platforms.AsmResolver;

namespace AwaitFuscator.Engine;

/// <summary>
/// Transforms a non-asynchronous method into an asynchronous method.
/// </summary>
public class MethodTransformer
{
    private readonly ObfuscatorContext _context;
    private readonly MethodDefinition _method;
    private readonly AsyncStateMachineType _stateMachine;
    private readonly MoveNextBuilder _moveNextBuilder;

    /// <summary>
    /// Creates a new method transformer.
    /// </summary>
    /// <param name="context">The obfuscator context.</param>
    /// <param name="method">The method to transform its method body for.</param>
    public MethodTransformer(ObfuscatorContext context, MethodDefinition method)
    {
        _context = context;
        _method = method;
        _stateMachine = new AsyncStateMachineType(_context, _method);
        _moveNextBuilder = new MoveNextBuilder(_context, _stateMachine, _method);
    }

    /// <summary>
    /// Applies the transformation to the method.
    /// </summary>
    public void ApplyTransformation()
    {
        var declaringType = _method.DeclaringType!;

        var moveNext = DoTransform();

        // Add MoveNext to the state machine type.
        _stateMachine.Definition.Methods.Add(moveNext);
        _stateMachine.Definition.MethodImplementations.Add(new MethodImplementation(
            _context.CodeFactory.IAsyncStateMachine_MoveNextMethod,
            moveNext
        ));

        // Add fields required by MoveNext to the state machine type.
        foreach (var awaiterField in _moveNextBuilder.GetAwaiterFields())
            _stateMachine.Definition.Fields.Add(awaiterField);
        foreach (var localFields in _moveNextBuilder.GetLocalFields())
            _stateMachine.Definition.Fields.Add(localFields);

        // Add types required by MoveNext to the module.
        foreach (var awaiterType in _moveNextBuilder.GetAwaiterTypes())
        {
            // Add as nested type if required.
            if (awaiterType.IsNestedAssembly)
                declaringType.NestedTypes.Add(awaiterType);
            else
                _context.TargetModule.TopLevelTypes.Add(awaiterType);
        }

        _context.TargetModule.TopLevelTypes.Add(_moveNextBuilder.FrameType);
        declaringType.NestedTypes.Add(_stateMachine.Definition);

        AddExtensionMethodsToModule();

        // Replace the original method body with a state machine startup stub.
        _method.CilMethodBody = _context.CodeFactory.CreateStartupStub(_stateMachine, _method);

        if (_method.Signature!.ReturnsValue)
        {
            // If the method returns a value, we need to move the state machine to a local proxy method
            // so that we can make it return Task<T>.

            var methodBuilder = _context.CodeFactory.GetMethodBuilderFactory(_method.Signature.ReturnType);

            var proxy = MoveToAsyncTaskProxyMethod();
            var local = new CilLocalVariable(methodBuilder.TaskAwaiterType);

            var body = _method.CilMethodBody!;
            body.LocalVariables.Clear();
            body.LocalVariables.Add(local);

            var il = body.Instructions;
            il.Clear();

            // Push all arguments.
            if (!_method.IsStatic)
                il.Add(CilOpCodes.Ldarg_0);

            foreach (var parameter in _method.Parameters)
                il.Add(CilOpCodes.Ldarg, parameter);

            // Call the proxy method, its awaiter and its GetResult method.
            il.Add(CilOpCodes.Call, proxy);
            il.Add(CilOpCodes.Callvirt, methodBuilder.GetAwaiterMethod);
            il.Add(CilOpCodes.Stloc, local);
            il.Add(CilOpCodes.Ldloca, local);
            il.Add(CilOpCodes.Call, methodBuilder.GetResultMethod);
            il.Add(CilOpCodes.Ret);
            il.OptimizeMacros();

            declaringType.Methods.Add(proxy);
        }
    }

    private MethodDefinition DoTransform()
    {
        try
        {
            _moveNextBuilder.Begin();

            var astCfg = _method.CilMethodBody!
                .ConstructStaticFlowGraph()
                .Lift(new CilPurityClassifier());

            _moveNextBuilder.RegisterLabels(astCfg.Nodes.Select(x => (int) x.Offset));

            foreach (var node in astCfg.Nodes)
            {
                _moveNextBuilder.BeginBlock((int) node.Offset);
                foreach (var statement in node.Contents.Instructions)
                    _moveNextBuilder.AppendStatement(statement);
                _moveNextBuilder.EndBlock();
            }

            return _moveNextBuilder.End();
        }
        catch
        {
            _moveNextBuilder.RemoveTemporaryAwaiters();
            throw;
        }
    }

    private void AddExtensionMethodsToModule()
    {
        var extensionsContainer = new TypeDefinition(
            null,
            _context.Parameters.UseAnonymousTypes
                ? $"<>AnonType_{_method.MetadataToken.Rid}"
                : $"Extensions_{_method.MetadataToken}",
            TypeAttributes.Abstract | TypeAttributes.Sealed,
            _context.TargetModule.CorLibTypeFactory.Object.Type
        );
        extensionsContainer.CustomAttributes.Add(new CustomAttribute(_context.CodeFactory.CompilerGeneratedAttributeConstructor));
        _context.TargetModule.TopLevelTypes.Add(extensionsContainer);

        foreach (var getAwaiterMethod in _moveNextBuilder.GetGetAwaiterMethods())
            extensionsContainer.Methods.Add(getAwaiterMethod);
    }

    private MethodDefinition MoveToAsyncTaskProxyMethod()
    {
        var taskType = _context.CodeFactory
            .GetMethodBuilderFactory(_method.Signature!.ReturnType)
            .TaskType;

        var result = new MethodDefinition(
            $"<{_method.Name}>g__{_method.Name}Async|0_0",
            MethodAttributes.Assembly,
            MethodSignature.CreateStatic(taskType, _method.Signature.ParameterTypes)
        );

        result.Signature!.HasThis = _method.Signature.HasThis;
        result.IsStatic = _method.IsStatic;

        // Copy all parameters.
        foreach (var parameter in _method.ParameterDefinitions)
            result.ParameterDefinitions.Add(new ParameterDefinition(parameter.Sequence, parameter.Name, parameter.Attributes));
        result.Parameters.PullUpdatesFromMethodSignature();

        // Add compiler generated attribute.
        result.CustomAttributes.Add(
            new CustomAttribute(_context.CodeFactory.CompilerGeneratedAttributeConstructor)
        );

        var originalBody = _method.CilMethodBody!;
        result.CilMethodBody = new CilMethodBody(result);

        // Move all locals from the original method to the new one.
        foreach (var local in originalBody.LocalVariables.ToArray())
        {
            originalBody.LocalVariables.Remove(local);
            result.CilMethodBody.LocalVariables.Add(local);
        }

        // Copy all instructions, but map the parameters to the new parameters.
        foreach (var instruction in originalBody.Instructions)
        {
            if (instruction.Operand is Parameter p)
                instruction.Operand = result.Parameters[p.Index];

            result.CilMethodBody.Instructions.Add(instruction);
        }

        return result;
    }
}